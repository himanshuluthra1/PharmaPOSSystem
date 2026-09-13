using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Application.Features.Reports;

public enum ReportKind
{
    // Sales
    Sales,
    SalesByCustomer,
    SalesByMedicine,
    SalesByPaymentMode,
    SalesDayWise,
    SalesCreditDue,
    Profit,
    SaleReturns,
    MedicineReturns,
    ScheduleRegister,

    // Purchase
    Purchases,
    PurchasesBySupplier,
    SupplierOutstanding,
    SupplierPayments,
    PurchaseReturns,
    ExpiryToCompanyClaims,

    // Customers
    CustomerOutstanding,
    CustomerReceipts,

    // Payments & Expenses
    PaymentVouchers,
    ReceiptVouchers,
    ExpenseRegister,
    ExpenseByAccount,
    CashBookSummary,

    // GST
    GstSummary,
    Gstr1,
    Gstr2B,

    // Stock
    StockValuation,
    BatchStock,
    Expiry,
    LowStock,
    SlowMovingStock,
    StockAdjustments,

    // Sales detail
    MedicinesSoldByDate
}

public sealed class ReportKindOption(ReportKind kind, string label, string description)
{
    public ReportKind Kind { get; } = kind;
    public string Label { get; } = label;
    public string Description { get; } = description;
}

public record ReportDefinition(
    ReportKind Kind,
    string Group,
    string Label,
    string Description,
    bool UsesDateRange,
    IReadOnlyList<FilterPreset> FilterPresets);

public enum FilterPreset
{
    None,
    PaymentStatus,
    GstDocumentType,
    Gstr1Section,
    Gstr2BSection,
    ExpiryWindow,
    LowStockSeverity,
    SaleReturnMode
}

public record ReportColumnDto(string Key, string Header, string? Format = null);

public sealed class ReportTableDto
{
    public ReportSummaryDto Summary { get; set; } = new();
    public List<ReportColumnDto> Columns { get; set; } = [];
    public List<Dictionary<string, object?>> Rows { get; set; } = [];
    public GstSummaryDto? GstSummary { get; set; }
    public GstReturnExportDto? GstReturnExport { get; set; }
    public ScheduleRegisterReportDto? ScheduleRegister { get; set; }
}

public static class ReportCatalog
{
    public const string GroupSales = "Sales";
    public const string GroupPurchase = "Purchase";
    public const string GroupCustomers = "Customers";
    public const string GroupPayments = "Payments & Expenses";
    public const string GroupGst = "GST";
    public const string GroupStock = "Stock & Inventory";

    public static IReadOnlyList<ReportDefinition> All { get; } =
    [
        // Sales
        Def(ReportKind.Sales, GroupSales, "Invoice Register",
            "Completed sales invoices for the selected period.", true, FilterPreset.PaymentStatus),
        Def(ReportKind.SalesByCustomer, GroupSales, "By Customer",
            "Sales totals grouped by customer / patient.", true, FilterPreset.None),
        Def(ReportKind.SalesByMedicine, GroupSales, "By Medicine",
            "Quantity and revenue ranked by medicine.", true, FilterPreset.None),
        Def(ReportKind.MedicinesSoldByDate, GroupSales, "Medicines Sold by Date",
            "Sale-line medicines with date, invoice, qty and value. Add to shortage book (F7).", true, FilterPreset.None),
        Def(ReportKind.SalesByPaymentMode, GroupSales, "By Payment Mode",
            "Collections split by Cash, UPI, Card, Credit, etc.", true, FilterPreset.None),
        Def(ReportKind.SalesDayWise, GroupSales, "Day-wise Summary",
            "Daily sales count, tax and totals.", true, FilterPreset.None),
        Def(ReportKind.SalesCreditDue, GroupSales, "Credit / Balance Due",
            "Open credit bills with amount still due.", true, FilterPreset.None),
        Def(ReportKind.Profit, GroupSales, "Gross Profit",
            "Revenue vs estimated cost per sale invoice.", true, FilterPreset.None),
        Def(ReportKind.SaleReturns, GroupSales, "Sale Returns",
            "Return transactions for the selected period.", true, FilterPreset.SaleReturnMode),
        Def(ReportKind.MedicineReturns, GroupSales, "Medicine-wise Returns",
            "Returned quantities grouped by medicine and batch.", true, FilterPreset.None),
        Def(ReportKind.ScheduleRegister, GroupSales, "Schedule H / H1 Register",
            "Inspector register for Schedule H and H1 sales.", true, FilterPreset.None),

        // Purchase
        Def(ReportKind.Purchases, GroupPurchase, "Purchase Register",
            "Received purchase / GRN invoices for the period.", true, FilterPreset.PaymentStatus),
        Def(ReportKind.PurchasesBySupplier, GroupPurchase, "By Supplier",
            "Purchase totals grouped by supplier.", true, FilterPreset.None),
        Def(ReportKind.SupplierOutstanding, GroupPurchase, "Supplier Outstanding",
            "Open payables by supplier (current balance).", false, FilterPreset.None),
        Def(ReportKind.SupplierPayments, GroupPurchase, "Purchase Payments",
            "Supplier bills with paid, partial, and pending amounts. Filter by payment status.", true, FilterPreset.PaymentStatus),
        Def(ReportKind.PurchaseReturns, GroupPurchase, "Purchase Returns",
            "Supplier returns for the period — kind, amount, and how credit was settled (receipt or purchase bill).", true, FilterPreset.None),
        Def(ReportKind.ExpiryToCompanyClaims, GroupPurchase, "Expiry to Company Claims",
            "Expiry claims sent to suppliers — claim lines, expected credit, and credit-note status.", true, FilterPreset.None),

        // Customers
        Def(ReportKind.CustomerOutstanding, GroupCustomers, "Customer Outstanding",
            "Open receivables by customer (current balance).", false, FilterPreset.None),
        Def(ReportKind.CustomerReceipts, GroupCustomers, "Customer Receipts",
            "Receipt vouchers collected from customers.", true, FilterPreset.None),
        Def(ReportKind.SalesByCustomer, GroupCustomers, "Customer-wise Sales",
            "Sales totals grouped by customer / patient.", true, FilterPreset.None),
        Def(ReportKind.SalesCreditDue, GroupCustomers, "Pending Collections",
            "Customer bills still due (pending receipts).", true, FilterPreset.None),

        // Payments & Expenses
        Def(ReportKind.SupplierPayments, GroupPayments, "Purchase Payments",
            "Supplier bills with paid, partial, and pending amounts. Filter by payment status.", true, FilterPreset.PaymentStatus),
        Def(ReportKind.PaymentVouchers, GroupPayments, "Payment Vouchers",
            "Supplier payment vouchers posted in the period.", true, FilterPreset.None),
        Def(ReportKind.ReceiptVouchers, GroupPayments, "Receipt Vouchers",
            "Customer receipt vouchers posted in the period.", true, FilterPreset.None),
        Def(ReportKind.SalesCreditDue, GroupPayments, "Pending Collections",
            "Customer bills still due (pending receipts).", true, FilterPreset.None),
        Def(ReportKind.ExpenseRegister, GroupPayments, "Expense Register",
            "Expense vouchers with account and amount.", true, FilterPreset.None),
        Def(ReportKind.ExpenseByAccount, GroupPayments, "Expense by Account",
            "Expenses rolled up by expense account.", true, FilterPreset.None),
        Def(ReportKind.CashBookSummary, GroupPayments, "Cash Book Summary",
            "Day-wise cash in / out / closing from the cash book.", true, FilterPreset.None),

        // GST
        Def(ReportKind.GstSummary, GroupGst, "GST Summary",
            "Output vs input GST with invoice-wise detail.", true, FilterPreset.GstDocumentType),
        Def(ReportKind.Gstr1, GroupGst, "GSTR-1 export",
            "B2B / B2CS / HSN / credit notes from sales. Export JSON or Excel.", true, FilterPreset.Gstr1Section),
        Def(ReportKind.Gstr2B, GroupGst, "GSTR-2B worksheet",
            "Inward invoices and ITC by rate from purchases.", true, FilterPreset.Gstr2BSection),

        // Stock
        Def(ReportKind.StockValuation, GroupStock, "Stock Valuation",
            "Current stock value at MRP and purchase cost (includes negative qty batches).", false, FilterPreset.None),
        Def(ReportKind.BatchStock, GroupStock, "Batch-wise Stock",
            "All batches with non-zero quantity (including negative).", false, FilterPreset.None),
        Def(ReportKind.Expiry, GroupStock, "Expiry Report",
            "Expired stock and batches expiring within 1–12 months.", false, FilterPreset.ExpiryWindow),
        Def(ReportKind.LowStock, GroupStock, "Low Stock",
            "Medicines at or below reorder level.", false, FilterPreset.LowStockSeverity),
        Def(ReportKind.SlowMovingStock, GroupStock, "Slow / Non-moving Stock",
            "Stock with no sale in 90+ days (or never sold).", false, FilterPreset.None),
        Def(ReportKind.StockAdjustments, GroupStock, "Stock Adjustments",
            "Manual / physical stock adjustments with system vs physical qty.", true, FilterPreset.None),
    ];

    public static IReadOnlyList<string> Groups { get; } =
    [
        GroupSales, GroupPurchase, GroupCustomers, GroupPayments, GroupGst, GroupStock
    ];

    public static ReportDefinition Get(ReportKind kind) =>
        All.First(d => d.Kind == kind);

    public static IEnumerable<ReportDefinition> ForGroup(string group) =>
        All.Where(d => d.Group == group);

    /// <summary>Unique kinds for the report picker (Customer-wise Sales shares SalesByCustomer).</summary>
    public static IReadOnlyList<ReportKindOption> DistinctOptions()
    {
        var seen = new HashSet<ReportKind>();
        var list = new List<ReportKindOption>();
        foreach (var d in All)
        {
            if (!seen.Add(d.Kind)) continue;
            list.Add(new ReportKindOption(d.Kind, d.Label, d.Description));
        }
        return list;
    }

    private static ReportDefinition Def(
        ReportKind kind, string group, string label, string description,
        bool usesDateRange, FilterPreset filter) =>
        new(kind, group, label, description, usesDateRange, [filter]);
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
    decimal PaidAmount,
    decimal CashPaid,
    decimal ReturnCreditApplied,
    decimal BalanceDue,
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
    decimal GrandTotal,
    int DocumentId = 0)
{
    public string InvoiceDateLabel => InvoiceDate.ToString("dd/MM/yyyy");
    public decimal TotalTax => CgstAmount + SgstAmount + IgstAmount;
}

public record ProfitReportRowDto(
    int SaleId,
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
    int MedicineId,
    string MedicineName,
    string? GenericName,
    decimal QuantitySold,
    decimal Revenue,
    decimal Cost,
    decimal GrossProfit)
{
    public decimal MarginPercent => Revenue > 0 ? Math.Round(GrossProfit / Revenue * 100m, 1) : 0m;
}

public record MedicinesSoldByDateRowDto(
    DateTime SaleDate,
    string InvoiceNumber,
    int SaleId,
    int MedicineId,
    string MedicineName,
    string? GenericName,
    string BatchNumber,
    decimal Quantity,
    decimal Revenue,
    decimal Cost,
    decimal GrossProfit)
{
    public string SaleDateLabel => SaleDate.ToString("dd/MM/yyyy");
    public decimal MarginPercent => Revenue > 0 ? Math.Round(GrossProfit / Revenue * 100m, 1) : 0m;
}

public record StockValuationReportRowDto(
    string MedicineName,
    string? SupplierName,
    string BatchNumber,
    DateTime? ExpiryDate,
    decimal Quantity,
    decimal PurchasePrice,
    decimal Mrp,
    decimal StockValue,
    decimal StockAmount)
{
    public string ExpiryLabel => ExpiryDate?.ToString("dd/MM/yyyy") ?? "—";
    public string SupplierLabel => string.IsNullOrWhiteSpace(SupplierName) ? "—" : SupplierName;
}

public record StockAdjustmentReportRowDto(
    DateTime AdjustmentDate,
    string AdjustmentNumber,
    string MedicineName,
    string BatchNumber,
    decimal SystemQuantity,
    decimal PhysicalQuantity,
    decimal Difference,
    string? Reason,
    string? Remarks)
{
    public string AdjustmentDateLabel => AdjustmentDate.ToString("dd/MM/yyyy");
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

/// <summary>One sale bill in the Sales By Customer drill-down list.</summary>
[Obsolete("Use ReportBillListRowDto")]
public record CustomerSaleBillRowDto(
    int SaleId,
    string InvoiceNumber,
    DateTime InvoiceDate,
    decimal GrandTotal,
    decimal PaidAmount,
    decimal BalanceDue,
    string Status)
{
    public string InvoiceDateLabel => InvoiceDate.ToString("dd/MM/yyyy hh:mm tt");
}

public enum ReportDocumentKind
{
    Sale = 0,
    Purchase = 1
}

/// <summary>Underlying sale or purchase bill for a consolidated report row drill-down.</summary>
public record ReportBillListRowDto(
    ReportDocumentKind DocumentKind,
    int DocumentId,
    string InvoiceNumber,
    DateTime InvoiceDate,
    string PartyName,
    decimal GrandTotal,
    decimal PaidAmount,
    decimal BalanceDue,
    string Status)
{
    public string InvoiceDateLabel => InvoiceDate.ToString("dd/MM/yyyy hh:mm tt");
    public string KindLabel => DocumentKind == ReportDocumentKind.Sale ? "Sale" : "Purchase";
}

/// <summary>Query keys for listing bills behind a consolidated report row.</summary>
public sealed class ReportBillDrillDownQuery
{
    public required ReportKind Kind { get; init; }
    public DateTime From { get; init; }
    public DateTime To { get; init; }
    public int? BranchId { get; init; }
    public string Title { get; init; } = "Bills";
    public string? CustomerKey { get; init; }
    public int? CustomerId { get; init; }
    public int? SupplierId { get; init; }
    public int? MedicineId { get; init; }
    public string? BatchNumber { get; init; }
    public string? PaymentMethod { get; init; }
    public DateTime? Day { get; init; }
    public bool OpenBillsOnly { get; init; }
}
