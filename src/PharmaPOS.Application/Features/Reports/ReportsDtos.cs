using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Application.Features.Reports;

public enum ReportKind
{
    Sales,
    Purchases,
    GstSummary,
    Profit,
    SalesByMedicine,
    StockValuation,
    Expiry,
    LowStock,
    SaleReturns,
    MedicineReturns,
    ScheduleRegister
}

public sealed class ReportKindOption(ReportKind kind, string label, string description)
{
    public ReportKind Kind { get; } = kind;
    public string Label { get; } = label;
    public string Description { get; } = description;
}

public class ReportSummaryDto
{
    public decimal TotalAmount { get; set; }
    public decimal TotalTax { get; set; }
    public decimal TotalDiscount { get; set; }
    public int RecordCount { get; set; }
    public string? FooterNote { get; set; }
}

public record SalesReportRowDto(
    int SaleId,
    string InvoiceNumber,
    DateTime InvoiceDate,
    string CustomerName,
    int ItemCount,
    decimal SubTotal,
    decimal DiscountAmount,
    decimal CgstAmount,
    decimal SgstAmount,
    decimal IgstAmount,
    decimal GrandTotal,
    decimal PaidAmount,
    decimal BalanceDue)
{
    public string InvoiceDateLabel => InvoiceDate.ToString("dd/MM/yyyy hh:mm tt");
    public decimal TaxAmount => CgstAmount + SgstAmount + IgstAmount;
}

public record PurchaseReportRowDto(
    int PurchaseId,
    string InvoiceNumber,
    DateTime InvoiceDate,
    string SupplierName,
    int ItemCount,
    decimal SubTotal,
    decimal DiscountAmount,
    decimal CgstAmount,
    decimal SgstAmount,
    decimal IgstAmount,
    decimal GrandTotal,
    /// <summary>Total settled (cash/bank + return credit applied).</summary>
    decimal PaidAmount,
    /// <summary>Cash/bank paid only (excludes return credit).</summary>
    decimal CashPaid,
    /// <summary>Supplier return credit applied toward this bill.</summary>
    decimal ReturnCreditApplied,
    /// <summary>Net amount still payable after cash and return credit.</summary>
    decimal BalanceDue,
    /// <summary>Why the bill was left unpaid / partially paid.</summary>
    string DueReason)
{
    public string InvoiceDateLabel => InvoiceDate.ToString("dd/MM/yyyy hh:mm tt");
    public decimal TaxAmount => CgstAmount + SgstAmount + IgstAmount;
}

public class GstSummaryDto
{
    public decimal SalesTaxable { get; set; }
    public decimal SalesCgst { get; set; }
    public decimal SalesSgst { get; set; }
    public decimal SalesIgst { get; set; }
    public decimal SalesTotalTax { get; set; }
    public decimal SalesGrandTotal { get; set; }

    public decimal PurchaseTaxable { get; set; }
    public decimal PurchaseCgst { get; set; }
    public decimal PurchaseSgst { get; set; }
    public decimal PurchaseIgst { get; set; }
    public decimal PurchaseTotalTax { get; set; }
    public decimal PurchaseGrandTotal { get; set; }

    public decimal NetTaxPayable => SalesTotalTax - PurchaseTotalTax;
}

public record GstDetailRowDto(
    string DocumentType,
    string InvoiceNumber,
    DateTime InvoiceDate,
    string PartyName,
    decimal TaxableAmount,
    decimal CgstAmount,
    decimal SgstAmount,
    decimal IgstAmount,
    decimal GrandTotal)
{
    public string InvoiceDateLabel => InvoiceDate.ToString("dd/MM/yyyy");
    public decimal TotalTax => CgstAmount + SgstAmount + IgstAmount;
}

public record ProfitReportRowDto(
    string InvoiceNumber,
    DateTime InvoiceDate,
    string CustomerName,
    decimal Revenue,
    decimal Cost,
    decimal GrossProfit)
{
    public string InvoiceDateLabel => InvoiceDate.ToString("dd/MM/yyyy hh:mm tt");
    public decimal MarginPercent => Revenue > 0 ? Math.Round(GrossProfit / Revenue * 100m, 1) : 0m;
}

public record MedicineSalesRowDto(
    string MedicineName,
    string? GenericName,
    decimal QuantitySold,
    decimal Revenue,
    decimal Cost,
    decimal GrossProfit)
{
    public decimal MarginPercent => Revenue > 0 ? Math.Round(GrossProfit / Revenue * 100m, 1) : 0m;
}

public record StockValuationReportRowDto(
    string MedicineName,
    string BatchNumber,
    DateTime? ExpiryDate,
    decimal Quantity,
    decimal PurchasePrice,
    decimal Mrp,
    decimal StockValue,
    decimal StockAmount)
{
    public string ExpiryLabel => ExpiryDate?.ToString("dd/MM/yyyy") ?? "—";
}

public record ExpiryReportRowDto(
    string MedicineName,
    string BatchNumber,
    DateTime? ExpiryDate,
    decimal Quantity,
    decimal StockValue,
    string ExpiryStatus,
    int? SupplierId = null,
    string? SupplierName = null)
{
    public string ExpiryLabel => ExpiryDate?.ToString("dd/MM/yyyy") ?? "—";
    public string SupplierLabel => string.IsNullOrWhiteSpace(SupplierName) ? "—" : SupplierName;
}

public record LowStockReportRowDto(
    string MedicineName,
    string? GenericName,
    decimal QuantityOnHand,
    int ReorderLevel,
    int ReorderQuantity,
    decimal Shortfall)
{
    public bool IsCritical => QuantityOnHand <= 0;
}

/// <summary>Which scheduled drugs to include in the inspector register.</summary>
public enum ScheduleRegisterFilter
{
    HAndH1 = 0,
    ScheduleH = 1,
    ScheduleH1 = 2
}

public record ScheduleRegisterRowDto(
    int SaleId,
    DateTime InvoiceDate,
    string InvoiceNumber,
    string PatientName,
    string? PatientPhone,
    string? DoctorName,
    string? DoctorRegistration,
    string MedicineName,
    ScheduleDrugType ScheduleType,
    string? BatchNumber,
    decimal Quantity)
{
    public string InvoiceDateLabel => InvoiceDate.ToString("dd-MMM-yyyy");
    public string ScheduleLabel => ScheduleType switch
    {
        ScheduleDrugType.ScheduleH => "H",
        ScheduleDrugType.ScheduleH1 => "H1",
        ScheduleDrugType.ScheduleX => "X",
        ScheduleDrugType.ScheduleG => "G",
        ScheduleDrugType.Otc => "OTC",
        _ => "—"
    };
    public string DoctorDisplay => string.IsNullOrWhiteSpace(DoctorName)
        ? "—"
        : string.IsNullOrWhiteSpace(DoctorRegistration)
            ? DoctorName
            : $"{DoctorName} ({DoctorRegistration})";
}

public sealed class ScheduleRegisterReportDto
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public ScheduleRegisterFilter Filter { get; set; }
    public string FilterLabel { get; set; } = "Schedule H / H1";
    public string CompanyName { get; set; } = string.Empty;
    public string? DrugLicenseNumber { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public List<ScheduleRegisterRowDto> Rows { get; set; } = new();
    public decimal TotalQuantity => Rows.Sum(r => r.Quantity);
    public int RecordCount => Rows.Count;
}
