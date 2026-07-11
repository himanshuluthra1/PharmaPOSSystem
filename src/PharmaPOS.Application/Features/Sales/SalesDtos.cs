using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Application.Features.Sales;

/// <summary>A medicine match returned by the billing search box.</summary>
public record MedicineLookupDto(
    int Id,
    string Name,
    string? GenericName,
    string? Barcode,
    decimal GstPercent,
    decimal DefaultDiscountPercent,
    bool PrescriptionRequired,
    decimal TotalStock);

/// <summary>A sellable batch of a medicine (manual batch selection).</summary>
public record BatchLookupDto(
    int BatchId,
    string BatchNumber,
    DateTime? ExpiryDate,
    decimal QuantityAvailable,
    decimal Mrp,
    decimal SellingPrice,
    decimal GstPercent);

/// <summary>A customer match for the customer picker.</summary>
public record CustomerLookupDto(
    int Id,
    string Name,
    string? Phone,
    CustomerType Type,
    decimal OutstandingBalance,
    decimal CreditLimit);

/// <summary>A doctor match for the doctor picker.</summary>
public record DoctorLookupDto(int Id, string Name, string? Specialization);

/// <summary>One line to be billed (prices are MRP/tax-inclusive).</summary>
public class SaleLineRequest
{
    public int MedicineId { get; set; }
    public int MedicineBatchId { get; set; }
    public string? BatchNumber { get; set; }
    public decimal Quantity { get; set; }
    public decimal Mrp { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
}

/// <summary>A tender against the bill.</summary>
public class SalePaymentRequest
{
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public string? ReferenceNumber { get; set; }
}

/// <summary>Everything needed to create and finalize an invoice.</summary>
public class CreateSaleRequest
{
    public int? CustomerId { get; set; }
    public int? DoctorId { get; set; }
    public string? BillingCustomerName { get; set; }
    public string? BillingCustomerPhone { get; set; }
    public string? BillingCustomerAddress { get; set; }
    public string? BillingDoctorName { get; set; }
    public string? PrescriptionPath { get; set; }
    public string? Remarks { get; set; }
    public List<SaleLineRequest> Lines { get; set; } = new();
    public List<SalePaymentRequest> Payments { get; set; } = new();
    public int RewardPointsRedeemed { get; set; }
}

/// <summary>A row in the fast-billing bill history dropdown.</summary>
public record SaleListItemDto(int SaleId, string InvoiceNumber, DateTime InvoiceDate, string? PatientName = null)
{
    public string DisplayLabel => SaleId == 0
        ? $"{InvoiceNumber} (New)"
        : $"{InvoiceNumber} - {InvoiceDate:dd/MM/yyyy hh:mm tt} - {PatientName ?? "Walk-in"}";

    public bool IsNewBill => SaleId == 0;

    public string BillDateLabel => SaleId == 0
        ? "(New)"
        : InvoiceDate.ToString("dd/MM/yyyy hh:mm tt");

    public string BillPatientLabel => SaleId == 0
        ? string.Empty
        : string.IsNullOrWhiteSpace(PatientName) ? "Walk-in" : PatientName;
}

/// <summary>Bill search criteria for the fast-billing search popup.</summary>
public enum BillSearchType
{
    PatientName,
    MobileNumber,
    MedicineName
}

/// <summary>A bill row returned by the fast-billing search popup.</summary>
public record BillSearchResultDto(
    int SaleId,
    string InvoiceNumber,
    DateTime InvoiceDate,
    string? PatientName,
    string? Mobile,
    string? MatchedMedicine = null)
{
    public string BillDateLabel => InvoiceDate.ToString("dd/MM/yyyy hh:mm tt");

    public string BillPatientLabel => string.IsNullOrWhiteSpace(PatientName) ? "Walk-in" : PatientName;

    public string BillMobileLabel => Mobile ?? "—";

    public string DetailLabel => !string.IsNullOrWhiteSpace(MatchedMedicine)
        ? MatchedMedicine!
        : BillMobileLabel;
}

/// <summary>Full invoice payload for editing an existing bill.</summary>
public class SaleEditDto
{
    public int SaleId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public string? BillingCustomerName { get; set; }
    public string? BillingCustomerPhone { get; set; }
    public string? BillingCustomerAddress { get; set; }
    public string? BillingDoctorName { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public List<SaleEditLineDto> Lines { get; set; } = new();
}

public class SaleEditLineDto
{
    public int MedicineId { get; set; }
    public int MedicineBatchId { get; set; }
    public string MedicineName { get; set; } = string.Empty;
    public string BatchNumber { get; set; } = string.Empty;
    public DateTime? ExpiryDate { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Mrp { get; set; }
    public decimal GstPercent { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal AvailableStock { get; set; }
}

/// <summary>Update an existing invoice (same bill number, revised lines/totals).</summary>
public class UpdateSaleRequest : CreateSaleRequest
{
    public int SaleId { get; set; }
}

/// <summary>Printable snapshot of a finalized invoice.</summary>
public class SaleReceiptDto
{
    public int SaleId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }

    public string CompanyName { get; set; } = string.Empty;
    public string? CompanyAddress { get; set; }
    public string? CompanyPhone { get; set; }
    public string? CompanyGst { get; set; }
    public string? CompanyDrugLicense { get; set; }
    public string? InvoiceFooter { get; set; }

    public string CustomerName { get; set; } = "Walk-in Customer";
    public string? CustomerPhone { get; set; }
    public string? DoctorName { get; set; }

    public List<SaleReceiptLineDto> Lines { get; set; } = new();

    public decimal SubTotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal RoundOff { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal ChangeReturned { get; set; }
    public int RewardPointsEarned { get; set; }
}

public record SaleReceiptLineDto(
    int SerialNo,
    string MedicineName,
    string BatchNumber,
    DateTime? ExpiryDate,
    decimal Quantity,
    decimal Mrp,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal DiscountAmount,
    decimal GstPercent,
    decimal Amount);

/// <summary>Medicine snapshot shown from the billing grid (F4).</summary>
public record SaleMedicineDetailDto(
    string MedicineName,
    string? Salt,
    decimal QuantityAvailable,
    decimal CostPrice,
    decimal Mrp,
    string? Location,
    string PackingSize,
    string PackingType);
