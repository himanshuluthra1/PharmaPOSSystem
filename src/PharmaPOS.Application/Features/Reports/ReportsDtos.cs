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
    MedicineReturns
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
    decimal PaidAmount,
    decimal BalanceDue)
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
    decimal StockValue)
{
    public string ExpiryLabel => ExpiryDate?.ToString("dd/MM/yyyy") ?? "—";
}

public record ExpiryReportRowDto(
    string MedicineName,
    string BatchNumber,
    DateTime? ExpiryDate,
    decimal Quantity,
    decimal StockValue,
    string ExpiryStatus)
{
    public string ExpiryLabel => ExpiryDate?.ToString("dd/MM/yyyy") ?? "—";
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
