using Microsoft.EntityFrameworkCore;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Domain.Entities.Inventory;
using PharmaPOS.Domain.Entities.Masters;
using PharmaPOS.Domain.Entities.Purchases;
using PharmaPOS.Domain.Entities.Sales;
using PharmaPOS.Domain.Entities.System;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.PurchaseReturns;

public class PurchaseReturnService : IPurchaseReturnService
{
    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _clock;

    public PurchaseReturnService(IUnitOfWork uow, IDateTimeProvider clock)
    {
        _uow = uow;
        _clock = clock;
    }

    public Task<List<ReturnReasonOptionDto>> ListReturnReasonsAsync(CancellationToken ct = default)
        => _uow.Repository<ReturnReason>().Query().AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Name)
            .Select(r => new ReturnReasonOptionDto(r.Id, r.Code, r.Name, r.RequiresRemarks))
            .ToListAsync(ct);

    public async Task<List<PurchaseReturnSearchResultDto>> SearchPurchasesAsync(
        string term, int? branchId, CancellationToken ct = default)
    {
        term = term?.Trim() ?? string.Empty;
        if (term.Length < 1) return [];

        var q = _uow.Repository<Purchase>().Query().AsNoTracking()
            .Where(p => p.Status == PurchaseStatus.Received
                        || p.Status == PurchaseStatus.PartiallyReturned
                        || p.Status == PurchaseStatus.Returned);

        if (branchId.HasValue) q = q.Where(p => p.BranchId == branchId);

        var normalized = term.Replace(" ", "");
        q = q.Where(p =>
            p.InvoiceNumber.Contains(term)
            || (p.SupplierInvoiceNumber != null && p.SupplierInvoiceNumber.Contains(term))
            || (p.Supplier != null && p.Supplier.Name.Contains(term)));

        return await q.OrderByDescending(p => p.InvoiceDate).Take(50)
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
                    PurchasePrice = i.PurchasePrice,
                    GstPercent = i.GstPercent,
                    DiscountPercent = i.DiscountPercent,
                    LineTotal = i.LineTotal
                };
            }).Where(l => l.AvailableQty > 0 || l.AvailableFreeQty > 0).ToList()
        };

        if (dto.Lines.Count == 0)
            return Result.Failure<PurchaseForReturnDto>("No returnable quantity left on this purchase.");

        return Result.Success(dto);
    }

    public async Task<Result<PurchaseReturnReceiptDto>> CreateReturnAsync(
        CreatePurchaseReturnRequest request, int? branchId, string? userName, CancellationToken ct = default)
    {
        try
        {
            return Result.Success(await PersistReturnAsync(request, branchId, userName, ct));
        }
        catch (PurchaseReturnException ex)
        {
            return Result.Failure<PurchaseReturnReceiptDto>(ex.Message);
        }
    }

    public async Task<Result<PurchaseReturnReceiptDto>> CreateDirectReturnAsync(
        CreateDirectPurchaseReturnRequest request, int? branchId, string? userName, CancellationToken ct = default)
    {
        try
        {
            return Result.Success(await PersistDirectReturnAsync(request, branchId, userName, ct));
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
        var q = _uow.Repository<PurchaseReturn>().Query().AsNoTracking()
            .Where(r => r.Status == PurchaseReturnStatus.Completed);
        if (branchId.HasValue) q = q.Where(r => r.BranchId == branchId);
        if (pendingSupplierReceiptOnly)
            q = q.Where(r => r.SupplierReturnReceiptNumber == null || r.SupplierReturnReceiptNumber == "");

        return await q.OrderByDescending(r => r.ReturnDate).Take(take)
            .Select(r => new PurchaseReturnListRowDto(
                r.Id,
                r.ReturnNumber,
                r.ReturnDate,
                r.Purchase != null ? r.Purchase.InvoiceNumber : "Direct",
                r.Purchase != null ? r.Purchase.SupplierInvoiceNumber : null,
                r.Supplier != null ? r.Supplier.Name : "—",
                r.GrandTotal,
                r.SupplierReturnReceiptNumber,
                r.SupplierReturnReceiptDate,
                r.SupplierReturnReceiptNumber != null && r.SupplierReturnReceiptNumber != "",
                r.PurchaseId == null))
            .ToListAsync(ct);
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
            ReturnNumber = ret.ReturnNumber,
            ReturnDate = ret.ReturnDate,
            SupplierName = ret.Supplier?.Name ?? "—",
            PurchaseInvoiceNumber = ret.Purchase?.InvoiceNumber ?? "Direct",
            IsDirectReturn = ret.PurchaseId is null,
            Remarks = ret.Remarks,
            GrandTotal = ret.GrandTotal,
            Lines = ret.Items
                .OrderBy(i => i.Id)
                .Select(i => new PurchaseReturnDetailLineDto(
                    names.TryGetValue(i.MedicineId, out var n) ? n : $"Medicine #{i.MedicineId}",
                    i.BatchNumber,
                    i.ExpiryDate,
                    i.ReturnedQuantity,
                    i.ReturnedFreeQuantity,
                    i.PurchasePrice,
                    i.GstPercent,
                    i.LineTotal,
                    i.ReturnReasonId is int rid && reasons.TryGetValue(rid, out var rn) ? rn : null))
                .ToList()
        });
    }

    public async Task<Result> AttachSupplierReceiptAsync(
        int purchaseReturnId, string receiptNumber, DateTime? receiptDate, string? userName, CancellationToken ct = default)
    {
        receiptNumber = receiptNumber?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(receiptNumber))
            return Result.Failure("Enter the supplier return receipt / debit note number.");

        var ret = await _uow.Repository<PurchaseReturn>().GetByIdAsync(purchaseReturnId, ct);
        if (ret is null) return Result.Failure("Purchase return not found.");
        if (ret.Status != PurchaseReturnStatus.Completed)
            return Result.Failure("Only completed returns can receive a supplier receipt number.");

        ret.SupplierReturnReceiptNumber = receiptNumber;
        ret.SupplierReturnReceiptDate = receiptDate?.Date ?? _clock.Now.Date;
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
            var availQty = item.Quantity - prev.Qty;
            var availFree = item.FreeQuantity - prev.Free;
            if (line.ReturnQuantity > availQty)
                throw new PurchaseReturnException(
                    $"Return qty exceeds available for batch {item.BatchNumber} (max {availQty:0.##}).");
            if (line.ReturnFreeQuantity > availFree)
                throw new PurchaseReturnException(
                    $"Return free qty exceeds available for batch {item.BatchNumber} (max {availFree:0.##}).");

            // Value only on paid qty (same as purchase line costing).
            var unitTaxable = item.Quantity > 0 ? item.TaxableAmount / item.Quantity : 0m;
            var unitTax = item.Quantity > 0 ? item.TaxAmount / item.Quantity : 0m;
            var unitTotal = item.Quantity > 0 ? item.LineTotal / item.Quantity : 0m;
            var taxable = Math.Round(unitTaxable * line.ReturnQuantity, 2);
            var tax = Math.Round(unitTax * line.ReturnQuantity, 2);
            var lineTotal = Math.Round(unitTotal * line.ReturnQuantity, 2);
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

        await UpdatePurchaseStatusAsync(purchase, returned, resolved, ct);
        await _uow.SaveChangesAsync(ct);

        return new PurchaseReturnReceiptDto
        {
            PurchaseReturnId = purchaseReturn.Id,
            ReturnNumber = purchaseReturn.ReturnNumber,
            PurchaseInvoiceNumber = purchase.InvoiceNumber,
            SupplierName = supplier.Name,
            ReturnDate = purchaseReturn.ReturnDate,
            GrandTotal = purchaseReturn.GrandTotal,
            IsFullReturn = purchaseReturn.IsFullReturn,
            IsDirectReturn = false,
            SupplierReturnReceiptNumber = null
        };
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
                Remarks = $"Direct return to supplier — {supplier.Name}"
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
