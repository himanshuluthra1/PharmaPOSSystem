using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.Purchases;

/// <summary>Goods-receipt operations: lookups and purchase (GRN) creation.</summary>
public interface IPurchaseService
{
    Task<List<PurchaseMedicineDto>> SearchMedicinesAsync(string term, CancellationToken ct = default);
    Task<List<SupplierLookupDto>> SearchSuppliersAsync(string term, CancellationToken ct = default);

    Task<PurchaseMedicineDto?> GetMedicineAsync(int medicineId, CancellationToken ct = default);

    /// <summary>Preview of the next purchase number for a new GRN (not reserved).</summary>
    Task<string> PreviewNextPurchaseNumberAsync(int? branchId, CancellationToken ct = default);

    /// <summary>All received purchases for the branch, newest first.</summary>
    Task<List<PurchaseListItemDto>> ListPurchasesAsync(int? branchId, CancellationToken ct = default);

    /// <summary>Load a saved purchase into the goods-receipt screen.</summary>
    Task<Result<PurchaseLoadDto>> GetPurchaseForLoadAsync(int purchaseId, int? branchId, CancellationToken ct = default);

    /// <summary>List received purchases, optionally filtered by supplier (null = all).</summary>
    Task<List<PurchaseSupplierBillDto>> ListPurchasesBySupplierAsync(int? supplierId, int? branchId, CancellationToken ct = default);

    /// <summary>
    /// Records a purchase and receives it atomically: creates/updates batches with
    /// their expiry &amp; costing, increments stock, writes stock-ledger movements,
    /// refreshes the medicine master pricing, updates the supplier payable and
    /// returns a summary for confirmation.
    /// </summary>
    Task<Result<PurchaseReceiptDto>> CreatePurchaseAsync(CreatePurchaseRequest request, int? branchId, CancellationToken ct = default);

    /// <summary>Update a received purchase: reverses prior stock/payable, reapplies lines.</summary>
    Task<Result<PurchaseReceiptDto>> UpdatePurchaseAsync(UpdatePurchaseRequest request, int? branchId, CancellationToken ct = default);
}
