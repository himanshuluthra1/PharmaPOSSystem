using Microsoft.EntityFrameworkCore;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Domain.Entities.Inventory;
using PharmaPOS.Domain.Entities.Masters;
using PharmaPOS.Domain.Entities.Purchases;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.Purchases;

public sealed class PurchaseOrderService : IPurchaseOrderService
{
    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _clock;

    public PurchaseOrderService(IUnitOfWork uow, IDateTimeProvider clock)
    {
        _uow = uow;
        _clock = clock;
    }

    public async Task<List<PurchaseOrderListItemDto>> ListAsync(int? branchId, CancellationToken ct = default)
    {
        var q = _uow.Repository<PurchaseOrder>().Query().AsNoTracking()
            .Include(p => p.Supplier)
            .Include(p => p.Items)
            .Where(p => !p.IsDeleted);
        if (branchId.HasValue)
            q = q.Where(p => p.BranchId == branchId);

        var rows = await q.OrderByDescending(p => p.OrderDate).ThenByDescending(p => p.Id).Take(300).ToListAsync(ct);
        return rows.Select(MapList).ToList();
    }

    public async Task<Result<PurchaseOrderDetailDto>> GetAsync(int purchaseOrderId, int? branchId, CancellationToken ct = default)
    {
        var po = await LoadTrackedAsync(purchaseOrderId, branchId, asNoTracking: true, ct);
        if (po is null)
            return Result.Failure<PurchaseOrderDetailDto>("Purchase order not found.");
        return Result.Success(MapDetail(po));
    }

    public async Task<Result<PurchaseOrderDetailDto>> SaveDraftAsync(
        SavePurchaseOrderRequest request, int? branchId, CancellationToken ct = default)
    {
        if (request.SupplierId <= 0)
            return Result.Failure<PurchaseOrderDetailDto>("Select a supplier.");
        if (request.Lines.Count == 0)
            return Result.Failure<PurchaseOrderDetailDto>("Add at least one medicine line.");
        if (request.Lines.Any(l => l.MedicineId <= 0 || l.Quantity <= 0))
            return Result.Failure<PurchaseOrderDetailDto>("Each line needs a medicine and quantity > 0.");

        var supplier = await _uow.Repository<Supplier>().GetByIdAsync(request.SupplierId, ct);
        if (supplier is null || supplier.IsDeleted)
            return Result.Failure<PurchaseOrderDetailDto>("Supplier not found.");

        try
        {
            var detail = await _uow.ExecuteInTransactionAsync(async token =>
            {
                PurchaseOrder po;
                if (request.Id is int id and > 0)
                {
                    po = await LoadTrackedAsync(id, branchId, asNoTracking: false, token)
                         ?? throw new InvalidOperationException("Purchase order not found.");
                    if (po.Status != PurchaseStatus.Draft)
                        throw new InvalidOperationException("Only draft purchase orders can be edited.");
                    foreach (var old in po.Items.ToList())
                        _uow.Repository<PurchaseOrderItem>().Remove(old);
                    po.Items.Clear();
                }
                else
                {
                    po = new PurchaseOrder
                    {
                        BranchId = branchId,
                        OrderNumber = await GenerateOrderNumberAsync(branchId, token),
                        Status = PurchaseStatus.Draft
                    };
                    await _uow.Repository<PurchaseOrder>().AddAsync(po, token);
                }

                po.SupplierId = request.SupplierId;
                po.OrderDate = request.OrderDate == default ? _clock.Now : request.OrderDate;
                po.ExpectedDate = request.ExpectedDate;
                po.Remarks = request.Remarks;
                po.Status = PurchaseStatus.Draft;

                decimal total = 0m;
                foreach (var line in request.Lines)
                {
                    var medicine = await _uow.Repository<Medicine>().GetByIdAsync(line.MedicineId, token)
                        ?? throw new InvalidOperationException("A selected medicine no longer exists.");
                    var price = line.EstimatedPrice > 0 ? line.EstimatedPrice : medicine.PurchasePrice;
                    po.Items.Add(new PurchaseOrderItem
                    {
                        MedicineId = line.MedicineId,
                        Quantity = line.Quantity,
                        ReceivedQuantity = 0,
                        EstimatedPrice = price
                    });
                    total += Math.Round(line.Quantity * price, 2);
                }

                po.TotalAmount = total;
                _uow.Repository<PurchaseOrder>().Update(po);
                await _uow.SaveChangesAsync(token);

                var saved = await LoadTrackedAsync(po.Id, branchId, asNoTracking: true, token)
                            ?? throw new InvalidOperationException("Could not reload purchase order.");
                return MapDetail(saved);
            }, ct);

            return Result.Success(detail);
        }
        catch (Exception ex)
        {
            return Result.Failure<PurchaseOrderDetailDto>(ex.Message);
        }
    }

    public async Task<Result<PurchaseOrderDetailDto>> ConfirmAsync(int purchaseOrderId, int? branchId, CancellationToken ct = default)
    {
        try
        {
            var detail = await _uow.ExecuteInTransactionAsync(async token =>
            {
                var po = await LoadTrackedAsync(purchaseOrderId, branchId, asNoTracking: false, token)
                         ?? throw new InvalidOperationException("Purchase order not found.");
                if (po.Status != PurchaseStatus.Draft)
                    throw new InvalidOperationException("Only draft orders can be confirmed.");
                if (po.Items.Count == 0)
                    throw new InvalidOperationException("Cannot confirm an empty purchase order.");

                po.Status = PurchaseStatus.Ordered;
                _uow.Repository<PurchaseOrder>().Update(po);
                await _uow.SaveChangesAsync(token);
                return MapDetail(po);
            }, ct);
            return Result.Success(detail);
        }
        catch (Exception ex)
        {
            return Result.Failure<PurchaseOrderDetailDto>(ex.Message);
        }
    }

    public async Task<Result> CancelAsync(int purchaseOrderId, int? branchId, CancellationToken ct = default)
    {
        try
        {
            await _uow.ExecuteInTransactionAsync(async token =>
            {
                var po = await LoadTrackedAsync(purchaseOrderId, branchId, asNoTracking: false, token)
                         ?? throw new InvalidOperationException("Purchase order not found.");
                if (po.Status is not (PurchaseStatus.Draft or PurchaseStatus.Ordered))
                    throw new InvalidOperationException("Only draft or ordered POs with no receipt can be cancelled.");
                if (po.Items.Any(i => i.ReceivedQuantity > 0))
                    throw new InvalidOperationException("Cannot cancel a PO that already has receipts.");

                po.Status = PurchaseStatus.Cancelled;
                _uow.Repository<PurchaseOrder>().Update(po);
                await _uow.SaveChangesAsync(token);
                return true;
            }, ct);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(ex.Message);
        }
    }

    public async Task<Result<SuggestReorderResultDto>> GenerateFromLowStockAsync(int? branchId, CancellationToken ct = default)
    {
        try
        {
            var result = await _uow.ExecuteInTransactionAsync(async token =>
            {
                var medicines = await _uow.Repository<Medicine>().Query().AsNoTracking()
                    .Where(m => m.Status == EntityStatus.Active && !m.IsDeleted && m.ReorderLevel > 0)
                    .Select(m => new { m.Id, m.Name, m.PurchasePrice, m.ReorderLevel, m.ReorderQuantity })
                    .ToListAsync(token);

                var stockQ = _uow.Repository<MedicineBatch>().Query().AsNoTracking()
                    .Where(b => !b.IsDeleted);
                if (branchId.HasValue)
                    stockQ = stockQ.Where(b => b.BranchId == branchId);
                var stockLookup = await stockQ
                    .GroupBy(b => b.MedicineId)
                    .Select(g => new { MedicineId = g.Key, Qty = g.Sum(x => x.QuantityAvailable) })
                    .ToDictionaryAsync(x => x.MedicineId, x => x.Qty, token);

                var low = medicines
                    .Select(m =>
                    {
                        stockLookup.TryGetValue(m.Id, out var qty);
                        var suggest = m.ReorderQuantity > 0 ? m.ReorderQuantity : m.ReorderLevel;
                        return new { m.Id, m.Name, m.PurchasePrice, OnHand = qty, m.ReorderLevel, SuggestQty = (decimal)suggest };
                    })
                    .Where(x => x.OnHand <= x.ReorderLevel && x.SuggestQty > 0)
                    .ToList();

                if (low.Count == 0)
                {
                    return new SuggestReorderResultDto(0, 0, 0, Array.Empty<PurchaseOrderListItemDto>());
                }

                var lastSupplier = await GetLastSupplierByMedicineAsync(
                    low.Select(x => x.Id).ToList(), branchId, token);

                var grouped = new Dictionary<int, List<(int MedicineId, decimal Qty, decimal Price)>>();
                var skipped = 0;
                foreach (var item in low)
                {
                    if (!lastSupplier.TryGetValue(item.Id, out var supplierId))
                    {
                        skipped++;
                        continue;
                    }

                    if (!grouped.TryGetValue(supplierId, out var list))
                    {
                        list = new List<(int, decimal, decimal)>();
                        grouped[supplierId] = list;
                    }

                    list.Add((item.Id, item.SuggestQty, item.PurchasePrice));
                }

                var created = new List<PurchaseOrderListItemDto>();
                var included = 0;
                foreach (var (supplierId, lines) in grouped)
                {
                    var po = new PurchaseOrder
                    {
                        BranchId = branchId,
                        OrderNumber = await GenerateOrderNumberAsync(branchId, token),
                        OrderDate = _clock.Now,
                        SupplierId = supplierId,
                        Status = PurchaseStatus.Draft,
                        Remarks = "Auto-generated from low stock"
                    };

                    decimal total = 0m;
                    foreach (var (medicineId, qty, price) in lines)
                    {
                        po.Items.Add(new PurchaseOrderItem
                        {
                            MedicineId = medicineId,
                            Quantity = qty,
                            ReceivedQuantity = 0,
                            EstimatedPrice = price
                        });
                        total += Math.Round(qty * price, 2);
                        included++;
                    }

                    po.TotalAmount = total;
                    await _uow.Repository<PurchaseOrder>().AddAsync(po, token);
                    await _uow.SaveChangesAsync(token);

                    var reloaded = await LoadTrackedAsync(po.Id, branchId, asNoTracking: true, token);
                    if (reloaded is not null)
                        created.Add(MapList(reloaded));
                }

                return new SuggestReorderResultDto(created.Count, included, skipped, created);
            }, ct);

            return Result.Success(result);
        }
        catch (Exception ex)
        {
            return Result.Failure<SuggestReorderResultDto>(ex.Message);
        }
    }

    public async Task<Result<PurchaseOrderReceiveDraftDto>> GetReceiveDraftAsync(
        int purchaseOrderId, int? branchId, CancellationToken ct = default)
    {
        var po = await LoadTrackedAsync(purchaseOrderId, branchId, asNoTracking: true, ct);
        if (po is null)
            return Result.Failure<PurchaseOrderReceiveDraftDto>("Purchase order not found.");
        if (po.Status is PurchaseStatus.Draft or PurchaseStatus.Cancelled or PurchaseStatus.Received)
            return Result.Failure<PurchaseOrderReceiveDraftDto>(
                "Receive is only allowed for ordered or partially received purchase orders.");

        var remaining = po.Items
            .Select(i => new { Item = i, Rem = i.Quantity - i.ReceivedQuantity })
            .Where(x => x.Rem > 0)
            .ToList();
        if (remaining.Count == 0)
            return Result.Failure<PurchaseOrderReceiveDraftDto>("Nothing left to receive on this purchase order.");

        var lines = new List<PurchaseOrderReceiveLineDto>();
        foreach (var row in remaining)
        {
            var med = row.Item.Medicine
                      ?? await _uow.Repository<Medicine>().Query().AsNoTracking()
                          .FirstOrDefaultAsync(m => m.Id == row.Item.MedicineId, ct);
            lines.Add(new PurchaseOrderReceiveLineDto(
                row.Item.MedicineId,
                med?.Name ?? $"#{row.Item.MedicineId}",
                med?.GenericName,
                row.Rem,
                row.Item.EstimatedPrice > 0 ? row.Item.EstimatedPrice : med?.PurchasePrice ?? 0m,
                med?.GstPercent ?? 0m,
                med?.Mrp ?? 0m,
                med?.SellingPrice ?? 0m));
        }

        return Result.Success(new PurchaseOrderReceiveDraftDto(
            po.Id,
            po.OrderNumber,
            po.SupplierId,
            po.Supplier?.Name ?? $"#{po.SupplierId}",
            lines));
    }

    /// <summary>
    /// Updates received qty/status. Safe to call inside an outer UoW transaction
    /// (does not begin its own transaction).
    /// </summary>
    public async Task ApplyReceiptAsync(
        int purchaseOrderId,
        IReadOnlyDictionary<int, decimal> receivedByMedicineId,
        CancellationToken ct = default)
    {
        if (receivedByMedicineId.Count == 0) return;

        var po = await _uow.Repository<PurchaseOrder>().Query()
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == purchaseOrderId && !p.IsDeleted, ct);
        if (po is null) return;

        foreach (var item in po.Items)
        {
            if (!receivedByMedicineId.TryGetValue(item.MedicineId, out var qty) || qty <= 0)
                continue;
            item.ReceivedQuantity = Math.Min(item.Quantity, item.ReceivedQuantity + qty);
            _uow.Repository<PurchaseOrderItem>().Update(item);
        }

        var anyReceived = po.Items.Any(i => i.ReceivedQuantity > 0);
        var allDone = po.Items.All(i => i.ReceivedQuantity >= i.Quantity);
        po.Status = allDone
            ? PurchaseStatus.Received
            : anyReceived
                ? PurchaseStatus.PartiallyReceived
                : po.Status;
        _uow.Repository<PurchaseOrder>().Update(po);
        await _uow.SaveChangesAsync(ct);
    }

    private async Task<Dictionary<int, int>> GetLastSupplierByMedicineAsync(
        IReadOnlyList<int> medicineIds, int? branchId, CancellationToken ct)
    {
        var result = new Dictionary<int, int>();
        if (medicineIds.Count == 0) return result;

        var q = _uow.Repository<PurchaseItem>().Query().AsNoTracking()
            .Where(i => !i.IsDeleted && medicineIds.Contains(i.MedicineId) && i.Purchase != null && !i.Purchase.IsDeleted);
        if (branchId.HasValue)
            q = q.Where(i => i.Purchase!.BranchId == branchId);

        var rows = await q
            .OrderByDescending(i => i.Purchase!.InvoiceDate)
            .ThenByDescending(i => i.PurchaseId)
            .Select(i => new { i.MedicineId, i.Purchase!.SupplierId, i.Purchase.InvoiceDate, i.PurchaseId })
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            if (!result.ContainsKey(row.MedicineId))
                result[row.MedicineId] = row.SupplierId;
        }

        return result;
    }

    private async Task<PurchaseOrder?> LoadTrackedAsync(
        int id, int? branchId, bool asNoTracking, CancellationToken ct)
    {
        IQueryable<PurchaseOrder> q = _uow.Repository<PurchaseOrder>().Query()
            .Include(p => p.Supplier)
            .Include(p => p.Items).ThenInclude(i => i.Medicine)
            .Where(p => p.Id == id && !p.IsDeleted);
        if (asNoTracking) q = q.AsNoTracking();
        if (branchId.HasValue) q = q.Where(p => p.BranchId == branchId);
        return await q.FirstOrDefaultAsync(ct);
    }

    private async Task<string> GenerateOrderNumberAsync(int? branchId, CancellationToken ct)
    {
        var today = _clock.Today;
        var tomorrow = today.AddDays(1);
        var q = _uow.Repository<PurchaseOrder>().Query()
            .Where(p => p.CreatedAtUtc >= today && p.CreatedAtUtc < tomorrow);
        if (branchId.HasValue) q = q.Where(p => p.BranchId == branchId);
        var count = await q.CountAsync(ct);
        return $"PO-{today:yyyyMMdd}-{count + 1:D4}";
    }

    private static PurchaseOrderListItemDto MapList(PurchaseOrder po)
    {
        var ordered = po.Items.Sum(i => i.Quantity);
        var received = po.Items.Sum(i => i.ReceivedQuantity);
        return new PurchaseOrderListItemDto(
            po.Id,
            po.OrderNumber,
            po.OrderDate,
            po.SupplierId,
            po.Supplier?.Name ?? $"#{po.SupplierId}",
            po.Status,
            po.TotalAmount,
            po.Items.Count,
            ordered,
            received);
    }

    private static PurchaseOrderDetailDto MapDetail(PurchaseOrder po)
    {
        var lines = po.Items
            .OrderBy(i => i.Id)
            .Select(i => new PurchaseOrderLineDto(
                i.Id,
                i.MedicineId,
                i.Medicine?.Name ?? $"#{i.MedicineId}",
                i.Medicine?.GenericName,
                i.Quantity,
                i.ReceivedQuantity,
                Math.Max(0, i.Quantity - i.ReceivedQuantity),
                i.EstimatedPrice,
                Math.Round(i.Quantity * i.EstimatedPrice, 2)))
            .ToList();

        return new PurchaseOrderDetailDto(
            po.Id,
            po.OrderNumber,
            po.OrderDate,
            po.ExpectedDate,
            po.SupplierId,
            po.Supplier?.Name ?? $"#{po.SupplierId}",
            po.Status,
            po.TotalAmount,
            po.Remarks,
            lines);
    }
}
