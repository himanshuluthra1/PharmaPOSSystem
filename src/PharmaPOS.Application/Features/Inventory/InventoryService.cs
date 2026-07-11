using Microsoft.EntityFrameworkCore;
using PharmaPOS.Application.Common;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Domain.Entities.Inventory;
using PharmaPOS.Domain.Entities.Masters;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Application.Features.Settings;
using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.Inventory;

public class InventoryService : IInventoryService
{
    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _clock;
    private readonly ISettingsService _settings;

    public InventoryService(IUnitOfWork uow, IDateTimeProvider clock, ISettingsService settings)
    {
        _uow = uow;
        _clock = clock;
        _settings = settings;
    }

    public async Task<StockSummaryDto> GetStockSummaryAsync(int? branchId, CancellationToken ct = default)
    {
        var today = _clock.Today;
        var prefs = await _settings.GetPreferencesAsync(ct);
        var nearExpiryDate = today.AddDays(prefs.NearExpiryDays);

        var batches = BatchQuery(branchId);
        var activeBatches = batches.Where(b => b.QuantityAvailable > 0);

        var summary = new StockSummaryDto
        {
            TotalBatches = await activeBatches.CountAsync(ct),
            TotalMedicines = await activeBatches.Select(b => b.MedicineId).Distinct().CountAsync(ct),
            TotalQuantity = await activeBatches.SumAsync(b => (decimal?)b.QuantityAvailable, ct) ?? 0m,
            StockValue = await activeBatches.SumAsync(b => (decimal?)(b.PurchasePrice * b.QuantityAvailable), ct) ?? 0m,
            ExpiredCount = await activeBatches.CountAsync(
                b => b.ExpiryDate != null && b.ExpiryDate < today, ct),
            NearExpiryCount = await activeBatches.CountAsync(
                b => b.ExpiryDate != null && b.ExpiryDate >= today && b.ExpiryDate <= nearExpiryDate, ct)
        };

        summary.LowStockCount = await CountLowStockMedicinesAsync(branchId, ct);
        return summary;
    }

    public async Task<List<StockBatchRowDto>> SearchStockBatchesAsync(
        string term,
        StockFilterKind filter,
        int? branchId,
        CancellationToken ct = default)
    {
        term = term.Trim();
        var today = _clock.Today;
        var prefs = await _settings.GetPreferencesAsync(ct);
        var nearExpiryDate = today.AddDays(prefs.NearExpiryDays);

        var batches = BatchQuery(branchId);
        var medicines = _uow.Repository<Medicine>().Query()
            .Where(m => m.Status == EntityStatus.Active);

        if (!string.IsNullOrWhiteSpace(term))
        {
            var normalized = SearchQueryExtensions.NormalizeTerm(term);
            batches = batches.Where(b =>
                b.BatchNumber.Replace(" ", "").Contains(normalized) ||
                (b.Medicine != null && (
                    b.Medicine.NameSearchKey.Contains(normalized) ||
                    (b.Medicine.GenericNameSearchKey != "" && b.Medicine.GenericNameSearchKey.Contains(normalized)) ||
                    (b.Medicine.BarcodeSearchKey != "" && b.Medicine.BarcodeSearchKey.Contains(normalized)))));
        }

        if (filter == StockFilterKind.LowStock)
        {
            batches = await FilterLowStockBatchesAsync(batches, branchId, ct);
        }
        else
        {
            batches = filter switch
            {
                StockFilterKind.InStock => batches.Where(b => b.QuantityAvailable > 0),
                StockFilterKind.ZeroStock => batches.Where(b => b.QuantityAvailable == 0),
                StockFilterKind.Expired => batches.Where(b =>
                    b.QuantityAvailable > 0 && b.ExpiryDate != null && b.ExpiryDate < today),
                StockFilterKind.NearExpiry => batches.Where(b =>
                    b.QuantityAvailable > 0 &&
                    b.ExpiryDate != null &&
                    b.ExpiryDate >= today &&
                    b.ExpiryDate <= nearExpiryDate),
                _ => batches
            };
        }

        var rows = await batches
            .OrderBy(b => b.Medicine!.Name)
            .ThenBy(b => b.ExpiryDate)
            .ThenBy(b => b.BatchNumber)
            .Select(b => new
            {
                b.Id,
                b.MedicineId,
                MedicineName = b.Medicine!.Name,
                b.Medicine.GenericName,
                b.BatchNumber,
                b.ExpiryDate,
                b.QuantityAvailable,
                b.PurchasePrice,
                b.Mrp,
                b.SellingPrice,
                RackNumber = b.RackNumber ?? b.Medicine.RackNumber,
                b.Medicine.ReorderLevel
            })
            .Take(1000)
            .ToListAsync(ct);

        if (rows.Count == 0) return [];

        var medicineIds = rows.Select(r => r.MedicineId).Distinct().ToList();
        var totals = await BatchQuery(branchId)
            .Where(b => medicineIds.Contains(b.MedicineId))
            .GroupBy(b => b.MedicineId)
            .Select(g => new { MedicineId = g.Key, Total = g.Sum(x => x.QuantityAvailable) })
            .ToDictionaryAsync(x => x.MedicineId, x => x.Total, ct);

        return rows.Select(r =>
        {
            var medTotal = totals.TryGetValue(r.MedicineId, out var t) ? t : r.QuantityAvailable;
            var isLow = r.ReorderLevel > 0 && medTotal <= r.ReorderLevel;
            var isExpired = r.ExpiryDate.HasValue && r.ExpiryDate.Value.Date < today;
            var isNear = !isExpired &&
                         r.QuantityAvailable > 0 &&
                         r.ExpiryDate.HasValue &&
                         r.ExpiryDate.Value.Date >= today &&
                         r.ExpiryDate.Value.Date <= nearExpiryDate;

            return new StockBatchRowDto(
                r.Id,
                r.MedicineId,
                r.MedicineName,
                r.GenericName,
                r.BatchNumber,
                r.ExpiryDate,
                r.QuantityAvailable,
                r.PurchasePrice,
                r.Mrp,
                r.SellingPrice,
                r.RackNumber,
                r.ReorderLevel,
                medTotal,
                isLow,
                isNear,
                isExpired);
        }).ToList();
    }

    public async Task<List<StockLedgerRowDto>> GetStockLedgerAsync(
        string? term,
        int? medicineId,
        int? batchId,
        int? branchId,
        int take = 500,
        CancellationToken ct = default)
    {
        term = term?.Trim() ?? string.Empty;
        var q = _uow.Repository<StockMovement>().Query().AsNoTracking();
        if (branchId.HasValue) q = q.Where(m => m.BranchId == branchId);
        if (medicineId.HasValue) q = q.Where(m => m.MedicineId == medicineId.Value);
        if (batchId.HasValue) q = q.Where(m => m.MedicineBatchId == batchId.Value);

        if (!string.IsNullOrWhiteSpace(term))
        {
            var normalized = SearchQueryExtensions.NormalizeTerm(term);
            q = q.Where(m =>
                (m.Medicine != null && m.Medicine.NameSearchKey.Contains(normalized)) ||
                (m.ReferenceNumber != null && m.ReferenceNumber.Replace(" ", "").Contains(normalized)) ||
                (m.Remarks != null && m.Remarks.Replace(" ", "").Contains(normalized)));
        }

        return await q
            .OrderByDescending(m => m.MovementDateUtc)
            .ThenByDescending(m => m.Id)
            .Take(take)
            .Select(m => new StockLedgerRowDto(
                m.Id,
                m.MovementDateUtc,
                m.MovementType,
                m.Medicine != null ? m.Medicine.Name : $"Medicine #{m.MedicineId}",
                m.MedicineBatch != null ? m.MedicineBatch.BatchNumber : null,
                m.Quantity,
                m.BalanceAfter,
                m.UnitCost,
                m.ReferenceNumber,
                m.Remarks))
            .ToListAsync(ct);
    }

    public Task<string> PreviewNextAdjustmentNumberAsync(int? branchId, CancellationToken ct = default)
        => GenerateAdjustmentNumberAsync(branchId, ct);

    public async Task<Result<StockAdjustmentReceiptDto>> CreateStockAdjustmentAsync(
        CreateStockAdjustmentRequest request,
        int? branchId,
        CancellationToken ct = default)
    {
        var lines = request.Lines
            .Where(l => l.PhysicalQuantity != l.SystemQuantity)
            .ToList();

        if (lines.Count == 0)
            return Result.Failure<StockAdjustmentReceiptDto>("Add at least one line with a quantity difference.");

        try
        {
            var receipt = await _uow.ExecuteInTransactionAsync(async token =>
            {
                var adjustmentNumber = await GenerateAdjustmentNumberAsync(branchId, token);
                var adjustment = new StockAdjustment
                {
                    BranchId = branchId,
                    AdjustmentNumber = adjustmentNumber,
                    AdjustmentDate = request.AdjustmentDate,
                    Reason = request.Reason
                };
                await _uow.Repository<StockAdjustment>().AddAsync(adjustment, token);
                await _uow.SaveChangesAsync(token);

                foreach (var line in lines)
                {
                    var batch = await _uow.Repository<MedicineBatch>().GetByIdAsync(line.MedicineBatchId, token);
                    if (batch is null)
                        throw new InventoryException("A selected batch no longer exists.");

                    if (line.PhysicalQuantity < 0)
                        throw new InventoryException("Physical quantity cannot be negative.");

                    var difference = line.PhysicalQuantity - line.SystemQuantity;
                    batch.QuantityAvailable = line.PhysicalQuantity;
                    _uow.Repository<MedicineBatch>().Update(batch);

                    await _uow.Repository<StockAdjustmentItem>().AddAsync(new StockAdjustmentItem
                    {
                        StockAdjustmentId = adjustment.Id,
                        MedicineId = line.MedicineId,
                        MedicineBatchId = line.MedicineBatchId,
                        SystemQuantity = line.SystemQuantity,
                        PhysicalQuantity = line.PhysicalQuantity,
                        Remarks = line.Remarks
                    }, token);

                    var movementType = difference > 0
                        ? StockMovementType.AdjustmentIn
                        : StockMovementType.AdjustmentOut;

                    await _uow.Repository<StockMovement>().AddAsync(new StockMovement
                    {
                        BranchId = branchId,
                        MedicineId = line.MedicineId,
                        MedicineBatchId = line.MedicineBatchId,
                        MovementType = movementType,
                        Quantity = difference,
                        BalanceAfter = batch.QuantityAvailable,
                        UnitCost = batch.PurchasePrice,
                        ReferenceType = nameof(StockAdjustment),
                        ReferenceId = adjustment.Id,
                        ReferenceNumber = adjustment.AdjustmentNumber,
                        MovementDateUtc = _clock.UtcNow,
                        Remarks = request.Reason
                    }, token);
                }

                await _uow.SaveChangesAsync(token);

                return new StockAdjustmentReceiptDto
                {
                    AdjustmentId = adjustment.Id,
                    AdjustmentNumber = adjustment.AdjustmentNumber,
                    AdjustmentDate = adjustment.AdjustmentDate,
                    LinesAdjusted = lines.Count
                };
            }, ct);

            return Result.Success(receipt);
        }
        catch (InventoryException ex)
        {
            return Result.Failure<StockAdjustmentReceiptDto>(ex.Message);
        }
        catch (Exception ex)
        {
            return Result.Failure<StockAdjustmentReceiptDto>($"Could not save adjustment: {ex.Message}");
        }
    }

    private IQueryable<MedicineBatch> BatchQuery(int? branchId)
    {
        var q = _uow.Repository<MedicineBatch>().Query()
            .Include(b => b.Medicine)
            .Where(b => b.Medicine != null && b.Medicine.Status == EntityStatus.Active);
        if (branchId.HasValue) q = q.Where(b => b.BranchId == branchId);
        return q;
    }

    private async Task<int> CountLowStockMedicinesAsync(int? branchId, CancellationToken ct)
    {
        var reorderMeds = await _uow.Repository<Medicine>().Query()
            .Where(m => m.Status == EntityStatus.Active && m.ReorderLevel > 0)
            .Select(m => new { m.Id, m.ReorderLevel })
            .ToListAsync(ct);

        if (reorderMeds.Count == 0) return 0;

        var reorderIds = reorderMeds.Select(m => m.Id).ToList();
        var stockByMedicine = await BatchQuery(branchId)
            .Where(b => reorderIds.Contains(b.MedicineId))
            .GroupBy(b => b.MedicineId)
            .Select(g => new { MedicineId = g.Key, Qty = g.Sum(x => x.QuantityAvailable) })
            .ToListAsync(ct);

        var stockLookup = stockByMedicine.ToDictionary(x => x.MedicineId, x => x.Qty);
        return reorderMeds.Count(m =>
            (stockLookup.TryGetValue(m.Id, out var q) ? q : 0m) <= m.ReorderLevel);
    }

    private async Task<IQueryable<MedicineBatch>> FilterLowStockBatchesAsync(
        IQueryable<MedicineBatch> batches,
        int? branchId,
        CancellationToken ct)
    {
        var reorderMeds = await _uow.Repository<Medicine>().Query()
            .Where(m => m.Status == EntityStatus.Active && m.ReorderLevel > 0)
            .Select(m => new { m.Id, m.ReorderLevel })
            .ToListAsync(ct);

        if (reorderMeds.Count == 0)
            return batches.Where(_ => false);

        var reorderIds = reorderMeds.Select(m => m.Id).ToList();
        var stockByMedicine = await BatchQuery(branchId)
            .Where(b => reorderIds.Contains(b.MedicineId))
            .GroupBy(b => b.MedicineId)
            .Select(g => new { MedicineId = g.Key, Qty = g.Sum(x => x.QuantityAvailable) })
            .ToListAsync(ct);

        var stockLookup = stockByMedicine.ToDictionary(x => x.MedicineId, x => x.Qty);
        var lowStockIds = reorderMeds
            .Where(m => (stockLookup.TryGetValue(m.Id, out var q) ? q : 0m) <= m.ReorderLevel)
            .Select(m => m.Id)
            .ToList();

        return batches.Where(b => lowStockIds.Contains(b.MedicineId));
    }

    private async Task<string> GenerateAdjustmentNumberAsync(int? branchId, CancellationToken ct)
    {
        var today = _clock.Today;
        var tomorrow = today.AddDays(1);
        var q = _uow.Repository<StockAdjustment>().Query()
            .Where(a => a.AdjustmentDate >= today && a.AdjustmentDate < tomorrow);
        if (branchId.HasValue) q = q.Where(a => a.BranchId == branchId);

        var todayCount = await q.CountAsync(ct);
        return $"ADJ-{today:yyyyMMdd}-{todayCount + 1:D4}";
    }

    private sealed class InventoryException : Exception
    {
        public InventoryException(string message) : base(message) { }
    }
}
