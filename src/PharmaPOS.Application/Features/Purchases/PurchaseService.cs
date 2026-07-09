using Microsoft.EntityFrameworkCore;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Domain.Entities.Inventory;
using PharmaPOS.Domain.Entities.Masters;
using PharmaPOS.Domain.Entities.Purchases;
using PharmaPOS.Domain.Enums;
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

    public PurchaseService(IUnitOfWork uow, IDateTimeProvider clock)
    {
        _uow = uow;
        _clock = clock;
    }

    public async Task<List<PurchaseMedicineDto>> SearchMedicinesAsync(string term, CancellationToken ct = default)
    {
        term = (term ?? string.Empty).Trim();
        if (term.Length < 2) return new();

        var baseQuery = _uow.Repository<Medicine>().Query().AsNoTracking()
            .Where(m => m.Status == EntityStatus.Active);

        var results = await baseQuery
            .Where(m => EF.Functions.Like(m.Name, term + "%") ||
                        (m.Barcode != null && m.Barcode == term) ||
                        (m.GenericName != null && EF.Functions.Like(m.GenericName, term + "%")))
            .OrderBy(m => m.Name)
            .Take(25)
            .Select(m => new PurchaseMedicineDto(
                m.Id, m.Name, m.GenericName, m.Barcode,
                m.GstPercent, m.PurchasePrice, m.Mrp, m.SellingPrice))
            .ToListAsync(ct);

        if (results.Count == 0)
        {
            results = await baseQuery
                .Where(m => EF.Functions.Like(m.Name, "%" + term + "%") ||
                            (m.GenericName != null && EF.Functions.Like(m.GenericName, "%" + term + "%")))
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
        term = (term ?? string.Empty).Trim();
        var q = _uow.Repository<Supplier>().Query().Where(s => s.Status == EntityStatus.Active);
        if (term.Length >= 1)
            q = q.Where(s => s.Name.Contains(term) || (s.Phone != null && s.Phone.Contains(term)));

        return await q.OrderBy(s => s.Name).Take(25)
            .Select(s => new SupplierLookupDto(s.Id, s.Name, s.Phone, s.GstNumber, s.OutstandingBalance))
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

        decimal subTotal = 0m, totalDiscount = 0m, totalTaxable = 0m, totalTax = 0m;

        foreach (var line in request.Lines)
        {
            if (line.Quantity <= 0)
                throw new PurchaseException("Quantity must be greater than zero.");
            if (string.IsNullOrWhiteSpace(line.BatchNumber))
                throw new PurchaseException("Every line needs a batch number.");

            var medicine = await _uow.Repository<Medicine>().GetByIdAsync(line.MedicineId, ct)
                ?? throw new PurchaseException("A selected medicine no longer exists.");

            var gross = line.PurchasePrice * line.Quantity;                 // tax-exclusive
            var discountAmount = Math.Round(gross * line.DiscountPercent / 100m, 2);
            var taxable = gross - discountAmount;
            var taxAmount = Math.Round(taxable * line.GstPercent / 100m, 2);
            var lineTotal = taxable + taxAmount;
            var receivedQty = line.Quantity + line.FreeQuantity;

            // Merge into an existing batch (same medicine + branch + number) or create one.
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
                await _uow.SaveChangesAsync(ct); // materialize Id for the movement/link
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
                MovementDateUtc = _clock.UtcNow,
                Remarks = medicine.Name
            }, ct);

            // Keep the medicine master's latest costing in sync.
            medicine.PurchasePrice = line.PurchasePrice;
            if (line.Mrp > 0) medicine.Mrp = line.Mrp;
            if (line.SellingPrice > 0) medicine.SellingPrice = line.SellingPrice;
            _uow.Repository<Medicine>().Update(medicine);

            subTotal += gross;
            totalDiscount += discountAmount;
            totalTaxable += taxable;
            totalTax += taxAmount;
        }

        var netTotal = totalTaxable + totalTax;
        var rounded = Math.Round(netTotal, 0, MidpointRounding.AwayFromZero);

        purchase.SubTotal = subTotal;
        purchase.DiscountAmount = totalDiscount;
        purchase.TaxableAmount = totalTaxable;
        purchase.CgstAmount = Math.Round(totalTax / 2m, 2);
        purchase.SgstAmount = totalTax - purchase.CgstAmount;
        purchase.IgstAmount = 0m;
        purchase.RoundOff = rounded - netTotal;
        purchase.GrandTotal = rounded;

        var paid = Math.Min(request.PaidAmount, rounded);
        purchase.PaidAmount = request.PaidAmount;
        purchase.PaymentStatus = request.PaidAmount >= rounded ? PaymentStatus.Paid
            : request.PaidAmount > 0 ? PaymentStatus.PartiallyPaid : PaymentStatus.Unpaid;

        purchase.InvoiceNumber = await GenerateInvoiceNumberAsync(branchId, ct);
        await _uow.Repository<Purchase>().AddAsync(purchase, ct);

        // Increase payable by the unpaid portion.
        var due = rounded - paid;
        if (due != 0)
        {
            supplier.OutstandingBalance += due;
            _uow.Repository<Supplier>().Update(supplier);
        }

        await _uow.SaveChangesAsync(ct);
        return purchase;
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
        return $"PUR-{today:yyyyMMdd}-{todayCount + 1:D4}";
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
