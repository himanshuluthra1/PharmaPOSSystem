using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.Inventory;

public interface IInventoryService
{
    Task<StockSummaryDto> GetStockSummaryAsync(int? branchId, CancellationToken ct = default);

    Task<List<StockBatchRowDto>> SearchStockBatchesAsync(
        string term,
        StockFilterKind filter,
        int? branchId,
        CancellationToken ct = default);

    Task<List<StockLedgerRowDto>> GetStockLedgerAsync(
        string? term,
        int? medicineId,
        int? batchId,
        int? branchId,
        int take = 500,
        CancellationToken ct = default);

    Task<string> PreviewNextAdjustmentNumberAsync(int? branchId, CancellationToken ct = default);

    /// <summary>
    /// Batches for stock adjustment (includes zero qty). Does not invent a large
    /// opening quantity — creates OPENING@0 only when the medicine has no batch yet.
    /// </summary>
    Task<List<AdjustmentBatchDto>> GetBatchesForAdjustmentAsync(
        int medicineId, int? branchId, CancellationToken ct = default);

    Task<Result<StockAdjustmentReceiptDto>> CreateStockAdjustmentAsync(
        CreateStockAdjustmentRequest request,
        int? branchId,
        CancellationToken ct = default);
}
