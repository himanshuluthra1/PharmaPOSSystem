using Microsoft.EntityFrameworkCore;
using PharmaPOS.Application.Common;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Domain.Entities.Inventory;
using PharmaPOS.Domain.Entities.Masters;
using PharmaPOS.Domain.Entities.Purchases;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Application.Features.Settings;
using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.Purchases;

/// <summary>
/// Default goods-receipt service. Purchase prices are treated as tax-exclusive
/// (the norm on Indian purchase invoices): GST is added on top of the taxable
/// value rather than extracted from it.
/// </summary>
public class PurchaseService : IPurchaseService
{
    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _clock;
    private readonly ISettingsService _settings;

    public PurchaseService(IUnitOfWork uow, IDateTimeProvider clock, ISettingsService settings)
    {
        _uow = uow;
        _clock = clock;
        _settings = settings;
    }

    public async Task<List<PurchaseMedicineDto>> SearchMedicinesAsync(string term, CancellationToken ct = default)
    {
        var normalized = SearchQueryExtensions.NormalizeTerm(term);
        if (normalized.Length < 2) return new();

        var baseQuery = _uow.Repository<Medicine>().Query().AsNoTracking()
            .Where(m => m.Status == EntityStatus.Active);

        var results = await baseQuery
            .WhereMedicineMatches(normalized, prefixOnly: true)
            .OrderBy(m => m.Name)
            .Take(25)
            .Select(m => new PurchaseMedicineDto(
                m.Id, m.Name, m.GenericName, m.Barcode,
                m.GstPercent, m.PurchasePrice, m.Mrp, m.SellingPrice))
            .ToListAsync(ct);

        if (results.Count == 0)
        {
            results = await baseQuery
                .WhereMedicineMatches(normalized, prefixOnly: false)
                .OrderBy(m => m.Name)
                .Take(25)
                .Select(m => new PurchaseMedicineDto(
                    m.Id, m.Name, m.GenericName, m.Barcode,
                    m.GstPercent, m.PurchasePrice, m.Mrp, m.SellingPrice))
                .ToListAsync(ct);
        }

        return results;
    }

    public async Task<List<SupplierLookupDto>> SearchSuppliersAsync(string term, CancellationToken ct = default)
    {
        var normalized = SearchQueryExtensions.NormalizeTerm(term);
        var q = _uow.Repository<Supplier>().Query().Where(s => s.Status == EntityStatus.Active);
        if (normalized.Length >= 1)
            q = q.WhereSupplierMatches(normalized);

        return await q.OrderBy(s => s.Name).Take(25)
            .Select(s => new SupplierLookupDto(s.Id, s.Name, s.Phone, s.GstNumber, s.OutstandingBalance))
            .ToListAsync(ct);
    }

    public async Task<PurchaseMedicineDto?> GetMedicineAsync(int medicineId, CancellationToken ct = default)
    {
        return await _uow.Repository<Medicine>().Query().AsNoTracking()
            .Where(m => m.Id == medicineId && m.Status == EntityStatus.Active)
            .Select(m => new PurchaseMedicineDto(
                m.Id, m.Name, m.GenericName, m.Barcode,
                m.GstPercent, m.PurchasePrice, m.Mrp, m.SellingPrice))
            .FirstOrDefaultAsync(ct);
    }

    public Task<List<PurchaseListItemDto>> ListPurchasesAsync(int? branchId, CancellationToken ct = default)
    {
        var q = _uow.Repository<Purchase>().Query().AsNoTracking()
            .Where(p => p.Status == PurchaseStatus.Received);
        if (branchId.HasValue) q = q.Where(p => p.BranchId == branchId);

        return q.OrderByDescending(p => p.InvoiceDate)
            .ThenByDescending(p => p.Id)
            .Select(p => new PurchaseListItemDto(
                p.Id,
                p.InvoiceNumber,
                p.InvoiceDate,
                p.Supplier != null ? p.Supplier.Name : $"Supplier #{p.SupplierId}",
                p.SupplierInvoiceNumber))
            .ToListAsync(ct);
    }

    public Task<string> PreviewNextPurchaseNumberAsync(int? branchId, CancellationToken ct = default)
        => GenerateInvoiceNumberAsync(branchId, ct);

    public async Task<Result<PurchaseLoadDto>> GetPurchaseForLoadAsync(int purchaseId, int? branchId, CancellationToken ct = default)
    {
        var purchase = await _uow.Repository<Purchase>().Query().AsNoTracking()
            .Include(p => p.Items)
            .Include(p => p.Supplier)
            .FirstOrDefaultAsync(p => p.Id == purchaseId && p.Status == PurchaseStatus.Received, ct);

        if (purchase is null)
            return Result.Failure<PurchaseLoadDto>("Purchase invoice not found.");
        if (branchId.HasValue && purchase.BranchId != branchId)
            return Result.Failure<PurchaseLoadDto>("Purchase belongs to another branch.");

        var medIds = purchase.Items.Select(i => i.MedicineId).Distinct().ToList();
        var medNames = await _uow.Repository<Medicine>().Query().AsNoTracking()
            .Where(m => medIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Name, m.GenericName })
            .ToDictionaryAsync(m => m.Id, m => m, ct);

        return Result.Success(new PurchaseLoadDto
        {
            PurchaseId = purchase.Id,
            InvoiceNumber = purchase.InvoiceNumber,
            SupplierInvoiceNumber = purchase.SupplierInvoiceNumber,
            InvoiceDate = purchase.InvoiceDate,
            SupplierId = purchase.SupplierId,
            SupplierName = purchase.Supplier?.Name ?? $"Supplier #{purchase.SupplierId}",
            SupplierPhone = purchase.Supplier?.Phone,
            PaidAmount = purchase.PaidAmount,
            PaymentMethod = PaymentMethod.Cash,
            Lines = purchase.Items.Select(i =>
            {
                medNames.TryGetValue(i.MedicineId, out var med);
                return new PurchaseLoadLineDto
                {
                    MedicineId = i.MedicineId,
                    MedicineName = med?.Name ?? $"Medicine #{i.MedicineId}",
                    GenericName = med?.GenericName,
                    BatchNumber = i.BatchNumber,
                    ManufacturingDate = i.ManufacturingDate,
                    ExpiryDate = i.ExpiryDate,
                    Quantity = i.Quantity,
                    FreeQuantity = i.FreeQuantity,
                    PurchasePrice = i.PurchasePrice,
                    Mrp = i.Mrp,
                    SellingPrice = i.SellingPrice,
                    DiscountPercent = i.DiscountPercent,
                    GstPercent = i.GstPercent
                };
            }).ToList()
        });
    }

    public Task<List<PurchaseSupplierBillDto>> ListPurchasesBySupplierAsync(int? supplierId, int? branchId, CancellationToken ct = default)
    {
        var q = _uow.Repository<Purchase>().Query().AsNoTracking()
            .Where(p => p.Status == PurchaseStatus.Received);
        if (branchId.HasValue) q = q.Where(p => p.BranchId == branchId);
        if (supplierId.HasValue) q = q.Where(p => p.SupplierId == supplierId.Value);

        return q.OrderByDescending(p => p.InvoiceDate)
            .ThenByDescending(p => p.Id)
            .Select(p => new PurchaseSupplierBillDto(
                p.Id,
                p.InvoiceNumber,
                p.InvoiceDate,
                p.Supplier != null ? p.Supplier.Name : $"Supplier #{p.SupplierId}",
                p.GrandTotal,
                p.Items.Count,
                p.GrandTotal > p.PaidAmount ? p.GrandTotal - p.PaidAmount : 0m))
            .ToListAsync(ct);
    }

    public async Task<Result<PurchaseReceiptDto>> CreatePurchaseAsync(CreatePurchaseRequest request, int? branchId, CancellationToken ct = default)
    {
        if (request.SupplierId <= 0)
            return Result.Failure<PurchaseReceiptDto>("Select a supplier for the purchase.");
        if (request.Lines.Count == 0)
            return Result.Failure<PurchaseReceiptDto>("Add at least one item to the purchase.");

        try
        {
            var purchase = await _uow.ExecuteInTransactionAsync(
                token => BuildAndPersistPurchaseAsync(request, branchId, token), ct);

            return await BuildReceiptAsync(purchase, ct);
        }
        catch (PurchaseException pex)
        {
            return Result.Failure<PurchaseReceiptDto>(pex.Message);
        }
        catch (Exception ex)
        {
            return Result.Failure<PurchaseReceiptDto>($"Could not save the purchase: {ex.Message}");
        }
    }

    public async Task<Result<PurchaseReceiptDto>> UpdatePurchaseAsync(UpdatePurchaseRequest request, int? branchId, CancellationToken ct = default)
    {
        if (request.SupplierId <= 0)
            return Result.Failure<PurchaseReceiptDto>("Select a supplier for the purchase.");
        if (request.Lines.Count == 0)
            return Result.Failure<PurchaseReceiptDto>("Add at least one item to the purchase.");

        try
        {
            var purchase = await _uow.ExecuteInTransactionAsync(
                token => UpdateAndPersistPurchaseAsync(request, branchId, token), ct);

            return await BuildReceiptAsync(purchase, ct);
        }
        catch (PurchaseException pex)
        {
            return Result.Failure<PurchaseReceiptDto>(pex.Message);
        }
        catch (Exception ex)
        {
            return Result.Failure<PurchaseReceiptDto>($"Could not update the purchase: {ex.Message}");
        }
    }

    private async Task<Purchase> UpdateAndPersistPurchaseAsync(UpdatePurchaseRequest request, int? branchId, CancellationToken ct)
    {
        var purchase = await _uow.Repository<Purchase>().Query()
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == request.PurchaseId && p.Status == PurchaseStatus.Received, ct)
            ?? throw new PurchaseException("Purchase invoice not found or cannot be edited.");

        if (branchId.HasValue && purchase.BranchId != branchId)
            throw new PurchaseException("Purchase belongs to another branch.");

        var oldSupplier = await _uow.Repository<Supplier>().GetByIdAsync(purchase.SupplierId, ct)
            ?? throw new PurchaseException("The original supplier no longer exists.");

        AdjustSupplierBalance(oldSupplier, purchase.GrandTotal, purchase.PaidAmount, reverse: true);

        await RestorePurchaseStockAsync(purchase, branchId, ct);

        foreach (var oldItem in purchase.Items.ToList())
            _uow.Repository<PurchaseItem>().Remove(oldItem);
        purchase.Items.Clear();

        var newSupplier = purchase.SupplierId == request.SupplierId
            ? oldSupplier
            : await _uow.Repository<Supplier>().GetByIdAsync(request.SupplierId, ct)
              ?? throw new PurchaseException("The selected supplier no longer exists.");

        purchase.SupplierId = request.SupplierId;
        purchase.SupplierInvoiceNumber = request.SupplierInvoiceNumber;
        purchase.InvoiceDate = request.InvoiceDate == default ? purchase.InvoiceDate : request.InvoiceDate;
        purchase.Remarks = request.Remarks;

        var totals = await ApplyPurchaseLinesAsync(purchase, request.Lines, branchId, ct);
        ApplyPurchaseTotals(purchase, totals, request.PaidAmount);

        AdjustSupplierBalance(newSupplier, purchase.GrandTotal, purchase.PaidAmount, reverse: false);

        _uow.Repository<Purchase>().Update(purchase);
        if (newSupplier.Id != oldSupplier.Id)
            _uow.Repository<Supplier>().Update(oldSupplier);
        _uow.Repository<Supplier>().Update(newSupplier);
        await _uow.SaveChangesAsync(ct);
        return purchase;
    }

    private async Task RestorePurchaseStockAsync(Purchase purchase, int? branchId, CancellationToken ct)
    {
        foreach (var item in purchase.Items)
        {
            var receivedQty = item.Quantity + item.FreeQuantity;
            if (receivedQty <= 0) continue;

            if (item.MedicineBatchId is not int batchId)
                continue;

            var batch = await _uow.Repository<MedicineBatch>().GetByIdAsync(batchId, ct);
            if (batch is null) continue;

            if (batch.QuantityAvailable < receivedQty)
            {
                var medicine = await _uow.Repository<Medicine>().GetByIdAsync(item.MedicineId, ct);
                throw new PurchaseException(
                    $"Cannot edit: insufficient stock to reverse {medicine?.Name ?? "item"} batch {item.BatchNumber}.");
            }

            batch.QuantityAvailable -= receivedQty;
            _uow.Repository<MedicineBatch>().Update(batch);

            await _uow.Repository<StockMovement>().AddAsync(new StockMovement
            {
                BranchId = branchId,
                MedicineId = item.MedicineId,
                MedicineBatchId = batchId,
                MovementType = StockMovementType.PurchaseReturn,
                Quantity = receivedQty,
                BalanceAfter = batch.QuantityAvailable,
                UnitCost = item.PurchasePrice,
                ReferenceType = nameof(Purchase),
                ReferenceId = purchase.Id,
                ReferenceNumber = purchase.InvoiceNumber,
                MovementDateUtc = _clock.UtcNow,
                Remarks = $"Reversal for edit {purchase.InvoiceNumber}"
            }, ct);
        }
    }

    private static void AdjustSupplierBalance(Supplier supplier, decimal grandTotal, decimal paidAmount, bool reverse)
    {
        var paid = Math.Min(paidAmount, grandTotal);
        var due = grandTotal - paid;
        if (due == 0) return;

        supplier.OutstandingBalance += reverse ? -due : due;
    }

    private async Task<Purchase> BuildAndPersistPurchaseAsync(CreatePurchaseRequest request, int? branchId, CancellationToken ct)
    {
        var supplier = await _uow.Repository<Supplier>().GetByIdAsync(request.SupplierId, ct)
            ?? throw new PurchaseException("The selected supplier no longer exists.");

        var purchase = new Purchase
        {
            InvoiceDate = request.InvoiceDate == default ? _clock.Now : request.InvoiceDate,
            BranchId = branchId,
            SupplierId = request.SupplierId,
            SupplierInvoiceNumber = request.SupplierInvoiceNumber,
            Remarks = request.Remarks,
            Status = PurchaseStatus.Received,
        };

        var totals = await ApplyPurchaseLinesAsync(purchase, request.Lines, branchId, ct);
        ApplyPurchaseTotals(purchase, totals, request.PaidAmount);

        purchase.InvoiceNumber = await GenerateInvoiceNumberAsync(branchId, ct);
        await _uow.Repository<Purchase>().AddAsync(purchase, ct);

        AdjustSupplierBalance(supplier, purchase.GrandTotal, purchase.PaidAmount, reverse: false);
        _uow.Repository<Supplier>().Update(supplier);

        await _uow.SaveChangesAsync(ct);
        return purchase;
    }

    private sealed record PurchaseTotals(decimal SubTotal, decimal Discount, decimal Taxable, decimal Tax);

    private async Task<PurchaseTotals> ApplyPurchaseLinesAsync(
        Purchase purchase, IReadOnlyList<PurchaseLineRequest> lines, int? branchId, CancellationToken ct)
    {
        decimal subTotal = 0m, totalDiscount = 0m, totalTaxable = 0m, totalTax = 0m;

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
                throw new PurchaseException("Quantity must be greater than zero.");
            if (string.IsNullOrWhiteSpace(line.BatchNumber))
                throw new PurchaseException("Every line needs a batch number.");

            var medicine = await _uow.Repository<Medicine>().GetByIdAsync(line.MedicineId, ct)
                ?? throw new PurchaseException("A selected medicine no longer exists.");

            var gross = line.PurchasePrice * line.Quantity;
            var discountAmount = Math.Round(gross * line.DiscountPercent / 100m, 2);
            var taxable = gross - discountAmount;
            var taxAmount = Math.Round(taxable * line.GstPercent / 100m, 2);
            var lineTotal = taxable + taxAmount;
            var receivedQty = line.Quantity + line.FreeQuantity;

            var batch = await _uow.Repository<MedicineBatch>().Query()
                .FirstOrDefaultAsync(b => b.MedicineId == line.MedicineId &&
                                          b.BranchId == branchId &&
                                          b.BatchNumber == line.BatchNumber, ct);

            if (batch is null)
            {
                batch = new MedicineBatch
                {
                    MedicineId = line.MedicineId,
                    BranchId = branchId,
                    BatchNumber = line.BatchNumber,
                    ManufacturingDate = line.ManufacturingDate,
                    ExpiryDate = line.ExpiryDate,
                    QuantityAvailable = receivedQty,
                    PurchasePrice = line.PurchasePrice,
                    Mrp = line.Mrp,
                    SellingPrice = line.SellingPrice > 0 ? line.SellingPrice : line.Mrp,
                    GstPercent = line.GstPercent
                };
                await _uow.Repository<MedicineBatch>().AddAsync(batch, ct);
                await _uow.SaveChangesAsync(ct);
            }
            else
            {
                batch.QuantityAvailable += receivedQty;
                batch.PurchasePrice = line.PurchasePrice;
                batch.Mrp = line.Mrp;
                if (line.SellingPrice > 0) batch.SellingPrice = line.SellingPrice;
                batch.GstPercent = line.GstPercent;
                if (line.ExpiryDate.HasValue) batch.ExpiryDate = line.ExpiryDate;
                if (line.ManufacturingDate.HasValue) batch.ManufacturingDate = line.ManufacturingDate;
                _uow.Repository<MedicineBatch>().Update(batch);
            }

            purchase.Items.Add(new PurchaseItem
            {
                MedicineId = line.MedicineId,
                MedicineBatchId = batch.Id,
                BatchNumber = line.BatchNumber,
                ManufacturingDate = line.ManufacturingDate,
                ExpiryDate = line.ExpiryDate,
                Quantity = line.Quantity,
                FreeQuantity = line.FreeQuantity,
                PurchasePrice = line.PurchasePrice,
                Mrp = line.Mrp,
                SellingPrice = line.SellingPrice,
                DiscountPercent = line.DiscountPercent,
                DiscountAmount = discountAmount,
                GstPercent = line.GstPercent,
                TaxableAmount = taxable,
                TaxAmount = taxAmount,
                LineTotal = lineTotal
            });

            await _uow.Repository<StockMovement>().AddAsync(new StockMovement
            {
                BranchId = branchId,
                MedicineId = line.MedicineId,
                MedicineBatchId = batch.Id,
                MovementType = StockMovementType.PurchaseIn,
                Quantity = receivedQty,
                BalanceAfter = batch.QuantityAvailable,
                UnitCost = line.PurchasePrice,
                ReferenceType = nameof(Purchase),
                ReferenceId = purchase.Id > 0 ? purchase.Id : null,
                ReferenceNumber = purchase.InvoiceNumber,
                MovementDateUtc = _clock.UtcNow,
                Remarks = medicine.Name
            }, ct);

            medicine.PurchasePrice = line.PurchasePrice;
            if (line.Mrp > 0) medicine.Mrp = line.Mrp;
            if (line.SellingPrice > 0) medicine.SellingPrice = line.SellingPrice;
            _uow.Repository<Medicine>().Update(medicine);

            subTotal += gross;
            totalDiscount += discountAmount;
            totalTaxable += taxable;
            totalTax += taxAmount;
        }

        return new PurchaseTotals(subTotal, totalDiscount, totalTaxable, totalTax);
    }

    private static void ApplyPurchaseTotals(Purchase purchase, PurchaseTotals totals, decimal paidAmount)
    {
        var netTotal = totals.Taxable + totals.Tax;
        var rounded = Math.Round(netTotal, 0, MidpointRounding.AwayFromZero);

        purchase.SubTotal = totals.SubTotal;
        purchase.DiscountAmount = totals.Discount;
        purchase.TaxableAmount = totals.Taxable;
        purchase.CgstAmount = Math.Round(totals.Tax / 2m, 2);
        purchase.SgstAmount = totals.Tax - purchase.CgstAmount;
        purchase.IgstAmount = 0m;
        purchase.RoundOff = rounded - netTotal;
        purchase.GrandTotal = rounded;
        purchase.PaidAmount = paidAmount;
        purchase.PaymentStatus = paidAmount >= rounded ? PaymentStatus.Paid
            : paidAmount > 0 ? PaymentStatus.PartiallyPaid : PaymentStatus.Unpaid;
    }

    /// <summary>Signals a recoverable, user-facing purchase validation failure.</summary>
    private sealed class PurchaseException : Exception
    {
        public PurchaseException(string message) : base(message) { }
    }

    private async Task<string> GenerateInvoiceNumberAsync(int? branchId, CancellationToken ct)
    {
        var today = _clock.Today;
        var tomorrow = today.AddDays(1);
        var q = _uow.Repository<Purchase>().Query()
            .Where(p => p.CreatedAtUtc >= today && p.CreatedAtUtc < tomorrow);
        if (branchId.HasValue) q = q.Where(p => p.BranchId == branchId);

        var todayCount = await q.CountAsync(ct);
        var prefs = await _settings.GetPreferencesAsync(ct);
        var prefix = string.IsNullOrWhiteSpace(prefs.PurchaseInvoicePrefix) ? "PUR" : prefs.PurchaseInvoicePrefix.Trim();
        return $"{prefix}-{today:yyyyMMdd}-{todayCount + 1:D4}";
    }

    private async Task<Result<PurchaseReceiptDto>> BuildReceiptAsync(Purchase purchase, CancellationToken ct)
    {
        var supplier = await _uow.Repository<Supplier>().GetByIdAsync(purchase.SupplierId, ct);

        var receipt = new PurchaseReceiptDto
        {
            PurchaseId = purchase.Id,
            InvoiceNumber = purchase.InvoiceNumber,
            SupplierInvoiceNumber = purchase.SupplierInvoiceNumber,
            InvoiceDate = purchase.InvoiceDate,
            SupplierName = supplier?.Name ?? $"#{purchase.SupplierId}",
            ItemCount = purchase.Items.Count,
            TotalQuantity = purchase.Items.Sum(i => i.Quantity + i.FreeQuantity),
            SubTotal = purchase.SubTotal,
            DiscountAmount = purchase.DiscountAmount,
            TaxableAmount = purchase.TaxableAmount,
            CgstAmount = purchase.CgstAmount,
            SgstAmount = purchase.SgstAmount,
            RoundOff = purchase.RoundOff,
            GrandTotal = purchase.GrandTotal,
            PaidAmount = purchase.PaidAmount,
            BalanceDue = purchase.GrandTotal > purchase.PaidAmount ? purchase.GrandTotal - purchase.PaidAmount : 0m
        };

        return Result.Success(receipt);
    }
}
