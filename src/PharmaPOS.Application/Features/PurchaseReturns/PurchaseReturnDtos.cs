using PharmaPOS.Domain.Enums;
using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.PurchaseReturns;

public interface IPurchaseReturnService
{
    Task<List<PurchaseReturnSearchResultDto>> SearchPurchasesAsync(
        string term, int? branchId, CancellationToken ct = default);

    Task<Result<PurchaseForReturnDto>> GetPurchaseForReturnAsync(
        int purchaseId, int? branchId, CancellationToken ct = default);

    Task<Result<PurchaseReturnReceiptDto>> CreateReturnAsync(
        CreatePurchaseReturnRequest request, int? branchId, string? userName, CancellationToken ct = default);

    /// <summary>Return stock to a supplier without linking to a purchase bill.</summary>
    Task<Result<PurchaseReturnReceiptDto>> CreateDirectReturnAsync(
        CreateDirectPurchaseReturnRequest request, int? branchId, string? userName, CancellationToken ct = default);

    Task<Result<DirectReturnBatchDto>> GetBatchForDirectReturnAsync(
        int medicineBatchId, int? branchId, CancellationToken ct = default);

    Task<List<PurchaseReturnListRowDto>> ListReturnsAsync(
        bool pendingSupplierReceiptOnly, int? branchId, int take = 100, CancellationToken ct = default);

    Task<Result<PurchaseReturnDetailDto>> GetReturnDetailsAsync(
        int purchaseReturnId, int? branchId, CancellationToken ct = default);

    Task<Result> AttachSupplierReceiptAsync(
        int purchaseReturnId, string receiptNumber, DateTime? receiptDate, string? userName, CancellationToken ct = default);

    Task<List<ReturnReasonOptionDto>> ListReturnReasonsAsync(CancellationToken ct = default);
}

public record PurchaseReturnSearchResultDto(
    int PurchaseId,
    string InvoiceNumber,
    string? SupplierInvoiceNumber,
    DateTime InvoiceDate,
    string SupplierName,
    decimal GrandTotal,
    PurchaseStatus Status);

public class PurchaseForReturnDto
{
    public int PurchaseId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string? SupplierInvoiceNumber { get; set; }
    public DateTime InvoiceDate { get; set; }
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public decimal GrandTotal { get; set; }
    public decimal PaidAmount { get; set; }
    public PurchaseStatus Status { get; set; }
    public List<PurchaseReturnLineDto> Lines { get; set; } = new();
}

public class PurchaseReturnLineDto
{
    public int PurchaseItemId { get; set; }
    public int MedicineId { get; set; }
    public string MedicineName { get; set; } = string.Empty;
    public int? MedicineBatchId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public DateTime? ExpiryDate { get; set; }
    public decimal Quantity { get; set; }
    public decimal FreeQuantity { get; set; }
    public decimal AlreadyReturnedQty { get; set; }
    public decimal AlreadyReturnedFreeQty { get; set; }
    public decimal AvailableQty => Math.Max(0, Quantity - AlreadyReturnedQty);
    public decimal AvailableFreeQty => Math.Max(0, FreeQuantity - AlreadyReturnedFreeQty);
    public decimal PurchasePrice { get; set; }
    public decimal GstPercent { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal LineTotal { get; set; }
}

public class CreatePurchaseReturnRequest
{
    public int PurchaseId { get; set; }
    public PurchaseReturnSettlementMode SettlementMode { get; set; } = PurchaseReturnSettlementMode.SupplierCredit;
    public string? Remarks { get; set; }
    public List<CreatePurchaseReturnLineRequest> Lines { get; set; } = new();
}

public class CreatePurchaseReturnLineRequest
{
    public int PurchaseItemId { get; set; }
    public decimal ReturnQuantity { get; set; }
    public decimal ReturnFreeQuantity { get; set; }
    public int? ReturnReasonId { get; set; }
    public string? ReasonRemarks { get; set; }
}

public class CreateDirectPurchaseReturnRequest
{
    public int SupplierId { get; set; }
    public PurchaseReturnSettlementMode SettlementMode { get; set; } = PurchaseReturnSettlementMode.SupplierCredit;
    public string? Remarks { get; set; }
    public List<CreateDirectPurchaseReturnLineRequest> Lines { get; set; } = new();
}

public class CreateDirectPurchaseReturnLineRequest
{
    public int MedicineBatchId { get; set; }
    public decimal ReturnQuantity { get; set; }
    public decimal ReturnFreeQuantity { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal GstPercent { get; set; }
    public int? ReturnReasonId { get; set; }
    public string? ReasonRemarks { get; set; }
}

public class DirectReturnBatchDto
{
    public int MedicineBatchId { get; set; }
    public int MedicineId { get; set; }
    public string MedicineName { get; set; } = string.Empty;
    public string BatchNumber { get; set; } = string.Empty;
    public DateTime? ExpiryDate { get; set; }
    public decimal QuantityAvailable { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal GstPercent { get; set; }
}

public class PurchaseReturnReceiptDto
{
    public int PurchaseReturnId { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public string PurchaseInvoiceNumber { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public DateTime ReturnDate { get; set; }
    public decimal GrandTotal { get; set; }
    public bool IsFullReturn { get; set; }
    public bool IsDirectReturn { get; set; }
    public string? SupplierReturnReceiptNumber { get; set; }
}

public record PurchaseReturnListRowDto(
    int Id,
    string ReturnNumber,
    DateTime ReturnDate,
    string PurchaseInvoiceNumber,
    string? SupplierInvoiceNumber,
    string SupplierName,
    decimal GrandTotal,
    string? SupplierReturnReceiptNumber,
    DateTime? SupplierReturnReceiptDate,
    bool HasSupplierReceipt,
    bool IsDirectReturn);

public class PurchaseReturnDetailDto
{
    public int Id { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public DateTime ReturnDate { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string PurchaseInvoiceNumber { get; set; } = string.Empty;
    public bool IsDirectReturn { get; set; }
    public string? Remarks { get; set; }
    public decimal GrandTotal { get; set; }
    public List<PurchaseReturnDetailLineDto> Lines { get; set; } = new();
}

public record PurchaseReturnDetailLineDto(
    string MedicineName,
    string? BatchNumber,
    DateTime? ExpiryDate,
    decimal ReturnedQuantity,
    decimal ReturnedFreeQuantity,
    decimal PurchasePrice,
    decimal GstPercent,
    decimal LineTotal,
    string? ReasonName);

public record ReturnReasonOptionDto(int Id, string Code, string Name, bool RequiresRemarks);
