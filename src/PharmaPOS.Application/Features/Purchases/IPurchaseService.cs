using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.Purchases;

/// <summary>Goods-receipt operations: lookups and purchase (GRN) creation.</summary>
public interface IPurchaseService
{
    Task<List<PurchaseMedicineDto>> SearchMedicinesAsync(string term, CancellationToken ct = default);
    Task<List<SupplierLookupDto>> SearchSuppliersAsync(string term, CancellationToken ct = default);

    /// <summary>
    /// Records a purchase and receives it atomically: creates/updates batches with
    /// their expiry &amp; costing, increments stock, writes stock-ledger movements,
    /// refreshes the medicine master pricing, updates the supplier payable and
    /// returns a summary for confirmation.
    /// </summary>
    Task<Result<PurchaseReceiptDto>> CreatePurchaseAsync(CreatePurchaseRequest request, int? branchId, CancellationToken ct = default);
}
