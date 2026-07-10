using Microsoft.EntityFrameworkCore;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Domain.Entities.Inventory;
using PharmaPOS.Domain.Entities.Masters;
using PharmaPOS.Domain.Entities.Sales;
using PharmaPOS.Domain.Entities.System;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Application.Features.Settings;
using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.Sales;

/// <summary>
/// Default billing service. Prices are treated as MRP / GST-inclusive (the norm for
/// Indian pharmacy retail): tax is extracted from the price rather than added on top,
/// so an invoice total never exceeds the printed MRP.
/// </summary>
public class SalesService : ISalesService
{
    private const int RewardPointsPerRupee = 100; // 1 point per ₹100 spent

    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _clock;
    private readonly ISettingsService _settings;

    public SalesService(IUnitOfWork uow, IDateTimeProvider clock, ISettingsService settings)
    {
        _uow = uow;
        _clock = clock;
        _settings = settings;
    }

    public async Task<List<MedicineLookupDto>> SearchMedicinesAsync(string term, int? branchId, CancellationToken ct = default)
    {
        term = (term ?? string.Empty).Trim();
        if (term.Length < 2) return new();

        // With 280k+ rows, prefix search on the indexed Name column is fast;
        // fall back to contains only when prefix returns nothing.
        var baseQuery = _uow.Repository<Medicine>().Query().AsNoTracking()
            .Where(m => m.Status == EntityStatus.Active);

        var medicines = await baseQuery
            .Where(m => EF.Functions.Like(m.Name, term + "%") ||
                        (m.Barcode != null && m.Barcode == term))
            .OrderBy(m => m.Name)
            .Take(25)
            .Select(m => new MedicineSearchRow(
                m.Id, m.Name, m.GenericName, m.Barcode,
                m.GstPercent, m.DefaultDiscountPercent, m.PrescriptionRequired))
            .ToListAsync(ct);

        if (medicines.Count == 0)
        {
            medicines = await baseQuery
                .Where(m => EF.Functions.Like(m.Name, "%" + term + "%"))
                .OrderBy(m => m.Name)
                .Take(25)
                .Select(m => new MedicineSearchRow(
                    m.Id, m.Name, m.GenericName, m.Barcode,
                    m.GstPercent, m.DefaultDiscountPercent, m.PrescriptionRequired))
                .ToListAsync(ct);
        }

        if (medicines.Count == 0) return new();

        var ids = medicines.Select(m => m.Id).ToList();
        var stockMap = await GetStockByMedicineIdsAsync(ids, branchId, ct);

        return medicines.Select(m => new MedicineLookupDto(
            m.Id, m.Name, m.GenericName, m.Barcode,
            m.GstPercent, m.DefaultDiscountPercent, m.PrescriptionRequired,
            stockMap.TryGetValue(m.Id, out var stock) ? stock : 0m)).ToList();
    }

    private async Task<Dictionary<int, decimal>> GetStockByMedicineIdsAsync(
        List<int> medicineIds, int? branchId, CancellationToken ct)
    {
        var q = _uow.Repository<MedicineBatch>().Query().AsNoTracking()
            .Where(b => medicineIds.Contains(b.MedicineId) && b.QuantityAvailable > 0);
        if (branchId.HasValue) q = q.Where(b => b.BranchId == branchId);

        return await q
            .GroupBy(b => b.MedicineId)
            .Select(g => new { MedicineId = g.Key, Stock = g.Sum(x => x.QuantityAvailable) })
            .ToDictionaryAsync(x => x.MedicineId, x => x.Stock, ct);
    }

    private sealed record MedicineSearchRow(
        int Id, string Name, string? GenericName, string? Barcode,
        decimal GstPercent, decimal DefaultDiscountPercent, bool PrescriptionRequired);

    public async Task<List<BatchLookupDto>> GetBatchesAsync(int medicineId, int? branchId, CancellationToken ct = default)
    {
        var q = _uow.Repository<MedicineBatch>().Query()
            .Where(b => b.MedicineId == medicineId && b.QuantityAvailable > 0);
        if (branchId.HasValue) q = q.Where(b => b.BranchId == branchId);

        var batches = await q
            .OrderBy(b => b.ExpiryDate)
            .Select(b => new BatchLookupDto(
                b.Id, b.BatchNumber, b.ExpiryDate, b.QuantityAvailable,
                b.Mrp, b.SellingPrice, b.GstPercent))
            .ToListAsync(ct);

        // Imported catalogue medicines often have no batches yet — provision an
        // opening batch from the master record so billing can proceed.
        if (batches.Count == 0)
            batches = await EnsureOpeningBatchAsync(medicineId, branchId, ct);

        return batches;
    }

    private async Task<List<BatchLookupDto>> EnsureOpeningBatchAsync(int medicineId, int? branchId, CancellationToken ct)
    {
        var medicine = await _uow.Repository<Medicine>().GetByIdAsync(medicineId, ct);
        if (medicine is null) return new();

        var existing = await _uow.Repository<MedicineBatch>().Query()
            .FirstOrDefaultAsync(b => b.MedicineId == medicineId &&
                                      b.BranchId == branchId &&
                                      b.BatchNumber == "OPENING", ct);
        if (existing is not null)
        {
            return new List<BatchLookupDto>
            {
                new(existing.Id, existing.BatchNumber, existing.ExpiryDate,
                    existing.QuantityAvailable, existing.Mrp, existing.SellingPrice, existing.GstPercent)
            };
        }

        var batch = new MedicineBatch
        {
            MedicineId = medicineId,
            BranchId = branchId,
            BatchNumber = "OPENING",
            ExpiryDate = _clock.Today.AddYears(2),
            QuantityAvailable = 99_999,
            PurchasePrice = medicine.PurchasePrice,
            Mrp = medicine.Mrp,
            SellingPrice = medicine.SellingPrice > 0 ? medicine.SellingPrice : medicine.Mrp,
            GstPercent = medicine.GstPercent
        };
        await _uow.Repository<MedicineBatch>().AddAsync(batch, ct);
        await _uow.SaveChangesAsync(ct);

        return new List<BatchLookupDto>
        {
            new(batch.Id, batch.BatchNumber, batch.ExpiryDate, batch.QuantityAvailable,
                batch.Mrp, batch.SellingPrice, batch.GstPercent)
        };
    }

    public async Task<List<CustomerLookupDto>> SearchCustomersAsync(string term, CancellationToken ct = default)
    {
        term = (term ?? string.Empty).Trim();
        if (term.Length < 1) return new();

        return await _uow.Repository<Customer>().Query()
            .Where(c => c.Status == EntityStatus.Active &&
                        (c.Name.Contains(term) || (c.Phone != null && c.Phone.Contains(term))))
            .OrderBy(c => c.Name)
            .Take(25)
            .Select(c => new CustomerLookupDto(c.Id, c.Name, c.Phone, c.Type, c.OutstandingBalance, c.CreditLimit))
            .ToListAsync(ct);
    }

    public async Task<List<DoctorLookupDto>> SearchDoctorsAsync(string term, CancellationToken ct = default)
    {
        term = (term ?? string.Empty).Trim();
        var q = _uow.Repository<Doctor>().Query().Where(d => d.Status == EntityStatus.Active);
        if (term.Length >= 1) q = q.Where(d => d.Name.Contains(term));

        return await q.OrderBy(d => d.Name).Take(25)
            .Select(d => new DoctorLookupDto(d.Id, d.Name, d.Specialization))
            .ToListAsync(ct);
    }

    public async Task<Result<SaleReceiptDto>> CreateSaleAsync(CreateSaleRequest request, int? branchId, CancellationToken ct = default)
    {
        if (request.Lines.Count == 0)
            return Result.Failure<SaleReceiptDto>("Add at least one item to the bill.");

        try
        {
            var sale = await _uow.ExecuteInTransactionAsync(
                token => BuildAndPersistSaleAsync(request, branchId, token), ct);

            // Receipt is composed from committed data via read-only queries.
            return await BuildReceiptAsync(sale, ct);
        }
        catch (BillingException bex)
        {
            return Result.Failure<SaleReceiptDto>(bex.Message);
        }
        catch (Exception ex)
        {
            return Result.Failure<SaleReceiptDto>($"Could not save the invoice: {ex.Message}");
        }
    }

    public async Task<Result<SaleReceiptDto>> UpdateSaleAsync(UpdateSaleRequest request, int? branchId, CancellationToken ct = default)
    {
        if (request.Lines.Count == 0)
            return Result.Failure<SaleReceiptDto>("Add at least one item to the bill.");

        try
        {
            var sale = await _uow.ExecuteInTransactionAsync(
                token => UpdateAndPersistSaleAsync(request, branchId, token), ct);

            return await BuildReceiptAsync(sale, ct);
        }
        catch (BillingException bex)
        {
            return Result.Failure<SaleReceiptDto>(bex.Message);
        }
        catch (Exception ex)
        {
            return Result.Failure<SaleReceiptDto>($"Could not update the invoice: {ex.Message}");
        }
    }

    public Task<List<SaleListItemDto>> ListBillsAsync(int? branchId, CancellationToken ct = default)
    {
        var q = _uow.Repository<Sale>().Query().AsNoTracking()
            .Where(s => s.Status == SaleStatus.Completed);
        if (branchId.HasValue) q = q.Where(s => s.BranchId == branchId);

        return q.OrderByDescending(s => s.InvoiceDate)
            .ThenByDescending(s => s.Id)
            .Select(s => new SaleListItemDto(
                s.Id,
                s.InvoiceNumber,
                s.InvoiceDate,
                s.BillingCustomerName ?? (s.Customer != null ? s.Customer.Name : null)))
            .ToListAsync(ct);
    }

    public async Task<List<string>> SuggestPatientNamesAsync(string term, int? branchId, CancellationToken ct = default)
    {
        term = (term ?? string.Empty).Trim();
        if (term.Length < 1) return new();

        var saleQuery = _uow.Repository<Sale>().Query().AsNoTracking()
            .Where(s => s.Status == SaleStatus.Completed &&
                        s.BillingCustomerName != null &&
                        EF.Functions.Like(s.BillingCustomerName, term + "%"));
        if (branchId.HasValue) saleQuery = saleQuery.Where(s => s.BranchId == branchId);

        var saleNames = await saleQuery
            .Select(s => s.BillingCustomerName!)
            .Distinct()
            .Take(15)
            .ToListAsync(ct);

        var customerNames = await _uow.Repository<Customer>().Query().AsNoTracking()
            .Where(c => c.Status == EntityStatus.Active &&
                        EF.Functions.Like(c.Name, term + "%"))
            .Select(c => c.Name)
            .Take(15)
            .ToListAsync(ct);

        return saleNames
            .Concat(customerNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();
    }

    public Task<List<BillSearchResultDto>> SearchBillsAsync(
        BillSearchType type, string term, int? branchId, CancellationToken ct = default)
    {
        term = (term ?? string.Empty).Trim();
        return type switch
        {
            BillSearchType.PatientName => SearchBillsByPatientAsync(term, branchId, ct),
            BillSearchType.MobileNumber => SearchBillsByMobileAsync(term, branchId, ct),
            BillSearchType.MedicineName => SearchBillsByMedicineAsync(term, branchId, ct),
            _ => Task.FromResult(new List<BillSearchResultDto>())
        };
    }

    private Task<List<BillSearchResultDto>> SearchBillsByPatientAsync(string term, int? branchId, CancellationToken ct)
    {
        if (term.Length < 1) return Task.FromResult(new List<BillSearchResultDto>());

        var q = _uow.Repository<Sale>().Query().AsNoTracking()
            .Where(s => s.Status == SaleStatus.Completed);
        if (branchId.HasValue) q = q.Where(s => s.BranchId == branchId);

        q = q.Where(s =>
            (s.BillingCustomerName != null && EF.Functions.Like(s.BillingCustomerName, "%" + term + "%")) ||
            (s.Customer != null && EF.Functions.Like(s.Customer.Name, "%" + term + "%")));

        return q.OrderByDescending(s => s.InvoiceDate)
            .ThenByDescending(s => s.Id)
            .Take(50)
            .Select(s => new BillSearchResultDto(
                s.Id,
                s.InvoiceNumber,
                s.InvoiceDate,
                s.BillingCustomerName ?? (s.Customer != null ? s.Customer.Name : null),
                s.BillingCustomerPhone ?? (s.Customer != null ? s.Customer.Phone : null),
                null))
            .ToListAsync(ct);
    }

    private Task<List<BillSearchResultDto>> SearchBillsByMobileAsync(string term, int? branchId, CancellationToken ct)
    {
        if (term.Length < 3) return Task.FromResult(new List<BillSearchResultDto>());

        var q = _uow.Repository<Sale>().Query().AsNoTracking()
            .Where(s => s.Status == SaleStatus.Completed);
        if (branchId.HasValue) q = q.Where(s => s.BranchId == branchId);

        q = q.Where(s =>
            (s.BillingCustomerPhone != null && s.BillingCustomerPhone.Contains(term)) ||
            (s.Customer != null && s.Customer.Phone != null && s.Customer.Phone.Contains(term)));

        return q.OrderByDescending(s => s.InvoiceDate)
            .ThenByDescending(s => s.Id)
            .Take(50)
            .Select(s => new BillSearchResultDto(
                s.Id,
                s.InvoiceNumber,
                s.InvoiceDate,
                s.BillingCustomerName ?? (s.Customer != null ? s.Customer.Name : null),
                s.BillingCustomerPhone ?? (s.Customer != null ? s.Customer.Phone : null),
                null))
            .ToListAsync(ct);
    }

    private Task<List<BillSearchResultDto>> SearchBillsByMedicineAsync(string term, int? branchId, CancellationToken ct)
    {
        if (term.Length < 2) return Task.FromResult(new List<BillSearchResultDto>());

        var q = _uow.Repository<Sale>().Query().AsNoTracking()
            .Where(s => s.Status == SaleStatus.Completed);
        if (branchId.HasValue) q = q.Where(s => s.BranchId == branchId);

        q = q.Where(s => s.Items.Any(i =>
            i.Medicine != null &&
            (EF.Functions.Like(i.Medicine.Name, term + "%") ||
             EF.Functions.Like(i.Medicine.Name, "%" + term + "%"))));

        return q.OrderByDescending(s => s.InvoiceDate)
            .ThenByDescending(s => s.Id)
            .Take(50)
            .Select(s => new BillSearchResultDto(
                s.Id,
                s.InvoiceNumber,
                s.InvoiceDate,
                s.BillingCustomerName ?? (s.Customer != null ? s.Customer.Name : null),
                s.BillingCustomerPhone ?? (s.Customer != null ? s.Customer.Phone : null),
                s.Items
                    .Where(i => i.Medicine != null &&
                                (EF.Functions.Like(i.Medicine.Name, term + "%") ||
                                 EF.Functions.Like(i.Medicine.Name, "%" + term + "%")))
                    .Select(i => i.Medicine!.Name)
                    .FirstOrDefault()))
            .ToListAsync(ct);
    }

    public async Task<Result<SaleEditDto>> GetSaleForEditAsync(int saleId, int? branchId, CancellationToken ct = default)
    {
        var sale = await _uow.Repository<Sale>().Query()
            .Include(s => s.Items)
            .Include(s => s.Payments)
            .FirstOrDefaultAsync(s => s.Id == saleId && s.Status == SaleStatus.Completed, ct);

        if (sale is null)
            return Result.Failure<SaleEditDto>("Invoice not found.");
        if (branchId.HasValue && sale.BranchId != branchId)
            return Result.Failure<SaleEditDto>("Invoice belongs to another branch.");

        var medIds = sale.Items.Select(i => i.MedicineId).Distinct().ToList();
        var medNames = await _uow.Repository<Medicine>().Query().AsNoTracking()
            .Where(m => medIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.Name, ct);

        var batchIds = sale.Items
            .Where(i => i.MedicineBatchId.HasValue)
            .Select(i => i.MedicineBatchId!.Value)
            .Distinct()
            .ToList();
        var batches = await _uow.Repository<MedicineBatch>().Query()
            .Where(b => batchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, ct);

        var dto = new SaleEditDto
        {
            SaleId = sale.Id,
            InvoiceNumber = sale.InvoiceNumber,
            InvoiceDate = sale.InvoiceDate,
            BillingCustomerName = sale.BillingCustomerName,
            BillingCustomerPhone = sale.BillingCustomerPhone,
            BillingCustomerAddress = sale.BillingCustomerAddress,
            BillingDoctorName = sale.BillingDoctorName,
            PaymentMethod = sale.Payments.OrderByDescending(p => p.Amount).FirstOrDefault()?.Method ?? PaymentMethod.Cash,
            Lines = sale.Items.Select(item =>
            {
                var batchQty = item.MedicineBatchId is int bid && batches.TryGetValue(bid, out var batch)
                    ? batch.QuantityAvailable
                    : 0m;
                return new SaleEditLineDto
                {
                    MedicineId = item.MedicineId,
                    MedicineBatchId = item.MedicineBatchId ?? 0,
                    MedicineName = medNames.TryGetValue(item.MedicineId, out var name) ? name : "Medicine",
                    BatchNumber = item.BatchNumber ?? string.Empty,
                    ExpiryDate = item.ExpiryDate,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    Mrp = item.Mrp,
                    GstPercent = item.GstPercent,
                    DiscountPercent = item.DiscountPercent,
                    AvailableStock = batchQty + item.Quantity
                };
            }).ToList()
        };

        return Result.Success(dto);
    }

    private async Task<Sale> UpdateAndPersistSaleAsync(UpdateSaleRequest request, int? branchId, CancellationToken ct)
    {
        var sale = await _uow.Repository<Sale>().Query()
            .Include(s => s.Items)
            .Include(s => s.Payments)
            .FirstOrDefaultAsync(s => s.Id == request.SaleId && s.Status == SaleStatus.Completed, ct);

        if (sale is null)
            throw new BillingException("Invoice not found or cannot be edited.");
        if (branchId.HasValue && sale.BranchId != branchId)
            throw new BillingException("Invoice belongs to another branch.");

        await RestoreSaleStockAsync(sale, branchId, ct);

        foreach (var oldItem in sale.Items.ToList())
            _uow.Repository<SaleItem>().Remove(oldItem);
        sale.Items.Clear();

        foreach (var oldPayment in sale.Payments.ToList())
            _uow.Repository<SalePayment>().Remove(oldPayment);
        sale.Payments.Clear();

        sale.CustomerId = request.CustomerId;
        sale.DoctorId = request.DoctorId;
        sale.BillingCustomerName = request.BillingCustomerName;
        sale.BillingCustomerPhone = request.BillingCustomerPhone;
        sale.BillingCustomerAddress = request.BillingCustomerAddress;
        sale.BillingDoctorName = request.BillingDoctorName;
        sale.PrescriptionPath = request.PrescriptionPath;
        sale.Remarks = request.Remarks;
        sale.RewardPointsRedeemed = request.RewardPointsRedeemed;

        await ApplySaleLinesAsync(sale, request.Lines, branchId, ct);
        ApplySalePayments(sale, request.Payments);

        _uow.Repository<Sale>().Update(sale);
        await _uow.SaveChangesAsync(ct);
        return sale;
    }

    private async Task RestoreSaleStockAsync(Sale sale, int? branchId, CancellationToken ct)
    {
        foreach (var item in sale.Items)
        {
            if (item.MedicineBatchId is not int batchId) continue;

            var batch = await _uow.Repository<MedicineBatch>().GetByIdAsync(batchId, ct);
            if (batch is null) continue;

            batch.QuantityAvailable += item.Quantity;
            _uow.Repository<MedicineBatch>().Update(batch);

            await _uow.Repository<StockMovement>().AddAsync(new StockMovement
            {
                BranchId = branchId,
                MedicineId = item.MedicineId,
                MedicineBatchId = batchId,
                MovementType = StockMovementType.SaleReturn,
                Quantity = item.Quantity,
                BalanceAfter = batch.QuantityAvailable,
                UnitCost = batch.PurchasePrice,
                ReferenceType = nameof(Sale),
                ReferenceId = sale.Id,
                MovementDateUtc = _clock.UtcNow,
                Remarks = $"Reversal for edit {sale.InvoiceNumber}"
            }, ct);
        }
    }

    private async Task<Sale> BuildAndPersistSaleAsync(CreateSaleRequest request, int? branchId, CancellationToken ct)
    {
        var sale = new Sale
        {
            InvoiceDate = _clock.Now,
            BranchId = branchId,
            CustomerId = request.CustomerId,
            DoctorId = request.DoctorId,
            BillingCustomerName = request.BillingCustomerName,
            BillingCustomerPhone = request.BillingCustomerPhone,
            BillingCustomerAddress = request.BillingCustomerAddress,
            BillingDoctorName = request.BillingDoctorName,
            PrescriptionPath = request.PrescriptionPath,
            Remarks = request.Remarks,
            Status = SaleStatus.Completed,
            RewardPointsRedeemed = request.RewardPointsRedeemed
        };

        await ApplySaleLinesAsync(sale, request.Lines, branchId, ct);
        ApplySalePayments(sale, request.Payments);

        sale.InvoiceNumber = await GenerateInvoiceNumberAsync(branchId, ct);

        await _uow.Repository<Sale>().AddAsync(sale, ct);

        if (request.CustomerId is int custId)
        {
            var customer = await _uow.Repository<Customer>().GetByIdAsync(custId, ct);
            if (customer is not null)
            {
                var paid = request.Payments.Sum(p => p.Amount);
                var due = sale.GrandTotal - Math.Min(paid, sale.GrandTotal);
                if (due > 0) customer.OutstandingBalance += due;
                customer.RewardPoints += sale.RewardPointsEarned - request.RewardPointsRedeemed;
                if (customer.RewardPoints < 0) customer.RewardPoints = 0;
                _uow.Repository<Customer>().Update(customer);
            }
        }

        await _uow.SaveChangesAsync(ct);
        return sale;
    }

    private async Task ApplySaleLinesAsync(Sale sale, List<SaleLineRequest> lines, int? branchId, CancellationToken ct)
    {
        decimal subTotal = 0m, totalDiscount = 0m, totalTaxable = 0m, totalTax = 0m;

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
                throw new BillingException("Quantity must be greater than zero.");

            var batch = await _uow.Repository<MedicineBatch>().GetByIdAsync(line.MedicineBatchId, ct);
            if (batch is null)
                throw new BillingException("A selected batch no longer exists.");
            if (batch.QuantityAvailable < line.Quantity)
                throw new BillingException($"Insufficient stock for batch {batch.BatchNumber} (available {batch.QuantityAvailable}).");

            var medicine = await _uow.Repository<Medicine>().GetByIdAsync(line.MedicineId, ct);

            var gstPercent = line.UnitPrice > 0 ? batch.GstPercent : 0m;
            var gross = line.UnitPrice * line.Quantity;
            var discountAmount = Math.Round(gross * line.DiscountPercent / 100m, 2);
            var netInclusive = gross - discountAmount;
            var taxable = Math.Round(netInclusive / (1 + gstPercent / 100m), 2);
            var taxAmount = netInclusive - taxable;

            sale.Items.Add(new SaleItem
            {
                MedicineId = line.MedicineId,
                MedicineBatchId = batch.Id,
                BatchNumber = batch.BatchNumber,
                ExpiryDate = batch.ExpiryDate,
                Quantity = line.Quantity,
                Mrp = line.UnitPrice,
                UnitPrice = line.UnitPrice,
                DiscountPercent = line.DiscountPercent,
                DiscountAmount = discountAmount,
                GstPercent = gstPercent,
                TaxableAmount = taxable,
                TaxAmount = taxAmount,
                LineTotal = netInclusive
            });

            batch.QuantityAvailable -= line.Quantity;
            _uow.Repository<MedicineBatch>().Update(batch);

            await _uow.Repository<StockMovement>().AddAsync(new StockMovement
            {
                BranchId = branchId,
                MedicineId = line.MedicineId,
                MedicineBatchId = batch.Id,
                MovementType = StockMovementType.SaleOut,
                Quantity = -line.Quantity,
                BalanceAfter = batch.QuantityAvailable,
                UnitCost = batch.PurchasePrice,
                ReferenceType = nameof(Sale),
                ReferenceId = sale.Id > 0 ? sale.Id : null,
                MovementDateUtc = _clock.UtcNow,
                Remarks = medicine?.Name
            }, ct);

            subTotal += gross;
            totalDiscount += discountAmount;
            totalTaxable += taxable;
            totalTax += taxAmount;
        }

        var netTotal = totalTaxable + totalTax;
        var rounded = Math.Round(netTotal, 0, MidpointRounding.AwayFromZero);
        var roundOff = rounded - netTotal;

        sale.SubTotal = subTotal;
        sale.DiscountAmount = totalDiscount;
        sale.TaxableAmount = totalTaxable;
        sale.CgstAmount = Math.Round(totalTax / 2m, 2);
        sale.SgstAmount = totalTax - sale.CgstAmount;
        sale.IgstAmount = 0m;
        sale.RoundOff = roundOff;
        sale.GrandTotal = rounded;
        sale.RewardPointsEarned = (int)(rounded / RewardPointsPerRupee);
    }

    private void ApplySalePayments(Sale sale, List<SalePaymentRequest> payments)
    {
        var paid = payments.Sum(p => p.Amount);
        sale.PaidAmount = paid;
        sale.ChangeReturned = paid > sale.GrandTotal ? paid - sale.GrandTotal : 0m;
        sale.PaymentStatus = paid >= sale.GrandTotal ? PaymentStatus.Paid
            : paid > 0 ? PaymentStatus.PartiallyPaid : PaymentStatus.Unpaid;

        foreach (var p in payments)
        {
            sale.Payments.Add(new SalePayment
            {
                Method = p.Method,
                Amount = p.Amount,
                ReferenceNumber = p.ReferenceNumber,
                PaymentDateUtc = _clock.UtcNow
            });
        }
    }

    /// <summary>Signals a recoverable, user-facing billing validation failure.</summary>
    private sealed class BillingException : Exception
    {
        public BillingException(string message) : base(message) { }
    }

    public Task<string> PreviewNextInvoiceNumberAsync(int? branchId, CancellationToken ct = default)
        => GenerateInvoiceNumberAsync(branchId, ct);

    private async Task<string> GenerateInvoiceNumberAsync(int? branchId, CancellationToken ct)
    {
        var today = _clock.Today;
        var tomorrow = today.AddDays(1);
        var q = _uow.Repository<Sale>().Query()
            .Where(s => s.InvoiceDate >= today && s.InvoiceDate < tomorrow);
        if (branchId.HasValue) q = q.Where(s => s.BranchId == branchId);

        var todayCount = await q.CountAsync(ct);
        var prefs = await _settings.GetPreferencesAsync(ct);
        var prefix = string.IsNullOrWhiteSpace(prefs.SalesInvoicePrefix) ? "INV" : prefs.SalesInvoicePrefix.Trim();
        return $"{prefix}-{today:yyyyMMdd}-{todayCount + 1:D4}";
    }

    private async Task<Result<SaleReceiptDto>> BuildReceiptAsync(Sale sale, CancellationToken ct)
    {
        var company = await _uow.Repository<CompanyProfile>().Query().FirstOrDefaultAsync(ct);

        string customerName = sale.BillingCustomerName ?? "Walk-in Customer";
        string? customerPhone = sale.BillingCustomerPhone;
        if (sale.CustomerId is int cid)
        {
            var c = await _uow.Repository<Customer>().GetByIdAsync(cid, ct);
            if (c is not null) { customerName = c.Name; customerPhone = c.Phone; }
        }

        string? doctorName = sale.BillingDoctorName;
        if (sale.DoctorId is int did)
            doctorName = (await _uow.Repository<Doctor>().GetByIdAsync(did, ct))?.Name;

        var medIds = sale.Items.Select(i => i.MedicineId).Distinct().ToList();
        var medNames = await _uow.Repository<Medicine>().Query()
            .Where(m => medIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.Name, ct);

        var receipt = new SaleReceiptDto
        {
            SaleId = sale.Id,
            InvoiceNumber = sale.InvoiceNumber,
            InvoiceDate = sale.InvoiceDate,
            CompanyName = company?.CompanyName ?? "PharmaPOS Medical Store",
            CompanyAddress = company is null ? null : string.Join(", ",
                new[] { company.Address, company.City, company.State, company.Pincode }
                    .Where(s => !string.IsNullOrWhiteSpace(s))),
            CompanyPhone = company?.Phone,
            CompanyGst = company?.GstNumber,
            CompanyDrugLicense = company?.DrugLicenseNumber,
            InvoiceFooter = company?.InvoiceFooter,
            CustomerName = customerName,
            CustomerPhone = customerPhone,
            DoctorName = doctorName,
            SubTotal = sale.SubTotal,
            DiscountAmount = sale.DiscountAmount,
            TaxableAmount = sale.TaxableAmount,
            CgstAmount = sale.CgstAmount,
            SgstAmount = sale.SgstAmount,
            RoundOff = sale.RoundOff,
            GrandTotal = sale.GrandTotal,
            PaidAmount = sale.PaidAmount,
            ChangeReturned = sale.ChangeReturned,
            RewardPointsEarned = sale.RewardPointsEarned
        };

        int sr = 1;
        foreach (var i in sale.Items)
        {
            receipt.Lines.Add(new SaleReceiptLineDto(
                sr++,
                medNames.TryGetValue(i.MedicineId, out var n) ? n : $"#{i.MedicineId}",
                i.BatchNumber ?? string.Empty,
                i.ExpiryDate,
                i.Quantity,
                i.Mrp,
                i.DiscountPercent,
                i.GstPercent,
                i.LineTotal));
        }

        return Result.Success(receipt);
    }
}
