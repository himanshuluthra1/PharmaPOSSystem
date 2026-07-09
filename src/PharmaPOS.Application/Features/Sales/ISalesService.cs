using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.Sales;

/// <summary>Fast-billing operations: lookups and invoice creation.</summary>
public interface ISalesService
{
    Task<List<MedicineLookupDto>> SearchMedicinesAsync(string term, int? branchId, CancellationToken ct = default);
    Task<List<BatchLookupDto>> GetBatchesAsync(int medicineId, int? branchId, CancellationToken ct = default);
    Task<List<CustomerLookupDto>> SearchCustomersAsync(string term, CancellationToken ct = default);
    Task<List<DoctorLookupDto>> SearchDoctorsAsync(string term, CancellationToken ct = default);

    /// <summary>
    /// Creates a completed sale atomically: validates stock, computes GST-inclusive
    /// totals, allocates the chosen batches, writes stock-ledger movements, updates
    /// customer balance/reward points and returns a printable receipt.
    /// </summary>
    Task<Result<SaleReceiptDto>> CreateSaleAsync(CreateSaleRequest request, int? branchId, CancellationToken ct = default);

    Task<Result<SaleReceiptDto>> UpdateSaleAsync(UpdateSaleRequest request, int? branchId, CancellationToken ct = default);

    /// <summary>Preview of the next invoice number for a new bill (not reserved).</summary>
    Task<string> PreviewNextInvoiceNumberAsync(int? branchId, CancellationToken ct = default);

    /// <summary>All completed bills for the branch, newest first.</summary>
    Task<List<SaleListItemDto>> ListBillsAsync(int? branchId, CancellationToken ct = default);

    /// <summary>Distinct patient names for autosuggest while searching bills.</summary>
    Task<List<string>> SuggestPatientNamesAsync(string term, int? branchId, CancellationToken ct = default);

    /// <summary>Search completed bills by patient name, mobile, or medicine name.</summary>
    Task<List<BillSearchResultDto>> SearchBillsAsync(BillSearchType type, string term, int? branchId, CancellationToken ct = default);

    /// <summary>Load a saved bill into the billing screen for editing.</summary>
    Task<Result<SaleEditDto>> GetSaleForEditAsync(int saleId, int? branchId, CancellationToken ct = default);
}
