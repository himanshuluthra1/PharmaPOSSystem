using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.Purchases;

public interface IPurchaseOrderService
{
    Task<List<PurchaseOrderListItemDto>> ListAsync(int? branchId, CancellationToken ct = default);
    Task<Result<PurchaseOrderDetailDto>> GetAsync(int purchaseOrderId, int? branchId, CancellationToken ct = default);
    Task<Result<PurchaseOrderDetailDto>> SaveDraftAsync(SavePurchaseOrderRequest request, int? branchId, CancellationToken ct = default);
    Task<Result<PurchaseOrderDetailDto>> ConfirmAsync(int purchaseOrderId, int? branchId, CancellationToken ct = default);
    Task<Result> CancelAsync(int purchaseOrderId, int? branchId, CancellationToken ct = default);

    /// <summary>
    /// Creates one Draft PO per last-purchase supplier for medicines at/below reorder level.
    /// Medicines with no prior supplier are skipped.
    /// </summary>
    Task<Result<SuggestReorderResultDto>> GenerateFromLowStockAsync(int? branchId, CancellationToken ct = default);

    /// <summary>Build a receive draft for Purchase GRN (remaining qty only).</summary>
    Task<Result<PurchaseOrderReceiveDraftDto>> GetReceiveDraftAsync(int purchaseOrderId, int? branchId, CancellationToken ct = default);

    /// <summary>Apply received quantities after a GRN is saved against this PO.</summary>
    Task ApplyReceiptAsync(int purchaseOrderId, IReadOnlyDictionary<int, decimal> receivedByMedicineId, CancellationToken ct = default);
}
