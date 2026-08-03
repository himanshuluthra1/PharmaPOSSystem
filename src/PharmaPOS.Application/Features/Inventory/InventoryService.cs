using Microsoft.EntityFrameworkCore;
using PharmaPOS.Application.Common;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Domain.Entities.Inventory;
using PharmaPOS.Domain.Entities.Masters;
using PharmaPOS.Domain.Entities.Purchases;
using PharmaPOS.Domain.Entities.Sales;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Application.Features.Settings;
using PharmaPOS.Application.Features.ReportingSync;
using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.Inventory;

public class InventoryService : IInventoryService
{
    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _clock;
    private readonly ISettingsService _settings;
    private readonly IReportingSyncService _reportingSync;

    public InventoryService(
        IUnitOfWork uow,
        IDateTimeProvider clock,
        ISettingsService settings,
        IReportingSyncService reportingSync)
    {
        _uow = uow;
        _clock = clock;
        _settings = settings;
        _reportingSync = reportingSync;
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

        if (!string.IsNullOrWhiteSpace(term))
        {
            var tokens = SearchQueryExtensions.GetSearchTokens(term);
            var normalized = SearchQueryExtensions.NormalizeTerm(term);

            if (tokens.Length > 1)
            {
                foreach (var raw in tokens)
                {
                    var token = SearchQueryExtensions.NormalizeTerm(raw);
                    if (token.Length == 0) continue;
                    batches = batches.Where(b =>
                        b.BatchNumber.Replace(" ", "").Contains(token) ||
                        (b.Medicine != null && (
                            b.Medicine.NameSearchKey.Contains(token) ||
                            (b.Medicine.GenericNameSearchKey != "" && b.Medicine.GenericNameSearchKey.Contains(token)) ||
                            (b.Medicine.BarcodeSearchKey != "" && b.Medicine.BarcodeSearchKey.Contains(token)))));
                }
            }
            else
            {
                batches = batches.Where(b =>
                    b.BatchNumber.Replace(" ", "").Contains(normalized) ||
                    (b.Medicine != null && (
                        b.Medicine.NameSearchKey.Contains(normalized) ||
                        (b.Medicine.GenericNameSearchKey != "" && b.Medicine.GenericNameSearchKey.Contains(normalized)) ||
                        (b.Medicine.BarcodeSearchKey != "" && b.Medicine.BarcodeSearchKey.Contains(normalized)))));
            }
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

        // Unfiltered on-hand lists can exceed thousands of rows; a hard 1000-row cap
        // alphabetically hid medicines like "DETTOL LIQ". Prefer a higher cap when
        // the user has typed a search term (result set is already narrowed).
        var take = string.IsNullOrWhiteSpace(term) ? 2500 : 1000;

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
            .Take(take)
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
        // Treat 0 as "no filter" — cart/purchase rows often expose BatchId = 0.
        if (batchId is <= 0) batchId = null;
        if (medicineId is <= 0) medicineId = null;

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

        var rows = await q
            .OrderByDescending(m => m.MovementDateUtc)
            .ThenByDescending(m => m.Id)
            .Take(Math.Max(take, 2000))
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

        // MedWin import wrote Sales/Purchases but not StockMovements. For a medicine
        // ledger (Ctrl+L), merge document history so purchase/sale lines appear.
        if (medicineId.HasValue && string.IsNullOrWhiteSpace(term) && batchId is null)
        {
            rows = await MergeDocumentHistoryAsync(rows, medicineId.Value, branchId, take, ct);
        }
        else if (rows.Count == 0 && medicineId.HasValue)
        {
            rows = await BuildBatchStockFallbackAsync(medicineId.Value, batchId, branchId, take, ct);
        }
        else if (rows.Count > take)
        {
            rows = rows.Take(take).ToList();
        }

        return rows;
    }

    private async Task<List<StockLedgerRowDto>> MergeDocumentHistoryAsync(
        List<StockLedgerRowDto> movementRows,
        int medicineId,
        int? branchId,
        int take,
        CancellationToken ct)
    {
        var docRows = await BuildDocumentLedgerRowsAsync(medicineId, branchId, ct);

        // Prefer real stock movements; skip document rows already covered by a movement reference.
        var coveredRefs = movementRows
            .Where(r => !string.IsNullOrWhiteSpace(r.ReferenceNumber))
            .Select(r => r.ReferenceNumber!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Ignore placeholder current-stock snapshot rows when we have purchase/sale history.
        var coreMovements = movementRows
            .Where(r => !(r.MovementType == StockMovementType.OpeningStock
                          && (string.Equals(r.ReferenceNumber, "OPENING", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(r.ReferenceNumber, "STOCK", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(r.Remarks, "MedWin opening stock", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(r.Remarks, "MedWin current stock snapshot", StringComparison.OrdinalIgnoreCase)
                              || (r.Remarks != null && r.Remarks.Contains("no movement history", StringComparison.OrdinalIgnoreCase)))))
            .ToList();

        var extraDocs = docRows
            .Where(d => string.IsNullOrWhiteSpace(d.ReferenceNumber)
                        || !coveredRefs.Contains(d.ReferenceNumber))
            .ToList();

        var merged = coreMovements.Concat(extraDocs).ToList();
        if (merged.Count == 0)
            return await BuildBatchStockFallbackAsync(medicineId, null, branchId, take, ct);

        return await ApplyRunningBalancesAsync(merged, medicineId, branchId, take, ct);
    }

    private async Task<List<StockLedgerRowDto>> BuildDocumentLedgerRowsAsync(
        int medicineId, int? branchId, CancellationToken ct)
    {
        var medicine = await _uow.Repository<Medicine>().Query().AsNoTracking()
            .Where(m => m.Id == medicineId)
            .Select(m => new { m.Name })
            .FirstOrDefaultAsync(ct);
        var medicineName = medicine?.Name ?? $"Medicine #{medicineId}";

        var fyStartLocal = GetIndianFinancialYearStart(_clock.Today);

        var purchaseQuery = _uow.Repository<PurchaseItem>().Query().AsNoTracking()
            .Where(i => i.MedicineId == medicineId
                        && i.Purchase != null
                        && i.Purchase.Status == PurchaseStatus.Received
                        && i.Purchase.InvoiceDate >= fyStartLocal);
        if (branchId.HasValue)
            purchaseQuery = purchaseQuery.Where(i => i.Purchase!.BranchId == branchId);

        var purchaseRows = await purchaseQuery
            .Select(i => new
            {
                i.Id,
                i.Purchase!.InvoiceDate,
                i.Purchase.InvoiceNumber,
                i.BatchNumber,
                Qty = i.Quantity + i.FreeQuantity,
                i.PurchasePrice
            })
            .ToListAsync(ct);

        var saleQuery = _uow.Repository<SaleItem>().Query().AsNoTracking()
            .Where(i => i.MedicineId == medicineId
                        && i.Sale != null
                        && i.Sale.Status != SaleStatus.Cancelled
                        && i.Quantity != 0
                        && i.Sale.InvoiceDate >= fyStartLocal);
        if (branchId.HasValue)
            saleQuery = saleQuery.Where(i => i.Sale!.BranchId == branchId);

        var saleRows = await saleQuery
            .Select(i => new
            {
                i.Id,
                i.Sale!.InvoiceDate,
                i.Sale.InvoiceNumber,
                i.BatchNumber,
                i.Quantity,
                i.UnitPrice
            })
            .ToListAsync(ct);

        var returnQuery = _uow.Repository<PurchaseReturnItem>().Query().AsNoTracking()
            .Where(i => i.MedicineId == medicineId
                        && i.PurchaseReturn != null
                        && i.PurchaseReturn.Status == PurchaseReturnStatus.Completed
                        && i.PurchaseReturn.ReturnDate >= fyStartLocal);
        if (branchId.HasValue)
            returnQuery = returnQuery.Where(i => i.PurchaseReturn!.BranchId == branchId);

        var returnRows = await returnQuery
            .Select(i => new
            {
                i.Id,
                i.PurchaseReturn!.ReturnDate,
                i.PurchaseReturn.ReturnNumber,
                i.BatchNumber,
                Qty = i.ReturnedQuantity + i.ReturnedFreeQuantity,
                i.PurchasePrice
            })
            .ToListAsync(ct);

        var rows = new List<StockLedgerRowDto>(purchaseRows.Count + saleRows.Count + returnRows.Count);

        foreach (var p in purchaseRows.Where(x => x.Qty != 0))
        {
            rows.Add(new StockLedgerRowDto(
                p.Id,
                ToUtc(p.InvoiceDate),
                StockMovementType.PurchaseIn,
                medicineName,
                p.BatchNumber,
                Math.Abs(p.Qty),
                0m,
                p.PurchasePrice,
                p.InvoiceNumber,
                null));
        }

        foreach (var s in saleRows)
        {
            var isReturn = s.Quantity < 0;
            // Quantities are stored in the same units as stock (MedWin import keeps loose units).
            rows.Add(new StockLedgerRowDto(
                s.Id,
                ToUtc(s.InvoiceDate),
                isReturn ? StockMovementType.SaleReturn : StockMovementType.SaleOut,
                medicineName,
                s.BatchNumber,
                Math.Abs(s.Quantity),
                0m,
                s.UnitPrice,
                s.InvoiceNumber,
                isReturn ? "Sale return (from bill)" : null));
        }

        foreach (var r in returnRows.Where(x => x.Qty != 0))
        {
            rows.Add(new StockLedgerRowDto(
                r.Id,
                ToUtc(r.ReturnDate),
                StockMovementType.PurchaseReturn,
                medicineName,
                r.BatchNumber,
                Math.Abs(r.Qty),
                0m,
                r.PurchasePrice,
                r.ReturnNumber,
                "Purchase return"));
        }

        return rows;
    }

    private async Task<List<StockLedgerRowDto>> ApplyRunningBalancesAsync(
        List<StockLedgerRowDto> rows,
        int medicineId,
        int? branchId,
        int take,
        CancellationToken ct)
    {
        var currentStockQuery = _uow.Repository<MedicineBatch>().Query().AsNoTracking()
            .Where(b => b.MedicineId == medicineId);
        if (branchId.HasValue)
            currentStockQuery = currentStockQuery.Where(b => b.BranchId == branchId);
        var currentStock = await currentStockQuery.SumAsync(b => (decimal?)b.QuantityAvailable, ct) ?? 0m;

        var fyStartLocal = GetIndianFinancialYearStart(_clock.Today);
        var fyStartUtc = ToUtc(fyStartLocal);

        var chronological = rows
            .OrderBy(r => r.MovementDateUtc)
            .ThenBy(r => r.MovementId)
            .ToList();

        var net = chronological.Sum(r => SignedLedgerQuantity(r.MovementType, r.Quantity));
        var opening = currentStock - net;
        var medicineName = chronological.FirstOrDefault()?.MedicineName ?? $"Medicine #{medicineId}";

        // Always show FY opening row (MedWin "FINANCIAL YEAR OPENING"), even when zero.
        chronological.Insert(0, new StockLedgerRowDto(
            0,
            fyStartUtc,
            opening >= 0 ? StockMovementType.OpeningStock : StockMovementType.AdjustmentOut,
            medicineName,
            null,
            Math.Abs(opening),
            0m,
            0m,
            "OPENING",
            opening >= 0
                ? $"Financial year opening ({fyStartLocal:dd/MM/yyyy})"
                : "Opening adjustment (history incomplete)"));

        decimal balance = 0m;
        var withBalance = new List<StockLedgerRowDto>(chronological.Count);
        foreach (var row in chronological)
        {
            balance += SignedLedgerQuantity(row.MovementType, row.Quantity);
            withBalance.Add(row with { BalanceAfter = balance });
        }

        return withBalance
            .OrderByDescending(r => r.MovementDateUtc)
            .ThenByDescending(r => r.MovementId)
            .Take(take)
            .ToList();
    }

    /// <summary>Indian FY starts 1 April.</summary>
    private static DateTime GetIndianFinancialYearStart(DateTime localDate)
    {
        var year = localDate.Month >= 4 ? localDate.Year : localDate.Year - 1;
        return new DateTime(year, 4, 1, 0, 0, 0, DateTimeKind.Local);
    }

    private static decimal SignedLedgerQuantity(StockMovementType type, decimal quantity)
    {
        var abs = Math.Abs(quantity);
        return IsInboundMovement(type) ? abs : -abs;
    }

    private static bool IsInboundMovement(StockMovementType type) => type switch
    {
        StockMovementType.PurchaseIn => true,
        StockMovementType.SaleReturn => true,
        StockMovementType.AdjustmentIn => true,
        StockMovementType.TransferIn => true,
        StockMovementType.OpeningStock => true,
        StockMovementType.NonSaleableIn => true,
        _ => false
    };

    private static DateTime ToUtc(DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
            : value.ToUniversalTime();

    private async Task<List<StockLedgerRowDto>> BuildBatchStockFallbackAsync(
        int medicineId,
        int? batchId,
        int? branchId,
        int take,
        CancellationToken ct)
    {
        var bq = _uow.Repository<MedicineBatch>().Query().AsNoTracking()
            .Where(b => b.MedicineId == medicineId);
        if (branchId.HasValue) bq = bq.Where(b => b.BranchId == branchId);
        if (batchId.HasValue) bq = bq.Where(b => b.Id == batchId.Value);

        return await bq
            .OrderByDescending(b => b.CreatedAtUtc)
            .ThenBy(b => b.BatchNumber)
            .Take(take)
            .Select(b => new StockLedgerRowDto(
                b.Id,
                b.CreatedAtUtc,
                StockMovementType.OpeningStock,
                b.Medicine != null ? b.Medicine.Name : $"Medicine #{medicineId}",
                b.BatchNumber,
                b.QuantityAvailable,
                b.QuantityAvailable,
                b.PurchasePrice,
                "STOCK",
                "Current batch stock (no movement history yet)"))
            .ToListAsync(ct);
    }

    public Task<string> PreviewNextAdjustmentNumberAsync(int? branchId, CancellationToken ct = default)
        => GenerateAdjustmentNumberAsync(branchId, ct);

    public async Task<List<AdjustmentBatchDto>> GetBatchesForAdjustmentAsync(
        int medicineId, int? branchId, CancellationToken ct = default)
    {
        var q = _uow.Repository<MedicineBatch>().Query()
            .Where(b => b.MedicineId == medicineId);
        if (branchId.HasValue) q = q.Where(b => b.BranchId == branchId);

        var batches = await q
            .OrderBy(b => b.ExpiryDate)
            .ThenBy(b => b.BatchNumber)
            .Select(b => new AdjustmentBatchDto(
                b.Id, b.BatchNumber, b.ExpiryDate, b.QuantityAvailable, b.Mrp))
            .ToListAsync(ct);

        if (batches.Count > 0) return batches;

        var medicine = await _uow.Repository<Medicine>().GetByIdAsync(medicineId, ct);
        if (medicine is null) return [];

        var existingOpening = await _uow.Repository<MedicineBatch>().Query()
            .FirstOrDefaultAsync(b => b.MedicineId == medicineId &&
                                      b.BranchId == branchId &&
                                      b.BatchNumber == "OPENING", ct);
        if (existingOpening is not null)
        {
            return
            [
                new AdjustmentBatchDto(
                    existingOpening.Id, existingOpening.BatchNumber, existingOpening.ExpiryDate,
                    existingOpening.QuantityAvailable, existingOpening.Mrp)
            ];
        }

        var batch = new MedicineBatch
        {
            MedicineId = medicineId,
            BranchId = branchId,
            BatchNumber = "OPENING",
            ExpiryDate = _clock.Today.AddYears(2),
            QuantityAvailable = 0,
            PurchasePrice = medicine.PurchasePrice,
            Mrp = medicine.Mrp,
            SellingPrice = medicine.SellingPrice > 0 ? medicine.SellingPrice : medicine.Mrp,
            GstPercent = medicine.GstPercent
        };
        await _uow.Repository<MedicineBatch>().AddAsync(batch, ct);
        await _uow.SaveChangesAsync(ct);
        await _reportingSync.EnqueueMedicineBatchAsync(batch.Id, ct);

        return
        [
            new AdjustmentBatchDto(batch.Id, batch.BatchNumber, batch.ExpiryDate, batch.QuantityAvailable, batch.Mrp)
        ];
    }

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

            var movementIds = await _uow.Repository<StockMovement>().Query().AsNoTracking()
                .Where(m => m.ReferenceType == nameof(StockAdjustment) && m.ReferenceId == receipt.AdjustmentId)
                .Select(m => m.Id)
                .ToListAsync(ct);
            foreach (var movementId in movementIds)
                await _reportingSync.EnqueueStockMovementAsync(movementId, ct);

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
