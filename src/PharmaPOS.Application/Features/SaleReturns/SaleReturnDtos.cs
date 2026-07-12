using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Application.Features.SaleReturns;

public enum SaleReturnSearchType
{
    InvoiceNumber = 0,
    CustomerMobile = 1,
    CustomerName = 2,
    Barcode = 3,
    QrCode = 4
}

public record SaleReturnSearchResultDto(
    int SaleId,
    string InvoiceNumber,
    DateTime InvoiceDate,
    string CustomerLabel,
    string? CustomerPhone,
    decimal GrandTotal,
    string CashierName,
    SaleStatus Status)
{
    public string InvoiceDateLabel => InvoiceDate.ToString("dd/MM/yyyy hh:mm tt");
    public string StatusLabel => Status switch
    {
        SaleStatus.Completed => "Completed",
        SaleStatus.PartiallyReturned => "Partially Returned",
        SaleStatus.Returned => "Fully Returned",
        _ => Status.ToString()
    };
}

public class SaleReturnPolicyDto
{
    public int AllowedDays { get; set; } = 30;
    public decimal HighValueThreshold { get; set; } = 5000m;
    public bool BlockExpired { get; set; } = true;
    public bool BlockScheduleDrugs { get; set; }
    public bool BlockRefrigerated { get; set; }
    public bool RefundOriginalPaymentMode { get; set; } = true;
    public int CreditNoteValidityDays { get; set; } = 90;
}

public record ReturnReasonDto(int Id, string Code, string Name, bool RequiresRemarks);

public class SaleForReturnDto
{
    public int SaleId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public int? CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal PaidAmount { get; set; }
    public SaleStatus Status { get; set; }
    public int RewardPointsEarned { get; set; }
    public int RewardPointsRedeemed { get; set; }
    public List<SaleReturnLineDto> Lines { get; set; } = new();
    public List<SalePaymentSnapshotDto> OriginalPayments { get; set; } = new();
}

public class SaleReturnLineDto
{
    public int SaleItemId { get; set; }
    public int MedicineId { get; set; }
    public string MedicineName { get; set; } = string.Empty;
    public string? GenericName { get; set; }
    public string? Barcode { get; set; }
    public int? MedicineBatchId { get; set; }
    public string? BatchNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public decimal Mrp { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal GstPercent { get; set; }
    public decimal SoldQuantity { get; set; }
    public decimal AlreadyReturnedQuantity { get; set; }
    public decimal AvailableReturnQuantity => Math.Max(0, SoldQuantity - AlreadyReturnedQuantity);
    public decimal ReturnQuantity { get; set; }
    public bool IsSelected { get; set; }
    public int ReturnReasonId { get; set; }
    public string? ReasonRemarks { get; set; }
    public bool SealIntact { get; set; } = true;
    public bool PackagingDamaged { get; set; }
    public bool ExpiryValid { get; set; } = true;
    public bool IsSaleable { get; set; } = true;
    public string? ScannedBatchNumber { get; set; }
    public bool BatchMismatchApproved { get; set; }
    public ScheduleDrugType ScheduleType { get; set; }
    public bool PrescriptionRequired { get; set; }
    public bool IsRefrigerated { get; set; }
    public string? ImagePath { get; set; }
    public decimal LineTotal { get; set; }
    public decimal ProportionalLineTotal { get; set; }
    public List<string> ValidationMessages { get; set; } = new();
}

public record SalePaymentSnapshotDto(PaymentMethod Method, decimal Amount, string? ReferenceNumber);

public class CreateSaleReturnRequest
{
    public int SaleId { get; set; }
    public bool ReturnEntireInvoice { get; set; }
    public RefundMode RefundMode { get; set; } = RefundMode.Cash;
    public string? Remarks { get; set; }
    public bool ManagerOverrideUsed { get; set; }
    public string? ManagerOverrideReason { get; set; }
    public int? ExchangeSaleId { get; set; }
    public decimal ExchangeAmount { get; set; }
    public List<ReturnRefundAllocationDto> RefundAllocations { get; set; } = new();
    public List<CreateSaleReturnLineRequest> Lines { get; set; } = new();
}

public class CreateSaleReturnLineRequest
{
    public int SaleItemId { get; set; }
    public decimal ReturnQuantity { get; set; }
    public int ReturnReasonId { get; set; }
    public string? ReasonRemarks { get; set; }
    public bool SealIntact { get; set; } = true;
    public bool PackagingDamaged { get; set; }
    public bool ExpiryValid { get; set; } = true;
    public bool IsSaleable { get; set; } = true;
    public string? ScannedBatchNumber { get; set; }
    public bool BatchMismatchApproved { get; set; }
}

public record ReturnRefundAllocationDto(RefundMode Mode, decimal Amount, string? TransactionReference);

public class SaleReturnReceiptDto
{
    public int SaleReturnId { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public string OriginalInvoiceNumber { get; set; } = string.Empty;
    public DateTime ReturnDate { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string? CompanyAddress { get; set; }
    public string? CompanyPhone { get; set; }
    public string? CompanyGst { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public RefundMode RefundMode { get; set; }
    public decimal RefundAmount { get; set; }
    public decimal SubTotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal GrandTotal { get; set; }
    public string? CreditNoteNumber { get; set; }
    public DateTime? CreditNoteExpiry { get; set; }
    public string CashierName { get; set; } = string.Empty;
    public string? Remarks { get; set; }
    public List<SaleReturnReceiptLineDto> Lines { get; set; } = new();
    public List<ReturnRefundAllocationDto> Refunds { get; set; } = new();
}

public record SaleReturnReceiptLineDto(
    int SrNo,
    string MedicineName,
    string BatchNumber,
    DateTime? ExpiryDate,
    decimal ReturnedQuantity,
    decimal Mrp,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal GstPercent,
    decimal LineTotal,
    string ReasonName,
    bool IsSaleable);

public record SaleReturnSummaryRowDto(
    string ReturnNumber,
    DateTime ReturnDate,
    string OriginalInvoiceNumber,
    string CustomerName,
    decimal RefundAmount,
    RefundMode RefundMode,
    string CashierName,
    bool IsFullReturn);

public record MedicineReturnReportRowDto(
    string MedicineName,
    string BatchNumber,
    decimal ReturnedQuantity,
    decimal RefundAmount,
    int ReturnCount);

public record DailySaleReturnReportDto(
    DateTime Date,
    int ReturnCount,
    decimal TotalRefund,
    decimal TotalGstReversed,
    int FullReturns,
    int PartialReturns);
