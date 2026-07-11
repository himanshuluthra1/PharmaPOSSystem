using Microsoft.EntityFrameworkCore;
using PharmaPOS.Application.Common;
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
        var normalized = SearchQueryExtensions.NormalizeTerm(term);
        if (normalized.Length < 2) return new();

        var baseQuery = _uow.Repository<Medicine>().Query().AsNoTracking()
            .Where(m => m.Status == EntityStatus.Active);

        var medicines = await baseQuery
            .WhereMedicineMatches(normalized, prefixOnly: true)
            .OrderBy(m => m.Name)
            .Take(25)
            .Select(m => new MedicineSearchRow(
                m.Id, m.Name, m.GenericName, m.Barcode,
                m.GstPercent, m.DefaultDiscountPercent, m.PrescriptionRequired))
            .ToListAsync(ct);

        if (medicines.Count == 0)
        {
            medicines = await baseQuery
                .WhereMedicineMatches(normalized, prefixOnly: false)
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

    public async Task<List<SaleListItemDto>> ListBillsAsync(int? branchId, CancellationToken ct = default)
    {
        var date = await GetInitialBillHistoryDateAsync(branchId, ct);
        return await ListBillsForDateAsync(date, branchId, ct);
    }

    public Task<List<SaleListItemDto>> ListBillsForDateAsync(
        DateOnly date, int? branchId, CancellationToken ct = default)
    {
        var (start, end) = GetDateRange(date);
        var q = _uow.Repository<Sale>().Query().AsNoTracking()
            .Where(s => s.Status == SaleStatus.Completed
                        && s.InvoiceDate >= start
                        && s.InvoiceDate < end);
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

    public async Task<DateOnly> GetInitialBillHistoryDateAsync(int? branchId, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(_clock.Today);
        if (await HasBillsOnDateAsync(today, branchId, ct))
            return today;

        var latest = await GetLatestBillDateAsync(branchId, ct);
        return latest ?? today;
    }

    public async Task<DateOnly?> GetPreviousBillDateAsync(
        DateOnly beforeDate, int? branchId, CancellationToken ct = default)
    {
        var before = GetDateRange(beforeDate).Start;
        var q = _uow.Repository<Sale>().Query().AsNoTracking()
            .Where(s => s.Status == SaleStatus.Completed && s.InvoiceDate < before);
        if (branchId.HasValue) q = q.Where(s => s.BranchId == branchId);

        var previous = await q.MaxAsync(s => (DateTime?)s.InvoiceDate, ct);
        return previous is null ? null : DateOnly.FromDateTime(previous.Value);
    }

    private async Task<bool> HasBillsOnDateAsync(DateOnly date, int? branchId, CancellationToken ct)
    {
        var (start, end) = GetDateRange(date);
        var q = _uow.Repository<Sale>().Query().AsNoTracking()
            .Where(s => s.Status == SaleStatus.Completed
                        && s.InvoiceDate >= start
                        && s.InvoiceDate < end);
        if (branchId.HasValue) q = q.Where(s => s.BranchId == branchId);
        return await q.AnyAsync(ct);
    }

    private async Task<DateOnly?> GetLatestBillDateAsync(int? branchId, CancellationToken ct)
    {
        var q = _uow.Repository<Sale>().Query().AsNoTracking()
            .Where(s => s.Status == SaleStatus.Completed);
        if (branchId.HasValue) q = q.Where(s => s.BranchId == branchId);

        var latest = await q.MaxAsync(s => (DateTime?)s.InvoiceDate, ct);
        return latest is null ? null : DateOnly.FromDateTime(latest.Value);
    }

    private (DateTime Start, DateTime End) GetDateRange(DateOnly date)
    {
        var start = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, _clock.Today.Kind);
        return (start, start.AddDays(1));
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
        var normalized = SearchQueryExtensions.NormalizeTerm(term);
        if (normalized.Length < 2) return Task.FromResult(new List<BillSearchResultDto>());

        var q = _uow.Repository<Sale>().Query().AsNoTracking()
            .Where(s => s.Status == SaleStatus.Completed);
        if (branchId.HasValue) q = q.Where(s => s.BranchId == branchId);

        q = q.Where(s => s.Items.Any(i =>
            i.Medicine != null &&
            (EF.Functions.Like(i.Medicine.NameSearchKey, normalized + "%") ||
             EF.Functions.Like(i.Medicine.NameSearchKey, "%" + normalized + "%"))));

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
                                (EF.Functions.Like(i.Medicine.NameSearchKey, normalized + "%") ||
                                 EF.Functions.Like(i.Medicine.NameSearchKey, "%" + normalized + "%")))
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

        var preferMedWinNames = sale.InvoiceNumber.StartsWith("MW-S-", StringComparison.OrdinalIgnoreCase);
        var medNames = await ResolveMedicineDisplayNamesAsync(
            sale.Items.Select(i => i.MedicineId), preferMedWinNames, ct);

        var batchIds = sale.Items
            .Where(i => i.MedicineBatchId.HasValue)
            .Select(i => i.MedicineBatchId!.Value)
            .Distinct()
            .ToList();
        var batches = await _uow.Repository<MedicineBatch>().Query()
            .Where(b => batchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, ct);
        var expiryByMedicineBatch = await LoadBatchExpiryLookupAsync(sale.Items, ct);

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

                var unitPrice = item.UnitPrice;
                var mrp = item.Mrp;
                if (mrp <= 0 && item.MedicineBatchId is int batchId && batches.TryGetValue(batchId, out var batchRow) && batchRow.Mrp > 0)
                    mrp = batchRow.Mrp;

                var discountPercent = SaleLinePricing.DiscountPercent(mrp > 0 ? mrp : unitPrice, unitPrice);

                return new SaleEditLineDto
                {
                    MedicineId = item.MedicineId,
                    MedicineBatchId = item.MedicineBatchId ?? 0,
                    MedicineName = medNames.TryGetValue(item.MedicineId, out var name) ? name : "Medicine",
                    BatchNumber = item.BatchNumber ?? string.Empty,
                    ExpiryDate = ResolveLineExpiry(item, batches, expiryByMedicineBatch),
                    Quantity = item.Quantity,
                    UnitPrice = unitPrice,
                    Mrp = mrp > 0 ? mrp : unitPrice,
                    GstPercent = item.GstPercent,
                    DiscountPercent = discountPercent,
                    AvailableStock = batchQty + item.Quantity
                };
            }).ToList()
        };

        return Result.Success(dto);
    }

    public async Task<Result<SaleReceiptDto>> GetSaleReceiptAsync(int saleId, int? branchId, CancellationToken ct = default)
    {
        var sale = await _uow.Repository<Sale>().Query()
            .AsNoTracking()
            .Include(s => s.Items)
            .Include(s => s.Payments)
            .FirstOrDefaultAsync(s => s.Id == saleId && s.Status == SaleStatus.Completed, ct);

        if (sale is null)
            return Result.Failure<SaleReceiptDto>("Invoice not found.");
        if (branchId.HasValue && sale.BranchId != branchId)
            return Result.Failure<SaleReceiptDto>("Invoice belongs to another branch.");

        if (sale.Items.Count == 0)
        {
            sale.Items = await _uow.Repository<SaleItem>().Query().AsNoTracking()
                .Where(i => i.SaleId == sale.Id)
                .ToListAsync(ct);
        }

        return await BuildReceiptAsync(sale, ct);
    }

    /// <summary>
    /// Finds an existing batch for a sale line, or provisions an OPENING batch when saving.
    /// </summary>
    private async Task<MedicineBatch> ResolveBatchForLineAsync(
        int medicineId, string? batchNumber, int? branchId, CancellationToken ct)
    {
        var q = _uow.Repository<MedicineBatch>().Query()
            .Where(b => b.MedicineId == medicineId);
        if (branchId.HasValue) q = q.Where(b => b.BranchId == branchId);

        if (!string.IsNullOrWhiteSpace(batchNumber))
        {
            var byNumber = await q.FirstOrDefaultAsync(b => b.BatchNumber == batchNumber, ct);
            if (byNumber is not null) return byNumber;
        }

        var opening = await q.FirstOrDefaultAsync(b => b.BatchNumber == "OPENING", ct);
        if (opening is not null) return opening;

        var created = await EnsureOpeningBatchAsync(medicineId, branchId, ct);
        if (created.Count == 0)
            throw new BillingException("A selected batch no longer exists.");

        var batchId = created[0].BatchId;
        var batch = await _uow.Repository<MedicineBatch>().GetByIdAsync(batchId, ct);
        if (batch is null)
            throw new BillingException("A selected batch no longer exists.");
        return batch;
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

            var batch = line.MedicineBatchId > 0
                ? await _uow.Repository<MedicineBatch>().GetByIdAsync(line.MedicineBatchId, ct)
                : null;
            batch ??= await ResolveBatchForLineAsync(line.MedicineId, line.BatchNumber, branchId, ct);
            if (batch.QuantityAvailable < line.Quantity)
                throw new BillingException($"Insufficient stock for batch {batch.BatchNumber} (available {batch.QuantityAvailable}).");

            var medicine = await _uow.Repository<Medicine>().GetByIdAsync(line.MedicineId, ct);

            var mrp = line.Mrp > 0
                ? line.Mrp
                : batch.Mrp > 0
                    ? batch.Mrp
                    : medicine?.Mrp > 0
                        ? medicine.Mrp
                        : line.UnitPrice;

            var gstPercent = line.UnitPrice > 0 ? batch.GstPercent : 0m;
            var grossAtMrp = SaleLinePricing.GrossAtMrp(mrp, line.Quantity);
            var discountAmount = SaleLinePricing.DiscountAmount(mrp, line.UnitPrice, line.Quantity);
            var discountPercent = SaleLinePricing.DiscountPercent(mrp, line.UnitPrice);
            var netInclusive = SaleLinePricing.LineTotal(line.UnitPrice, line.Quantity);
            var taxable = SaleLinePricing.TaxableAmount(netInclusive, gstPercent);
            var taxAmount = SaleLinePricing.TaxAmount(netInclusive, gstPercent, taxable);

            sale.Items.Add(new SaleItem
            {
                MedicineId = line.MedicineId,
                MedicineBatchId = batch.Id,
                BatchNumber = batch.BatchNumber,
                ExpiryDate = batch.ExpiryDate,
                Quantity = line.Quantity,
                Mrp = mrp,
                UnitPrice = line.UnitPrice,
                DiscountPercent = discountPercent,
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

            subTotal += grossAtMrp;
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

        var preferMedWinNames = sale.InvoiceNumber.StartsWith("MW-S-", StringComparison.OrdinalIgnoreCase);
        var medNames = await ResolveMedicineDisplayNamesAsync(
            sale.Items.Select(i => i.MedicineId), preferMedWinNames, ct);

        var batchIds = sale.Items
            .Where(i => i.MedicineBatchId.HasValue)
            .Select(i => i.MedicineBatchId!.Value)
            .Distinct()
            .ToList();
        var batchesById = batchIds.Count > 0
            ? await _uow.Repository<MedicineBatch>().Query().AsNoTracking()
                .Where(b => batchIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, ct)
            : new Dictionary<int, MedicineBatch>();
        var expiryByMedicineBatch = await LoadBatchExpiryLookupAsync(sale.Items, ct);

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
            RoundOff = sale.RoundOff,
            GrandTotal = sale.GrandTotal,
            PaidAmount = sale.PaidAmount,
            ChangeReturned = sale.ChangeReturned,
            RewardPointsEarned = sale.RewardPointsEarned
        };

        decimal totalTaxable = 0m, totalTax = 0m;
        int sr = 1;
        foreach (var i in sale.Items)
        {
            var unitPrice = i.UnitPrice;
            var mrp = i.Mrp > 0 ? i.Mrp : unitPrice;
            var discountAmount = i.DiscountAmount > 0
                ? i.DiscountAmount
                : SaleLinePricing.DiscountAmount(mrp, unitPrice, i.Quantity);
            var discountPercent = i.DiscountPercent > 0
                ? i.DiscountPercent
                : SaleLinePricing.DiscountPercent(mrp, unitPrice);
            var (lineTaxable, lineTax) = SaleLinePricing.ResolveLineTax(
                i.LineTotal, i.GstPercent, i.TaxableAmount, i.TaxAmount);
            totalTaxable += lineTaxable;
            totalTax += lineTax;

            receipt.Lines.Add(new SaleReceiptLineDto(
                sr++,
                medNames.TryGetValue(i.MedicineId, out var n) ? n : $"#{i.MedicineId}",
                i.BatchNumber ?? string.Empty,
                ResolveLineExpiry(i, batchesById, expiryByMedicineBatch),
                i.Quantity,
                mrp,
                unitPrice,
                discountPercent,
                discountAmount,
                i.GstPercent,
                i.LineTotal));
        }

        receipt.SubTotal = receipt.Lines.Sum(l => SaleLinePricing.GrossAtMrp(l.Mrp, l.Quantity));
        receipt.DiscountAmount = receipt.Lines.Sum(l => l.DiscountAmount);
        receipt.TaxableAmount = totalTaxable;
        (receipt.CgstAmount, receipt.SgstAmount) = SaleLinePricing.SplitCgstSgst(totalTax);
        receipt.GrandTotal = sale.GrandTotal > 0
            ? sale.GrandTotal
            : receipt.Lines.Sum(l => l.Amount) + sale.RoundOff;

        return Result.Success(receipt);
    }

    /// <summary>
    /// Resolves medicine names for historical sale lines, including soft-deleted MedWin imports.
    /// For MedWin-imported invoices, prefers mapped MedWin catalogue names when available.
    /// </summary>
    private async Task<Dictionary<int, string>> ResolveMedicineDisplayNamesAsync(
        IEnumerable<int> medicineIds,
        bool preferMedWinHistoricalNames,
        CancellationToken ct)
    {
        var ids = medicineIds.Distinct().ToList();
        if (ids.Count == 0) return new();

        var medicines = await _uow.Repository<Medicine>().QueryIncludingDeleted().AsNoTracking()
            .Where(m => ids.Contains(m.Id))
            .Select(m => new { m.Id, m.Name })
            .ToListAsync(ct);

        var result = medicines.ToDictionary(m => m.Id, m => m.Name);

        if (preferMedWinHistoricalNames)
        {
            var mappings = await _uow.Repository<MedicineMedWinMapping>().Query().AsNoTracking()
                .Where(m => ids.Contains(m.OneMgMedicineId)
                            || (m.MedWinMedicineId != null && ids.Contains(m.MedWinMedicineId.Value)))
                .Select(m => new { m.OneMgMedicineId, m.MedWinMedicineId, m.MedWinMedicineName })
                .ToListAsync(ct);

            foreach (var map in mappings)
            {
                if (map.MedWinMedicineId is int medWinMedicineId
                    && ids.Contains(medWinMedicineId)
                    && IsUsableMedWinDisplayName(map.MedWinMedicineName))
                {
                    result[medWinMedicineId] = map.MedWinMedicineName;
                }
            }

            foreach (var group in mappings.GroupBy(m => m.OneMgMedicineId))
            {
                if (!ids.Contains(group.Key)) continue;

                var medWinNames = group
                    .Select(m => m.MedWinMedicineName)
                    .Where(IsUsableMedWinDisplayName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (medWinNames.Count == 1)
                    result[group.Key] = medWinNames[0];
            }
        }

        foreach (var id in ids)
        {
            if (!result.ContainsKey(id))
                result[id] = $"Medicine #{id}";
        }

        return result;
    }

    private static bool IsUsableMedWinDisplayName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && !name.StartsWith("MedWinId:", StringComparison.OrdinalIgnoreCase);

    public async Task<SaleMedicineDetailDto?> GetMedicineLineDetailAsync(
        int medicineId, int? batchId, int? branchId, CancellationToken ct = default)
    {
        var medicine = await _uow.Repository<Medicine>().QueryIncludingDeleted().AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == medicineId, ct);
        if (medicine is null) return null;

        decimal qtyAvailable;
        var costPrice = medicine.PurchasePrice;
        var mrp = medicine.Mrp;
        var location = medicine.RackNumber;

        if (batchId is > 0)
        {
            var batchQuery = _uow.Repository<MedicineBatch>().Query().AsNoTracking()
                .Where(b => b.Id == batchId && b.MedicineId == medicineId);
            if (branchId.HasValue) batchQuery = batchQuery.Where(b => b.BranchId == branchId);

            var batch = await batchQuery.FirstOrDefaultAsync(ct);
            if (batch is not null)
            {
                qtyAvailable = batch.QuantityAvailable;
                if (batch.PurchasePrice > 0) costPrice = batch.PurchasePrice;
                if (batch.Mrp > 0) mrp = batch.Mrp;
                location = batch.RackNumber ?? location;
            }
            else
            {
                var stockMap = await GetStockByMedicineIdsAsync(new List<int> { medicineId }, branchId, ct);
                qtyAvailable = stockMap.GetValueOrDefault(medicineId);
            }
        }
        else
        {
            var stockMap = await GetStockByMedicineIdsAsync(new List<int> { medicineId }, branchId, ct);
            qtyAvailable = stockMap.GetValueOrDefault(medicineId);
        }

        var packingSize = medicine.UnitsPerPack > 0
            ? $"{medicine.UnitsPerPack} {medicine.UnitOfMeasure ?? "Nos"}".Trim()
            : "-";

        var packingType = !string.IsNullOrWhiteSpace(medicine.PackInfo)
            ? medicine.PackInfo
            : MedicineNotesHelper.ExtractPackInfo(medicine.Notes) ?? "-";

        return new SaleMedicineDetailDto(
            medicine.Name,
            string.IsNullOrWhiteSpace(medicine.GenericName) ? medicine.Composition : medicine.GenericName,
            qtyAvailable,
            costPrice,
            mrp,
            location,
            packingSize,
            packingType);
    }

    private async Task<Dictionary<(int MedicineId, string BatchNumber), DateTime?>> LoadBatchExpiryLookupAsync(
        IEnumerable<SaleItem> items, CancellationToken ct)
    {
        var medicineIds = items.Select(i => i.MedicineId).Distinct().ToList();
        if (medicineIds.Count == 0) return new();

        var batches = await _uow.Repository<MedicineBatch>().Query().AsNoTracking()
            .Where(b => medicineIds.Contains(b.MedicineId) && b.ExpiryDate != null)
            .Select(b => new { b.MedicineId, b.BatchNumber, b.ExpiryDate })
            .ToListAsync(ct);

        var dict = new Dictionary<(int, string), DateTime?>();
        foreach (var batch in batches)
        {
            if (string.IsNullOrWhiteSpace(batch.BatchNumber)) continue;
            var key = (batch.MedicineId, batch.BatchNumber.Trim().ToUpperInvariant());
            dict.TryAdd(key, batch.ExpiryDate);
        }

        return dict;
    }

    private static DateTime? ResolveLineExpiry(
        SaleItem item,
        Dictionary<int, MedicineBatch> batchesById,
        Dictionary<(int MedicineId, string BatchNumber), DateTime?> expiryByMedicineBatch)
    {
        if (item.ExpiryDate.HasValue) return item.ExpiryDate;
        if (item.MedicineBatchId is int batchId
            && batchesById.TryGetValue(batchId, out var linkedBatch)
            && linkedBatch.ExpiryDate.HasValue)
            return linkedBatch.ExpiryDate;
        if (!string.IsNullOrWhiteSpace(item.BatchNumber))
        {
            var key = (item.MedicineId, item.BatchNumber.Trim().ToUpperInvariant());
            if (expiryByMedicineBatch.TryGetValue(key, out var expiry))
                return expiry;
        }

        return null;
    }
}
