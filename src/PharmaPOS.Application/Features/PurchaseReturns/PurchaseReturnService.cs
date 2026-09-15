using Microsoft.EntityFrameworkCore;
using PharmaPOS.Application.Common;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Domain.Entities.Inventory;
using PharmaPOS.Domain.Entities.Masters;
using PharmaPOS.Domain.Entities.Purchases;
using PharmaPOS.Domain.Entities.Sales;
using PharmaPOS.Domain.Entities.System;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Application.Features.ReportingSync;
using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.PurchaseReturns;

public class PurchaseReturnService : IPurchaseReturnService
{
    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _clock;
    private readonly IReportingSyncService _reportingSync;
    private readonly IFinancialYearContext _financialYear;

    public PurchaseReturnService(
        IUnitOfWork uow,
        IDateTimeProvider clock,
        IReportingSyncService reportingSync,
        IFinancialYearContext financialYear)
    {
        _uow = uow;
        _clock = clock;
        _reportingSync = reportingSync;
        _financialYear = financialYear;
    }

    public Task<List<ReturnReasonOptionDto>> ListReturnReasonsAsync(CancellationToken ct = default)
        => _uow.Repository<ReturnReason>().Query().AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Name)
            .Select(r => new ReturnReasonOptionDto(r.Id, r.Code, r.Name, r.RequiresRemarks))
            .ToListAsync(ct);

    public async Task ReconcileSourceBillAmountAsync(int purchaseId, CancellationToken ct = default)
    {
        var purchase = await _uow.Repository<Purchase>().Query()
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == purchaseId, ct);
        if (purchase is null) return;
        if (purchase.Status is PurchaseStatus.Draft or PurchaseStatus.Cancelled or PurchaseStatus.Ordered)
            return;

        var before = purchase.GrandTotal;
        var paidBefore = purchase.PaidAmount;
        await NetPurchaseBillFromReturnsAsync(purchase, ct);
        if (purchase.GrandTotal == before && purchase.PaidAmount == paidBefore)
            return;

        _uow.Repository<Purchase>().Update(purchase);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<List<PurchaseReturnSearchResultDto>> SearchPurchasesAsync(
        string term, int? branchId, CancellationToken ct = default)
    {
        term = term?.Trim() ?? string.Empty;
        if (term.Length < 1) return [];

        var q = _uow.Repository<Purchase>().Query().AsNoTracking()
            .Where(p => p.Status == PurchaseStatus.Received
                        || p.Status == PurchaseStatus.PartiallyReturned
                        || p.Status == PurchaseStatus.Returned)
            .WhereInFinancialYear(_financialYear.Active, p => p.InvoiceDate);

        if (branchId.HasValue) q = q.Where(p => p.BranchId == branchId);

        var normalized = term.Replace(" ", "");
        q = q.Where(p =>
            p.InvoiceNumber.Contains(term)
            || (p.SupplierInvoiceNumber != null && p.SupplierInvoiceNumber.Contains(term))
            || (p.Supplier != null && p.Supplier.Name.Contains(term)));

        var ids = await q.OrderByDescending(p => p.InvoiceDate).Select(p => p.Id).Take(50).ToListAsync(ct);
        foreach (var id in ids)
            await ReconcileSourceBillAmountAsync(id, ct);

        return await _uow.Repository<Purchase>().Query().AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .OrderByDescending(p => p.InvoiceDate)
            .Select(p => new PurchaseReturnSearchResultDto(
                p.Id,
                p.InvoiceNumber,
                p.SupplierInvoiceNumber,
                p.InvoiceDate,
                p.Supplier != null ? p.Supplier.Name : "—",
                p.GrandTotal,
                p.Status))
            .ToListAsync(ct);
    }

    public async Task<Result<PurchaseForReturnDto>> GetPurchaseForReturnAsync(
        int purchaseId, int? branchId, CancellationToken ct = default)
    {
        await ReconcileSourceBillAmountAsync(purchaseId, ct);

        var purchase = await _uow.Repository<Purchase>().Query().AsNoTracking()
            .Include(p => p.Items)
            .Include(p => p.Supplier)
            .FirstOrDefaultAsync(p => p.Id == purchaseId, ct);

        if (purchase is null)
            return Result.Failure<PurchaseForReturnDto>("Purchase bill not found.");
        if (branchId.HasValue && purchase.BranchId != branchId)
            return Result.Failure<PurchaseForReturnDto>("Purchase belongs to another branch.");
        if (purchase.Status is PurchaseStatus.Cancelled or PurchaseStatus.Draft or PurchaseStatus.Ordered)
            return Result.Failure<PurchaseForReturnDto>("Only received purchases can be returned.");
        if (purchase.Status == PurchaseStatus.Returned)
            return Result.Failure<PurchaseForReturnDto>("This purchase has already been fully returned.");

        var returned = await LoadReturnedQuantitiesAsync(purchaseId, ct);
        var medIds = purchase.Items.Select(i => i.MedicineId).Distinct().ToList();
        var names = await _uow.Repository<Medicine>().QueryIncludingDeleted().AsNoTracking()
            .Where(m => medIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.Name, ct);

        var batchIds = purchase.Items
            .Where(i => i.MedicineBatchId is > 0)
            .Select(i => i.MedicineBatchId!.Value)
            .Distinct()
            .ToList();
        var stockByBatch = batchIds.Count == 0
            ? new Dictionary<int, decimal>()
            : await _uow.Repository<MedicineBatch>().Query().AsNoTracking()
                .Where(b => batchIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.QuantityAvailable, ct);

        var dto = new PurchaseForReturnDto
        {
            PurchaseId = purchase.Id,
            InvoiceNumber = purchase.InvoiceNumber,
            SupplierInvoiceNumber = purchase.SupplierInvoiceNumber,
            InvoiceDate = purchase.InvoiceDate,
            SupplierId = purchase.SupplierId,
            SupplierName = purchase.Supplier?.Name ?? "—",
            GrandTotal = purchase.GrandTotal,
            PaidAmount = purchase.PaidAmount,
            Status = purchase.Status,
            Lines = purchase.Items.Select(i =>
            {
                returned.TryGetValue(i.Id, out var r);
                var stock = i.MedicineBatchId is int bid && stockByBatch.TryGetValue(bid, out var s) ? s : 0m;
                return new PurchaseReturnLineDto
                {
                    PurchaseItemId = i.Id,
                    MedicineId = i.MedicineId,
                    MedicineName = names.TryGetValue(i.MedicineId, out var n) ? n : $"Medicine #{i.MedicineId}",
                    MedicineBatchId = i.MedicineBatchId,
                    BatchNumber = i.BatchNumber,
                    ExpiryDate = i.ExpiryDate,
                    Quantity = i.Quantity,
                    FreeQuantity = i.FreeQuantity,
                    AlreadyReturnedQty = r.Qty,
                    AlreadyReturnedFreeQty = r.Free,
                    StockOnHand = Math.Max(0, stock),
                    PurchasePrice = i.PurchasePrice,
                    GstPercent = i.GstPercent,
                    DiscountPercent = i.DiscountPercent,
                    LineTotal = i.LineTotal
                };
            }).Where(l => l.AvailableQty > 0 || l.AvailableFreeQty > 0).ToList()
        };

        if (dto.Lines.Count == 0)
        {
            var anyBillQty = purchase.Items.Any(i =>
            {
                returned.TryGetValue(i.Id, out var r);
                return i.Quantity - r.Qty > 0.009m || i.FreeQuantity - r.Free > 0.009m;
            });
            return Result.Failure<PurchaseForReturnDto>(anyBillQty
                ? "This bill still has returnable lines, but on-hand stock for those batches is 0. Check Inventory / Stock, or use Direct return only if stock exists under another batch."
                : "No returnable quantity left on this purchase.");
        }

        return Result.Success(dto);
    }

    public async Task<Result<PurchaseReturnReceiptDto>> CreateReturnAsync(
        CreatePurchaseReturnRequest request, int? branchId, string? userName, CancellationToken ct = default)
    {
        if (!_financialYear.CanEditTransactions)
            return FinancialYearGuard.FailIfReadOnly<PurchaseReturnReceiptDto>(_financialYear);
        try
        {
            var receipt = await PersistReturnAsync(request, branchId, userName, ct);
            await _reportingSync.EnqueuePurchaseReturnAsync(receipt.PurchaseReturnId, ct);
            return Result.Success(receipt);
        }
        catch (PurchaseReturnException ex)
        {
            return Result.Failure<PurchaseReturnReceiptDto>(ex.Message);
        }
    }

    public async Task<Result<PurchaseReturnReceiptDto>> CreateDirectReturnAsync(
        CreateDirectPurchaseReturnRequest request, int? branchId, string? userName, CancellationToken ct = default)
    {
        if (!_financialYear.CanEditTransactions)
            return FinancialYearGuard.FailIfReadOnly<PurchaseReturnReceiptDto>(_financialYear);
        try
        {
            var receipt = await PersistDirectReturnAsync(request, branchId, userName, ct);
            await _reportingSync.EnqueuePurchaseReturnAsync(receipt.PurchaseReturnId, ct);
            return Result.Success(receipt);
        }
        catch (PurchaseReturnException ex)
        {
            return Result.Failure<PurchaseReturnReceiptDto>(ex.Message);
        }
    }

    public async Task<Result<DirectReturnBatchDto>> GetBatchForDirectReturnAsync(
        int medicineBatchId, int? branchId, CancellationToken ct = default)
    {
        var batch = await _uow.Repository<MedicineBatch>().Query().AsNoTracking()
            .Include(b => b.Medicine)
            .FirstOrDefaultAsync(b => b.Id == medicineBatchId, ct);

        if (batch is null)
            return Result.Failure<DirectReturnBatchDto>("Stock batch not found.");
        if (branchId.HasValue && batch.BranchId != branchId)
            return Result.Failure<DirectReturnBatchDto>("Batch belongs to another branch.");
        if (batch.QuantityAvailable <= 0)
            return Result.Failure<DirectReturnBatchDto>("No stock available on this batch.");

        return Result.Success(new DirectReturnBatchDto
        {
            MedicineBatchId = batch.Id,
            MedicineId = batch.MedicineId,
            MedicineName = batch.Medicine?.Name ?? $"Medicine #{batch.MedicineId}",
            BatchNumber = batch.BatchNumber,
            ExpiryDate = batch.ExpiryDate,
            QuantityAvailable = batch.QuantityAvailable,
            PurchasePrice = batch.PurchasePrice,
            GstPercent = batch.GstPercent
        });
    }

    public async Task<List<PurchaseReturnListRowDto>> ListReturnsAsync(
        bool pendingSupplierReceiptOnly, int? branchId, int take = 100, CancellationToken ct = default)
    {
        await ClearPlaceholderSupplierReceiptNumbersAsync(ct);

        var q = _uow.Repository<PurchaseReturn>().Query().AsNoTracking()
            .Where(r => r.Status == PurchaseReturnStatus.Completed
                        && r.ReturnKind == PurchaseReturnKind.Standard);
        // Pending receipt work spans years (MedWin imports); do not FY-scope that filter.
        if (!pendingSupplierReceiptOnly)
            q = q.WhereInFinancialYear(_financialYear.Active, r => r.ReturnDate);
        if (branchId.HasValue) q = q.Where(r => r.BranchId == branchId);
        if (pendingSupplierReceiptOnly)
            q = q.Where(r => r.SupplierReturnReceiptNumber == null || r.SupplierReturnReceiptNumber == "");

        var limit = pendingSupplierReceiptOnly ? Math.Max(take, 2000) : take;
        return await q.OrderByDescending(r => r.ReturnDate).Take(limit)
            .Select(r => new PurchaseReturnListRowDto(
                r.Id,
                r.ReturnNumber,
                r.ReturnDate,
                r.SupplierId,
                r.Purchase != null ? r.Purchase.InvoiceNumber : "Direct",
                r.Purchase != null ? r.Purchase.SupplierInvoiceNumber : null,
                r.Supplier != null ? r.Supplier.Name : "—",
                r.GrandTotal,
                r.SupplierReturnReceiptNumber,
                r.SupplierReturnReceiptDate,
                r.SupplierReturnReceiptNumber != null && r.SupplierReturnReceiptNumber != "",
                r.PurchaseId == null,
                r.ReceiptSettlementKind,
                r.SettledAgainstPurchaseId,
                r.SupplierReturnReceiptNumber == null || r.SupplierReturnReceiptNumber == ""
                    ? null
                    : r.ReceiptSettlementKind == PurchaseReturnReceiptSettlementKind.PurchaseBill
                        ? $"Bill {r.SupplierReturnReceiptNumber}"
                        : $"Receipt {r.SupplierReturnReceiptNumber}",
                r.PurchaseId))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Clears MedWin-era placeholder debit notes ("0") so returns stay pending until a real receipt is saved.
    /// </summary>
    private async Task ClearPlaceholderSupplierReceiptNumbersAsync(CancellationToken ct)
    {
        var placeholders = await _uow.Repository<PurchaseReturn>().Query()
            .Where(r => r.SupplierReturnReceiptNumber == "0"
                        || r.SupplierReturnReceiptNumber == "0.0"
                        || r.SupplierReturnReceiptNumber == "0.00")
            .ToListAsync(ct);
        if (placeholders.Count == 0) return;

        foreach (var row in placeholders)
        {
            row.SupplierReturnReceiptNumber = null;
            row.SupplierReturnReceiptDate = null;
        }

        await _uow.SaveChangesAsync(ct);
    }

    public async Task<List<PurchaseReturnSupplierBillOptionDto>> ListSupplierBillsAsync(
        int supplierId, int? branchId, CancellationToken ct = default)
    {
        if (supplierId <= 0) return [];

        var q = _uow.Repository<Purchase>().Query().AsNoTracking()
            .Where(p => p.SupplierId == supplierId
                        && p.Status != PurchaseStatus.Cancelled
                        && p.Status != PurchaseStatus.Draft);
        if (branchId.HasValue) q = q.Where(p => p.BranchId == branchId);

        var rows = await q.OrderByDescending(p => p.InvoiceDate).ThenByDescending(p => p.Id)
            .Take(200)
            .Select(p => new
            {
                p.Id,
                p.InvoiceNumber,
                p.SupplierInvoiceNumber,
                p.InvoiceDate,
                p.GrandTotal,
                p.PaidAmount
            })
            .ToListAsync(ct);

        var ids = rows.Select(r => r.Id).ToList();
        var adjusted = new Dictionary<int, decimal>();
        if (ids.Count > 0)
        {
            adjusted = await _uow.Repository<PurchaseReturn>().Query().AsNoTracking()
                .Where(r => r.Status == PurchaseReturnStatus.Completed
                            && r.SettledAgainstPurchaseId != null
                            && ids.Contains(r.SettledAgainstPurchaseId.Value))
                .GroupBy(r => r.SettledAgainstPurchaseId!.Value)
                .Select(g => new { Id = g.Key, Adj = g.Sum(x => x.CreditAmount) })
                .ToDictionaryAsync(x => x.Id, x => x.Adj, ct);
        }

        return rows.Select(p => new PurchaseReturnSupplierBillOptionDto(
            p.Id,
            p.InvoiceNumber,
            p.SupplierInvoiceNumber,
            p.InvoiceDate,
            p.GrandTotal,
            p.PaidAmount,
            adjusted.TryGetValue(p.Id, out var a) ? a : 0m)).ToList();
    }

    public async Task<Result<PurchaseReturnDetailDto>> GetReturnDetailsAsync(
        int purchaseReturnId, int? branchId, CancellationToken ct = default)
    {
        var ret = await _uow.Repository<PurchaseReturn>().Query().AsNoTracking()
            .Include(r => r.Purchase)
            .Include(r => r.Supplier)
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == purchaseReturnId, ct);

        if (ret is null)
            return Result.Failure<PurchaseReturnDetailDto>("Purchase return not found.");
        if (branchId.HasValue && ret.BranchId != branchId)
            return Result.Failure<PurchaseReturnDetailDto>("Return belongs to another branch.");

        var medIds = ret.Items.Select(i => i.MedicineId).Distinct().ToList();
        var names = await _uow.Repository<Medicine>().QueryIncludingDeleted().AsNoTracking()
            .Where(m => medIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.Name, ct);

        var reasonIds = ret.Items.Where(i => i.ReturnReasonId.HasValue)
            .Select(i => i.ReturnReasonId!.Value).Distinct().ToList();
        var reasons = reasonIds.Count == 0
            ? new Dictionary<int, string>()
            : await _uow.Repository<ReturnReason>().Query().AsNoTracking()
                .Where(r => reasonIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, r => r.Name, ct);

        return Result.Success(new PurchaseReturnDetailDto
        {
            Id = ret.Id,
            SupplierId = ret.SupplierId,
            ReturnNumber = ret.ReturnNumber,
            ReturnDate = ret.ReturnDate,
            SupplierName = ret.Supplier?.Name ?? "—",
            PurchaseInvoiceNumber = ret.Purchase?.InvoiceNumber ?? "Direct",
            IsDirectReturn = ret.PurchaseId is null,
            Remarks = ret.Remarks,
            GrandTotal = ret.GrandTotal,
            CanEditLines = !ret.HasSupplierReceipt,
            ReceiptSettlementKind = ret.ReceiptSettlementKind,
            SupplierReturnReceiptNumber = ret.SupplierReturnReceiptNumber,
            SupplierReturnReceiptDate = ret.SupplierReturnReceiptDate,
            SettledAgainstPurchaseId = ret.SettledAgainstPurchaseId,
            Lines = ret.Items
                .OrderBy(i => i.Id)
                .Select(i => new PurchaseReturnDetailLineDto
                {
                    Id = i.Id,
                    MedicineName = names.TryGetValue(i.MedicineId, out var n) ? n : $"Medicine #{i.MedicineId}",
                    BatchNumber = i.BatchNumber,
                    ExpiryDate = i.ExpiryDate,
                    ReturnedQuantity = i.ReturnedQuantity,
                    ReturnedFreeQuantity = i.ReturnedFreeQuantity,
                    PurchasePrice = i.PurchasePrice,
                    DiscountPercent = i.DiscountPercent,
                    GstPercent = i.GstPercent,
                    RefundPercent = i.RefundPercent <= 0 ? 100m : i.RefundPercent,
                    LineTotal = i.LineTotal,
                    ReasonName = i.ReturnReasonId is int rid && reasons.TryGetValue(rid, out var rn) ? rn : null,
                    ReasonRemarks = i.ReasonRemarks
                })
                .ToList()
        });
    }

    public async Task<Result<PurchaseReturnDetailDto>> UpdateReturnLinesAsync(
        UpdatePurchaseReturnLinesRequest request, string? userName, CancellationToken ct = default)
    {
        var fyBlock = FinancialYearGuard.EnsureEditable(_financialYear);
        if (fyBlock.IsFailure)
            return Result.Failure<PurchaseReturnDetailDto>(fyBlock.Error ?? "Read-only financial year.");

        var ret = await _uow.Repository<PurchaseReturn>().Query()
            .Include(r => r.Items)
            .Include(r => r.Supplier)
            .Include(r => r.Purchase)
            .FirstOrDefaultAsync(r => r.Id == request.PurchaseReturnId, ct);

        if (ret is null)
            return Result.Failure<PurchaseReturnDetailDto>("Purchase return not found.");
        if (ret.HasSupplierReceipt)
            return Result.Failure<PurchaseReturnDetailDto>(
                "This return is already settled with a receipt / purchase bill and cannot be edited.");
        if (request.Lines.Count == 0)
            return Result.Failure<PurchaseReturnDetailDto>("No lines to update.");

        var oldGrand = ret.GrandTotal;
        var byId = ret.Items.ToDictionary(i => i.Id);

        foreach (var lineReq in request.Lines)
        {
            if (!byId.TryGetValue(lineReq.Id, out var item))
                return Result.Failure<PurchaseReturnDetailDto>($"Return line #{lineReq.Id} was not found.");

            if (lineReq.ReturnedQuantity < 0 || lineReq.ReturnedFreeQuantity < 0)
                return Result.Failure<PurchaseReturnDetailDto>("Quantity cannot be negative.");
            if (lineReq.ReturnedQuantity + lineReq.ReturnedFreeQuantity <= 0)
                return Result.Failure<PurchaseReturnDetailDto>("Each line needs quantity greater than zero.");

            var refundPct = lineReq.RefundPercent <= 0 ? 0 : Math.Min(lineReq.RefundPercent, 999m);
            var oldStock = item.ReturnedQuantity + item.ReturnedFreeQuantity;
            var newStock = lineReq.ReturnedQuantity + lineReq.ReturnedFreeQuantity;
            var stockDelta = newStock - oldStock;

            if (stockDelta != 0 && item.MedicineBatchId is int batchId)
            {
                var batch = await _uow.Repository<MedicineBatch>().GetByIdAsync(batchId, ct);
                if (batch is null)
                    return Result.Failure<PurchaseReturnDetailDto>($"Stock batch missing for {item.BatchNumber}.");
                if (stockDelta > 0 && batch.QuantityAvailable < stockDelta)
                    return Result.Failure<PurchaseReturnDetailDto>(
                        $"Insufficient stock for {item.BatchNumber}. Available {batch.QuantityAvailable:0.##}.");
                batch.QuantityAvailable -= stockDelta;
                _uow.Repository<MedicineBatch>().Update(batch);
            }

            var (taxable, tax, lineTotal) = CalcReturnLineAmounts(
                lineReq.PurchasePrice, lineReq.ReturnedQuantity, item.DiscountPercent, lineReq.GstPercent, refundPct);

            item.BatchNumber = string.IsNullOrWhiteSpace(lineReq.BatchNumber) ? item.BatchNumber : lineReq.BatchNumber.Trim();
            item.ExpiryDate = lineReq.ExpiryDate;
            item.ReturnedQuantity = lineReq.ReturnedQuantity;
            item.ReturnedFreeQuantity = lineReq.ReturnedFreeQuantity;
            item.PurchasePrice = lineReq.PurchasePrice;
            item.GstPercent = lineReq.GstPercent;
            item.RefundPercent = refundPct;
            item.TaxableAmount = taxable;
            item.TaxAmount = tax;
            item.LineTotal = lineTotal;
            item.DiscountAmount = Math.Round(
                lineReq.PurchasePrice * lineReq.ReturnedQuantity * item.DiscountPercent / 100m, 2);
            item.ReasonRemarks = string.IsNullOrWhiteSpace(lineReq.ReasonRemarks) ? null : lineReq.ReasonRemarks.Trim();
            item.ModifiedBy = userName;
            _uow.Repository<PurchaseReturnItem>().Update(item);
        }

        var taxableSum = ret.Items.Sum(i => i.TaxableAmount);
        var taxSum = ret.Items.Sum(i => i.TaxAmount);
        var grand = Math.Round(ret.Items.Sum(i => i.LineTotal), 2);
        var cgst = Math.Round(taxSum / 2m, 2);
        var sgst = taxSum - cgst;

        ret.SubTotal = Math.Round(ret.Items.Sum(i => i.PurchasePrice * i.ReturnedQuantity), 2);
        ret.DiscountAmount = Math.Max(0, Math.Round(ret.SubTotal - taxableSum, 2));
        ret.TaxableAmount = taxableSum;
        ret.CgstAmount = cgst;
        ret.SgstAmount = sgst;
        ret.GrandTotal = grand;
        ret.CreditAmount = grand;
        if (ret.CreditAppliedAmount > ret.CreditAmount)
            ret.CreditAppliedAmount = ret.CreditAmount;
        ret.ModifiedBy = userName;
        _uow.Repository<PurchaseReturn>().Update(ret);

        var delta = oldGrand - grand; // positive => less credit to supplier
        if (Math.Abs(delta) >= 0.005m)
        {
            var supplier = await _uow.Repository<Supplier>().GetByIdAsync(ret.SupplierId, ct);
            if (supplier is not null)
            {
                supplier.OutstandingBalance += delta;
                _uow.Repository<Supplier>().Update(supplier);
            }

            if (ret.PurchaseId is int purchaseId)
            {
                var purchase = await _uow.Repository<Purchase>().GetByIdAsync(purchaseId, ct);
                if (purchase is not null)
                {
                    purchase.GrandTotal = Math.Max(0m, Math.Round(purchase.GrandTotal + delta, 2));
                    if (purchase.PaidAmount > purchase.GrandTotal)
                        purchase.PaidAmount = purchase.GrandTotal;
                    purchase.PaymentStatus = purchase.GrandTotal <= 0m || purchase.PaidAmount >= purchase.GrandTotal
                        ? PaymentStatus.Paid
                        : purchase.PaidAmount > 0m
                            ? PaymentStatus.PartiallyPaid
                            : PaymentStatus.Unpaid;
                    _uow.Repository<Purchase>().Update(purchase);
                }
            }
        }

        await _uow.SaveChangesAsync(ct);
        return await GetReturnDetailsAsync(ret.Id, ret.BranchId, ct);
    }

    private static (decimal Taxable, decimal Tax, decimal LineTotal) CalcReturnLineAmounts(
        decimal price, decimal qty, decimal discountPercent, decimal gstPercent, decimal refundPercent)
    {
        var disc = Math.Clamp(discountPercent, 0m, 100m);
        var gst = Math.Max(0m, gstPercent);
        var refund = Math.Clamp(refundPercent, 0m, 999m);
        var taxable = Math.Round(price * qty * (1m - disc / 100m), 2);
        var tax = Math.Round(taxable * gst / 100m, 2);
        var gross = taxable + tax;
        var lineTotal = Math.Round(gross * refund / 100m, 2);
        return (taxable, tax, lineTotal);
    }

    public async Task<Result> AttachSupplierReceiptAsync(
        AttachPurchaseReturnReceiptRequest request, string? userName, CancellationToken ct = default)
    {
        var fyBlock = FinancialYearGuard.EnsureEditable(_financialYear);
        if (fyBlock.IsFailure) return fyBlock;

        var ret = await _uow.Repository<PurchaseReturn>().GetByIdAsync(request.PurchaseReturnId, ct);
        if (ret is null) return Result.Failure("Purchase return not found.");
        if (ret.Status != PurchaseReturnStatus.Completed)
            return Result.Failure("Only completed returns can receive a supplier receipt number.");

        string settlementRef;
        DateTime settlementDate;
        int? settledPurchaseId = null;

        if (request.SettlementKind == PurchaseReturnReceiptSettlementKind.PurchaseBill)
        {
            if (request.SettledAgainstPurchaseId is null or <= 0)
                return Result.Failure("Select the purchase bill that includes this credit.");

            var bill = await _uow.Repository<Purchase>().Query()
                .FirstOrDefaultAsync(p => p.Id == request.SettledAgainstPurchaseId.Value, ct);
            if (bill is null)
                return Result.Failure("Purchase bill not found.");
            if (bill.SupplierId != ret.SupplierId)
                return Result.Failure("Selected bill belongs to a different supplier.");
            if (bill.Status is PurchaseStatus.Cancelled or PurchaseStatus.Draft)
                return Result.Failure("Select a posted purchase bill.");

            settledPurchaseId = bill.Id;
            settlementRef = string.IsNullOrWhiteSpace(bill.SupplierInvoiceNumber)
                ? bill.InvoiceNumber
                : $"{bill.InvoiceNumber} / {bill.SupplierInvoiceNumber}";
            settlementDate = request.ReceiptDate?.Date ?? bill.InvoiceDate.Date;

            // Keep purchase.ReturnCreditApplied in sync so bill viewer / purchase register
            // show the same credit Parties use for Adjusted (PaidAmount stays cash-only).
            var previousApplied = ret.SettledAgainstPurchaseId == bill.Id ? ret.CreditAmount : 0m;
            var nextApplied = Math.Max(0m, bill.ReturnCreditApplied - previousApplied + ret.CreditAmount);
            bill.ReturnCreditApplied = nextApplied;
            bill.ModifiedBy = userName;
            _uow.Repository<Purchase>().Update(bill);
        }
        else
        {
            settlementRef = request.ReceiptNumber?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(settlementRef))
                return Result.Failure("Enter the supplier return receipt / debit note number.");
            settlementDate = request.ReceiptDate?.Date ?? _clock.Now.Date;

            // Clearing a prior bill settlement restores that bill's ReturnCreditApplied.
            if (ret.SettledAgainstPurchaseId is int priorBillId)
            {
                var priorBill = await _uow.Repository<Purchase>().Query()
                    .FirstOrDefaultAsync(p => p.Id == priorBillId, ct);
                if (priorBill is not null)
                {
                    priorBill.ReturnCreditApplied = Math.Max(0m, priorBill.ReturnCreditApplied - ret.CreditAmount);
                    priorBill.ModifiedBy = userName;
                    _uow.Repository<Purchase>().Update(priorBill);
                }
            }
        }

        ret.ReceiptSettlementKind = request.SettlementKind;
        ret.SettledAgainstPurchaseId = settledPurchaseId;
        ret.SupplierReturnReceiptNumber = settlementRef;
        ret.SupplierReturnReceiptDate = settlementDate;
        if (settledPurchaseId is not null)
            ret.CreditAppliedAmount = ret.CreditAmount;
        ret.ModifiedBy = userName;
        _uow.Repository<PurchaseReturn>().Update(ret);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<PurchaseReturnReceiptDto> PersistReturnAsync(
        CreatePurchaseReturnRequest request, int? branchId, string? userName, CancellationToken ct)
    {
        if (request.Lines.Count == 0)
            throw new PurchaseReturnException("Select at least one line to return.");

        var purchase = await _uow.Repository<Purchase>().Query()
            .Include(p => p.Items)
            .Include(p => p.Supplier)
            .FirstOrDefaultAsync(p => p.Id == request.PurchaseId, ct)
            ?? throw new PurchaseReturnException("Purchase bill not found.");

        if (branchId.HasValue && purchase.BranchId != branchId)
            throw new PurchaseReturnException("Purchase belongs to another branch.");
        if (purchase.Status is PurchaseStatus.Cancelled or PurchaseStatus.Draft or PurchaseStatus.Returned)
            throw new PurchaseReturnException("This purchase cannot be returned.");

        var returned = await LoadReturnedQuantitiesAsync(purchase.Id, ct);
        var itemMap = purchase.Items.ToDictionary(i => i.Id);
        var resolved = new List<(PurchaseItem Item, CreatePurchaseReturnLineRequest Req, decimal Taxable, decimal Tax, decimal LineTotal)>();

        foreach (var line in request.Lines)
        {
            if (line.ReturnQuantity < 0 || line.ReturnFreeQuantity < 0)
                throw new PurchaseReturnException("Return quantity cannot be negative.");
            if (line.ReturnQuantity <= 0 && line.ReturnFreeQuantity <= 0)
                continue;
            if (!itemMap.TryGetValue(line.PurchaseItemId, out var item))
                throw new PurchaseReturnException("A selected purchase line no longer exists.");

            returned.TryGetValue(item.Id, out var prev);
            var billQty = Math.Max(0, item.Quantity - prev.Qty);
            var billFree = Math.Max(0, item.FreeQuantity - prev.Free);

            if (line.ReturnQuantity > billQty + 0.0001m)
                throw new PurchaseReturnException(
                    $"Return qty exceeds available for batch {item.BatchNumber} (max {billQty:0.##}). " +
                    (billQty <= 0.009m
                        ? "This purchase line is already fully returned — use Direct return if stock still exists on another/open batch."
                        : string.Empty));
            if (line.ReturnFreeQuantity > billFree + 0.0001m)
                throw new PurchaseReturnException(
                    $"Return free qty exceeds available for batch {item.BatchNumber} (max {billFree:0.##}).");

            decimal stockOnHand = 0m;
            if (item.MedicineBatchId is int batchIdForStock)
            {
                var stockBatch = await _uow.Repository<MedicineBatch>().Query().AsNoTracking()
                    .FirstOrDefaultAsync(b => b.Id == batchIdForStock, ct);
                stockOnHand = Math.Max(0, stockBatch?.QuantityAvailable ?? 0m);
            }

            var needStock = line.ReturnQuantity + line.ReturnFreeQuantity;
            if (needStock > stockOnHand + 0.0001m)
                throw new PurchaseReturnException(
                    $"Insufficient stock for batch {item.BatchNumber}. On hand {stockOnHand:0.##}, need {needStock:0.##}.");

            // Value on paid qty. MedWin lines sometimes have LineTotal=0 — fall back to price + GST.
            var lineTotal = Math.Round(UnitReturnValue(item) * line.ReturnQuantity, 2);
            var taxable = Math.Round(UnitTaxableValue(item) * line.ReturnQuantity, 2);
            var tax = Math.Round(lineTotal - taxable, 2);
            resolved.Add((item, line, taxable, tax, lineTotal));
        }

        if (resolved.Count == 0)
            throw new PurchaseReturnException("Select at least one line with quantity to return.");

        var taxableSum = resolved.Sum(r => r.Taxable);
        var taxSum = resolved.Sum(r => r.Tax);
        var grand = Math.Round(resolved.Sum(r => r.LineTotal), 2);
        var cgst = Math.Round(taxSum / 2m, 2);
        var sgst = taxSum - cgst;

        var purchaseReturn = new PurchaseReturn
        {
            ReturnNumber = await GenerateReturnNumberAsync(branchId, ct),
            PurchaseId = purchase.Id,
            SupplierId = purchase.SupplierId,
            ReturnDate = _clock.Now,
            BranchId = branchId ?? purchase.BranchId,
            SubTotal = Math.Round(resolved.Sum(r => r.Item.PurchasePrice * r.Req.ReturnQuantity), 2),
            DiscountAmount = Math.Max(0, Math.Round(resolved.Sum(r => r.Item.PurchasePrice * r.Req.ReturnQuantity) - taxableSum, 2)),
            TaxableAmount = taxableSum,
            CgstAmount = cgst,
            SgstAmount = sgst,
            RoundOff = 0,
            GrandTotal = grand,
            CreditAmount = grand,
            SettlementMode = request.SettlementMode,
            Status = PurchaseReturnStatus.Completed,
            Remarks = request.Remarks,
            IsFullReturn = IsFullReturn(purchase, returned, resolved),
            CreatedBy = userName
        };

        await _uow.Repository<PurchaseReturn>().AddAsync(purchaseReturn, ct);
        await _uow.SaveChangesAsync(ct);

        foreach (var (item, req, taxable, tax, lineTotal) in resolved)
        {
            var stockQty = req.ReturnQuantity + req.ReturnFreeQuantity;
            if (item.MedicineBatchId is null or <= 0)
                throw new PurchaseReturnException($"Batch missing for {item.BatchNumber}.");

            var batch = await _uow.Repository<MedicineBatch>().GetByIdAsync(item.MedicineBatchId.Value, ct)
                ?? throw new PurchaseReturnException($"Stock batch not found for {item.BatchNumber}.");

            if (batch.QuantityAvailable < stockQty)
                throw new PurchaseReturnException(
                    $"Insufficient stock for {item.BatchNumber}. Available {batch.QuantityAvailable:0.##}, need {stockQty:0.##}.");

            batch.QuantityAvailable -= stockQty;
            _uow.Repository<MedicineBatch>().Update(batch);

            await _uow.Repository<PurchaseReturnItem>().AddAsync(new PurchaseReturnItem
            {
                PurchaseReturnId = purchaseReturn.Id,
                PurchaseItemId = item.Id,
                MedicineId = item.MedicineId,
                MedicineBatchId = item.MedicineBatchId,
                BatchNumber = item.BatchNumber,
                ExpiryDate = item.ExpiryDate,
                ReturnedQuantity = req.ReturnQuantity,
                ReturnedFreeQuantity = req.ReturnFreeQuantity,
                PurchasePrice = item.PurchasePrice,
                DiscountPercent = item.DiscountPercent,
                DiscountAmount = Math.Round(item.PurchasePrice * req.ReturnQuantity * item.DiscountPercent / 100m, 2),
                GstPercent = item.GstPercent,
                TaxableAmount = taxable,
                TaxAmount = tax,
                LineTotal = lineTotal,
                RefundPercent = 100m,
                ReturnReasonId = req.ReturnReasonId,
                ReasonRemarks = req.ReasonRemarks,
                CreatedBy = userName
            }, ct);

            await _uow.Repository<StockMovement>().AddAsync(new StockMovement
            {
                BranchId = branchId ?? purchase.BranchId,
                MedicineId = item.MedicineId,
                MedicineBatchId = batch.Id,
                MovementType = StockMovementType.PurchaseReturn,
                Quantity = -stockQty,
                BalanceAfter = batch.QuantityAvailable,
                UnitCost = item.PurchasePrice,
                ReferenceType = nameof(PurchaseReturn),
                ReferenceId = purchaseReturn.Id,
                ReferenceNumber = purchaseReturn.ReturnNumber,
                MovementDateUtc = _clock.UtcNow,
                Remarks = $"Return to supplier — {purchase.InvoiceNumber}"
            }, ct);
        }

        // Reduce what we owe the supplier (or create credit if already paid).
        var supplier = await _uow.Repository<Supplier>().GetByIdAsync(purchase.SupplierId, ct)
            ?? throw new PurchaseReturnException("Supplier not found.");
        supplier.OutstandingBalance -= grand;
        _uow.Repository<Supplier>().Update(supplier);

        var unpaidBefore = Math.Max(0m, purchase.GrandTotal - purchase.PaidAmount);
        await UpdatePurchaseStatusAsync(purchase, returned, resolved, ct);
        await NetPurchaseBillFromReturnsAsync(purchase, ct);

        // Portion that reduced unpaid due is consumed on this bill.
        var appliedToBill = Math.Min(grand, unpaidBefore);
        purchaseReturn.CreditAppliedAmount += appliedToBill;
        if (request.SettlementMode == PurchaseReturnSettlementMode.CashRefund)
            purchaseReturn.CreditAppliedAmount = purchaseReturn.CreditAmount;
        _uow.Repository<PurchaseReturn>().Update(purchaseReturn);
        _uow.Repository<Purchase>().Update(purchase);
        await _uow.SaveChangesAsync(ct);

        var balanceDue = Math.Max(0m, purchase.GrandTotal - purchase.PaidAmount);
        return new PurchaseReturnReceiptDto
        {
            PurchaseReturnId = purchaseReturn.Id,
            ReturnNumber = purchaseReturn.ReturnNumber,
            PurchaseInvoiceNumber = purchase.InvoiceNumber,
            SupplierName = supplier.Name,
            ReturnDate = purchaseReturn.ReturnDate,
            GrandTotal = purchaseReturn.GrandTotal,
            AppliedToPurchaseBill = appliedToBill,
            PurchaseBalanceDueAfter = balanceDue,
            RemainingSupplierCredit = purchaseReturn.RemainingCredit,
            IsFullReturn = purchaseReturn.IsFullReturn,
            IsDirectReturn = false,
            SupplierReturnReceiptNumber = null
        };
    }

    /// <summary>
    /// Sets purchase GrandTotal to remaining goods value (sum of lines minus completed returns).
    /// Idempotent — also repairs MedWin bills returned earlier that still showed the original amount.
    /// </summary>
    private async Task NetPurchaseBillFromReturnsAsync(Purchase purchase, CancellationToken ct)
    {
        var returnedTotal = await _uow.Repository<PurchaseReturn>().Query().AsNoTracking()
            .Where(r => r.PurchaseId == purchase.Id && r.Status == PurchaseReturnStatus.Completed)
            .SumAsync(r => (decimal?)r.GrandTotal, ct) ?? 0m;
        if (returnedTotal <= 0m)
            return;

        var linesTotal = purchase.Items.Sum(i => i.LineTotal > 0
            ? i.LineTotal
            : Math.Round(i.PurchasePrice * i.Quantity * (1 + i.GstPercent / 100m), 2));

        // Header still looks like the original invoice (at/above line goods value).
        // After we net once, GrandTotal falls below line totals so we do not subtract again.
        if (linesTotal > 0.01m && purchase.GrandTotal + 1m < linesTotal)
            return;

        purchase.GrandTotal = Math.Max(0m, Math.Round(purchase.GrandTotal - returnedTotal, 2));
        if (purchase.PaidAmount > purchase.GrandTotal)
            purchase.PaidAmount = purchase.GrandTotal;

        purchase.PaymentStatus = purchase.GrandTotal <= 0m || purchase.PaidAmount >= purchase.GrandTotal
            ? PaymentStatus.Paid
            : purchase.PaidAmount > 0m
                ? PaymentStatus.PartiallyPaid
                : PaymentStatus.Unpaid;
    }

    private static decimal UnitReturnValue(PurchaseItem item)
    {
        if (item.Quantity > 0 && item.LineTotal > 0)
            return item.LineTotal / item.Quantity;
        var taxable = UnitTaxableValue(item);
        return Math.Round(taxable * (1 + item.GstPercent / 100m), 4);
    }

    private static decimal UnitTaxableValue(PurchaseItem item)
    {
        if (item.Quantity > 0 && item.TaxableAmount > 0)
            return item.TaxableAmount / item.Quantity;
        var gross = item.PurchasePrice;
        return item.DiscountPercent > 0
            ? Math.Round(gross * (1 - item.DiscountPercent / 100m), 4)
            : gross;
    }

    private async Task<PurchaseReturnReceiptDto> PersistDirectReturnAsync(
        CreateDirectPurchaseReturnRequest request, int? branchId, string? userName, CancellationToken ct)
    {
        if (request.SupplierId <= 0)
            throw new PurchaseReturnException("Select a supplier.");
        if (request.Lines.Count == 0)
            throw new PurchaseReturnException("Add at least one medicine to return.");

        var supplier = await _uow.Repository<Supplier>().GetByIdAsync(request.SupplierId, ct)
            ?? throw new PurchaseReturnException("Supplier not found.");

        var resolved = new List<(MedicineBatch Batch, CreateDirectPurchaseReturnLineRequest Req, decimal Taxable, decimal Tax, decimal LineTotal, decimal Discount)>();

        foreach (var line in request.Lines)
        {
            if (line.ReturnQuantity < 0 || line.ReturnFreeQuantity < 0)
                throw new PurchaseReturnException("Return quantity cannot be negative.");
            if (line.ReturnQuantity <= 0 && line.ReturnFreeQuantity <= 0)
                continue;
            if (line.PurchasePrice < 0)
                throw new PurchaseReturnException("Purchase price cannot be negative.");
            if (line.MedicineBatchId <= 0)
                throw new PurchaseReturnException("Each line must have a stock batch.");

            var batch = await _uow.Repository<MedicineBatch>().GetByIdAsync(line.MedicineBatchId, ct)
                ?? throw new PurchaseReturnException("Stock batch not found.");
            if (branchId.HasValue && batch.BranchId != branchId)
                throw new PurchaseReturnException($"Batch {batch.BatchNumber} belongs to another branch.");

            var stockQty = line.ReturnQuantity + line.ReturnFreeQuantity;
            if (batch.QuantityAvailable < stockQty)
                throw new PurchaseReturnException(
                    $"Insufficient stock for {batch.BatchNumber}. Available {batch.QuantityAvailable:0.##}, need {stockQty:0.##}.");

            var discount = Math.Round(line.PurchasePrice * line.ReturnQuantity * line.DiscountPercent / 100m, 2);
            var taxable = Math.Round(line.PurchasePrice * line.ReturnQuantity - discount, 2);
            var tax = Math.Round(taxable * line.GstPercent / 100m, 2);
            var lineTotal = Math.Round(taxable + tax, 2);
            resolved.Add((batch, line, taxable, tax, lineTotal, discount));
        }

        if (resolved.Count == 0)
            throw new PurchaseReturnException("Add at least one line with quantity to return.");

        // Deduct stock once per unique batch after validating combined quantities.
        var byBatch = resolved.GroupBy(r => r.Batch.Id);
        foreach (var g in byBatch)
        {
            var need = g.Sum(x => x.Req.ReturnQuantity + x.Req.ReturnFreeQuantity);
            var batch = g.First().Batch;
            if (batch.QuantityAvailable < need)
                throw new PurchaseReturnException(
                    $"Insufficient stock for {batch.BatchNumber}. Available {batch.QuantityAvailable:0.##}, need {need:0.##}.");
        }

        var taxableSum = resolved.Sum(r => r.Taxable);
        var taxSum = resolved.Sum(r => r.Tax);
        var grand = Math.Round(resolved.Sum(r => r.LineTotal), 2);
        var cgst = Math.Round(taxSum / 2m, 2);
        var sgst = taxSum - cgst;

        var purchaseReturn = new PurchaseReturn
        {
            ReturnNumber = await GenerateReturnNumberAsync(branchId, ct),
            PurchaseId = null,
            SupplierId = supplier.Id,
            ReturnDate = _clock.Now,
            BranchId = branchId ?? supplier.BranchId,
            SubTotal = Math.Round(resolved.Sum(r => r.Req.PurchasePrice * r.Req.ReturnQuantity), 2),
            DiscountAmount = Math.Round(resolved.Sum(r => r.Discount), 2),
            TaxableAmount = taxableSum,
            CgstAmount = cgst,
            SgstAmount = sgst,
            RoundOff = 0,
            GrandTotal = grand,
            CreditAmount = grand,
            SettlementMode = request.SettlementMode,
            ReturnKind = request.ReturnKind,
            Status = PurchaseReturnStatus.Completed,
            Remarks = request.Remarks,
            IsFullReturn = false,
            CreatedBy = userName
        };

        await _uow.Repository<PurchaseReturn>().AddAsync(purchaseReturn, ct);
        await _uow.SaveChangesAsync(ct);

        foreach (var (batch, req, taxable, tax, lineTotal, discount) in resolved)
        {
            var stockQty = req.ReturnQuantity + req.ReturnFreeQuantity;
            batch.QuantityAvailable -= stockQty;
            _uow.Repository<MedicineBatch>().Update(batch);

            await _uow.Repository<PurchaseReturnItem>().AddAsync(new PurchaseReturnItem
            {
                PurchaseReturnId = purchaseReturn.Id,
                PurchaseItemId = null,
                MedicineId = batch.MedicineId,
                MedicineBatchId = batch.Id,
                BatchNumber = batch.BatchNumber,
                ExpiryDate = batch.ExpiryDate,
                ReturnedQuantity = req.ReturnQuantity,
                ReturnedFreeQuantity = req.ReturnFreeQuantity,
                PurchasePrice = req.PurchasePrice,
                DiscountPercent = req.DiscountPercent,
                DiscountAmount = discount,
                GstPercent = req.GstPercent,
                TaxableAmount = taxable,
                TaxAmount = tax,
                LineTotal = lineTotal,
                RefundPercent = 100m,
                ReturnReasonId = req.ReturnReasonId,
                ReasonRemarks = req.ReasonRemarks,
                CreatedBy = userName
            }, ct);

            await _uow.Repository<StockMovement>().AddAsync(new StockMovement
            {
                BranchId = branchId ?? purchaseReturn.BranchId,
                MedicineId = batch.MedicineId,
                MedicineBatchId = batch.Id,
                MovementType = StockMovementType.PurchaseReturn,
                Quantity = -stockQty,
                BalanceAfter = batch.QuantityAvailable,
                UnitCost = req.PurchasePrice,
                ReferenceType = nameof(PurchaseReturn),
                ReferenceId = purchaseReturn.Id,
                ReferenceNumber = purchaseReturn.ReturnNumber,
                MovementDateUtc = _clock.UtcNow,
                Remarks = request.ReturnKind == PurchaseReturnKind.ExpiryToCompany
                    ? $"Expiry-to-company — {supplier.Name}"
                    : $"Direct return to supplier — {supplier.Name}"
            }, ct);
        }

        supplier.OutstandingBalance -= grand;
        _uow.Repository<Supplier>().Update(supplier);
        await _uow.SaveChangesAsync(ct);

        return new PurchaseReturnReceiptDto
        {
            PurchaseReturnId = purchaseReturn.Id,
            ReturnNumber = purchaseReturn.ReturnNumber,
            PurchaseInvoiceNumber = "Direct",
            SupplierName = supplier.Name,
            ReturnDate = purchaseReturn.ReturnDate,
            GrandTotal = purchaseReturn.GrandTotal,
            IsFullReturn = false,
            IsDirectReturn = true,
            SupplierReturnReceiptNumber = null
        };
    }

    private async Task UpdatePurchaseStatusAsync(
        Purchase purchase,
        Dictionary<int, (decimal Qty, decimal Free)> previouslyReturned,
        List<(PurchaseItem Item, CreatePurchaseReturnLineRequest Req, decimal Taxable, decimal Tax, decimal LineTotal)> resolved,
        CancellationToken ct)
    {
        foreach (var (item, req, _, _, _) in resolved)
        {
            if (!previouslyReturned.TryGetValue(item.Id, out var prev))
                prev = (0, 0);
            previouslyReturned[item.Id] = (prev.Qty + req.ReturnQuantity, prev.Free + req.ReturnFreeQuantity);
        }

        var full = purchase.Items.All(i =>
        {
            previouslyReturned.TryGetValue(i.Id, out var r);
            return r.Qty >= i.Quantity && r.Free >= i.FreeQuantity;
        });

        purchase.Status = full ? PurchaseStatus.Returned : PurchaseStatus.PartiallyReturned;
        _uow.Repository<Purchase>().Update(purchase);
        await Task.CompletedTask;
    }

    private static bool IsFullReturn(
        Purchase purchase,
        Dictionary<int, (decimal Qty, decimal Free)> previouslyReturned,
        List<(PurchaseItem Item, CreatePurchaseReturnLineRequest Req, decimal Taxable, decimal Tax, decimal LineTotal)> resolved)
    {
        var map = previouslyReturned.ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var (item, req, _, _, _) in resolved)
        {
            map.TryGetValue(item.Id, out var prev);
            map[item.Id] = (prev.Qty + req.ReturnQuantity, prev.Free + req.ReturnFreeQuantity);
        }

        return purchase.Items.All(i =>
        {
            map.TryGetValue(i.Id, out var r);
            return r.Qty >= i.Quantity && r.Free >= i.FreeQuantity;
        });
    }

    private async Task<Dictionary<int, (decimal Qty, decimal Free)>> LoadReturnedQuantitiesAsync(
        int purchaseId, CancellationToken ct)
    {
        var rows = await _uow.Repository<PurchaseReturnItem>().Query().AsNoTracking()
            .Where(i => i.PurchaseReturn!.PurchaseId == purchaseId
                        && i.PurchaseItemId != null
                        && i.PurchaseReturn.Status == PurchaseReturnStatus.Completed)
            .GroupBy(i => i.PurchaseItemId!.Value)
            .Select(g => new
            {
                PurchaseItemId = g.Key,
                Qty = g.Sum(x => x.ReturnedQuantity),
                Free = g.Sum(x => x.ReturnedFreeQuantity)
            })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.PurchaseItemId, r => (r.Qty, r.Free));
    }

    private async Task<string> GenerateReturnNumberAsync(int? branchId, CancellationToken ct)
    {
        var company = await _uow.Repository<CompanyProfile>().Query().AsNoTracking().FirstOrDefaultAsync(ct);
        var prefix = string.IsNullOrWhiteSpace(company?.PurchaseReturnPrefix) ? "PR" : company!.PurchaseReturnPrefix.Trim();
        var day = _clock.Now.ToString("yyyyMMdd");
        var stem = $"{prefix}-{day}-";

        var last = await _uow.Repository<PurchaseReturn>().Query().AsNoTracking()
            .Where(r => r.ReturnNumber.StartsWith(stem))
            .OrderByDescending(r => r.ReturnNumber)
            .Select(r => r.ReturnNumber)
            .FirstOrDefaultAsync(ct);

        var next = 1;
        if (last is not null && last.Length > stem.Length
            && int.TryParse(last[stem.Length..], out var n))
            next = n + 1;

        return $"{stem}{next:D4}";
    }
}

public sealed class PurchaseReturnException : Exception
{
    public PurchaseReturnException(string message) : base(message) { }
}
