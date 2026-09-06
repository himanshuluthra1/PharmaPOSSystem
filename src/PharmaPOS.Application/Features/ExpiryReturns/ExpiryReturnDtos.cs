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
    string? CreditNoteNumber,
    DateTime? CreditNoteDate,
    string ReturnNumber);

public class ExpiryClaimDetailDto
{
    public int Id { get; set; }
    public string ClaimNumber { get; set; } = string.Empty;
    public DateTime ClaimDate { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string ReturnNumber { get; set; } = string.Empty;
    public decimal ExpectedCreditAmount { get; set; }
    public ExpiryClaimStatus Status { get; set; }
    public string? CreditNoteNumber { get; set; }
    public DateTime? CreditNoteDate { get; set; }
    public decimal? CreditNoteAmount { get; set; }
    public string? Remarks { get; set; }
    public List<ExpiryClaimDetailLineDto> Lines { get; set; } = [];
}

public record ExpiryClaimDetailLineDto(
    string MedicineName,
    string BatchNumber,
    DateTime? ExpiryDate,
    decimal StockQuantity,
    decimal ClaimQuantity,
    decimal PurchasePrice,
    decimal LineTotal,
    string? PurchaseInvoiceNumber);

public class AttachExpiryCreditNoteRequest
{
    public int ClaimId { get; set; }
    public string CreditNoteNumber { get; set; } = string.Empty;
    public DateTime? CreditNoteDate { get; set; }
    public decimal? CreditNoteAmount { get; set; }
}
