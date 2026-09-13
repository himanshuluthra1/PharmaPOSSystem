using PharmaPOS.Domain.Enums;
using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.ExpiryReturns;

public interface IExpiryReturnService
{
    Task<List<ExpiryEligibleBatchDto>> ListEligibleBatchesAsync(
        int horizonDays,
        int? supplierId,
        int? branchId,
        CancellationToken ct = default);

    Task<List<ExpirySupplierOptionDto>> ListSuppliersAsync(CancellationToken ct = default);

    Task<Result<ExpiryClaimReceiptDto>> SubmitClaimAsync(
        SubmitExpiryClaimRequest request, int? branchId, string? userName, CancellationToken ct = default);

    Task<List<ExpiryClaimListRowDto>> ListClaimsAsync(
        bool awaitingCreditNoteOnly, int? branchId, int take = 100, CancellationToken ct = default);

    Task<Result<ExpiryClaimDetailDto>> GetClaimAsync(int claimId, int? branchId, CancellationToken ct = default);

    Task<List<ExpirySupplierBillOptionDto>> ListSupplierBillsAsync(
        int supplierId, int? branchId, CancellationToken ct = default);

    Task<Result<ExpiryClaimDetailDto>> UpdateClaimLinesAsync(
        UpdateExpiryClaimLinesRequest request, string? userName, CancellationToken ct = default);

    Task<Result> AttachCreditNoteAsync(
        AttachExpiryCreditNoteRequest request, string? userName, CancellationToken ct = default);
}

public record ExpiryEligibleBatchDto(
    int MedicineBatchId,
    int MedicineId,
    string MedicineName,
    string BatchNumber,
    DateTime ExpiryDate,
    decimal StockQuantity,
    decimal PurchasePrice,
    decimal GstPercent,
    decimal StockValue,
    string ExpiryStatus,
    int? SupplierId,
    string? SupplierName,
    int? PurchaseId,
    string? PurchaseInvoiceNumber);

public class SubmitExpiryClaimRequest
{
    public int SupplierId { get; set; }
    public string? Remarks { get; set; }
    public List<SubmitExpiryClaimLineRequest> Lines { get; set; } = [];
}

public class SubmitExpiryClaimLineRequest
{
    public int MedicineBatchId { get; set; }
    public decimal ClaimQuantity { get; set; }
    public int? SupplierId { get; set; }
}

public record ExpirySupplierOptionDto(int Id, string Name);

public record ExpirySupplierBillOptionDto(
    int PurchaseId,
    string InvoiceNumber,
    string? SupplierBillNumber,
    DateTime InvoiceDate,
    decimal GrandTotal)
{
    public string Label =>
        string.IsNullOrWhiteSpace(SupplierBillNumber)
            ? $"{InvoiceNumber} · {InvoiceDate:dd-MMM-yyyy} · {GrandTotal:N2}"
            : $"{InvoiceNumber} / {SupplierBillNumber} · {InvoiceDate:dd-MMM-yyyy} · {GrandTotal:N2}";
}

public class ExpiryClaimReceiptDto
{
    public int ClaimId { get; set; }
    public string ClaimNumber { get; set; } = string.Empty;
    public string ReturnNumber { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public decimal ExpectedCreditAmount { get; set; }
    public int LineCount { get; set; }
}

public record ExpiryClaimListRowDto(
    int Id,
    string ClaimNumber,
    DateTime ClaimDate,
    string SupplierName,
    decimal ExpectedCreditAmount,
    string Status,
    string? SettlementReference,
    DateTime? CreditNoteDate,
    string ReturnNumber,
    int? SettledAgainstPurchaseId = null);

public class ExpiryClaimDetailDto
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public string ClaimNumber { get; set; } = string.Empty;
    public DateTime ClaimDate { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string ReturnNumber { get; set; } = string.Empty;
    public decimal ExpectedCreditAmount { get; set; }
    public ExpiryClaimStatus Status { get; set; }
    public bool CanEditLines { get; set; }
    public ExpiryCreditSettlementKind CreditSettlementKind { get; set; }
    public string? CreditNoteNumber { get; set; }
    public DateTime? CreditNoteDate { get; set; }
    public decimal? CreditNoteAmount { get; set; }
    public int? SettledAgainstPurchaseId { get; set; }
    public string? SettledAgainstPurchaseLabel { get; set; }
    public string? Remarks { get; set; }
    public List<ExpiryClaimDetailLineDto> Lines { get; set; } = [];
}

public class ExpiryClaimDetailLineDto
{
    public int Id { get; set; }
    public string MedicineName { get; set; } = string.Empty;
    public string BatchNumber { get; set; } = string.Empty;
    public DateTime? ExpiryDate { get; set; }
    public decimal StockQuantity { get; set; }
    public decimal ClaimQuantity { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal GstPercent { get; set; }
    public decimal RefundPercent { get; set; } = 100m;
    public decimal LineTotal { get; set; }
    public string? PurchaseInvoiceNumber { get; set; }
    public int? PurchaseId { get; set; }
}

public class UpdateExpiryClaimLinesRequest
{
    public int ClaimId { get; set; }
    public List<UpdateExpiryClaimLineRequest> Lines { get; set; } = [];
}

public class UpdateExpiryClaimLineRequest
{
    public int Id { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public DateTime? ExpiryDate { get; set; }
    public decimal ClaimQuantity { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal GstPercent { get; set; }
    public decimal RefundPercent { get; set; } = 100m;
}

public class AttachExpiryCreditNoteRequest
{
    public int ClaimId { get; set; }
    public ExpiryCreditSettlementKind SettlementKind { get; set; } = ExpiryCreditSettlementKind.CreditNote;
    public string CreditNoteNumber { get; set; } = string.Empty;
    public int? SettledAgainstPurchaseId { get; set; }
    public DateTime? CreditNoteDate { get; set; }
    public decimal? CreditNoteAmount { get; set; }
}
