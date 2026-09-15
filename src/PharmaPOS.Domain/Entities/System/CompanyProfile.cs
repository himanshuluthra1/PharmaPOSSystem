using PharmaPOS.Domain.Common;
using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Domain.Entities.System;

/// <summary>Singleton company/store settings used on invoices and reports.</summary>
public class CompanyProfile : BaseEntity
{
    public string CompanyName { get; set; } = string.Empty;
    public string? LegalName { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Pincode { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? GstNumber { get; set; }
    public string? DrugLicenseNumber { get; set; }
    public string? Pan { get; set; }
    public string? LogoPath { get; set; }
    public string? InvoiceFooter { get; set; }
    /// <summary>UPI VPA (e.g. shop@okicici) printed as a pay QR on sale bills.</summary>
    public string? UpiVpa { get; set; }
    public string Currency { get; set; } = "INR";
    public string? CurrencySymbol { get; set; } = "\u20B9";

    // Operational preferences (singleton settings row).
    public int NearExpiryDays { get; set; } = 90;
    public int DefaultLowStockThreshold { get; set; } = 10;
    public string SalesInvoicePrefix { get; set; } = "INV";
    public string PurchaseInvoicePrefix { get; set; } = "PUR";
    public string SaleReturnPrefix { get; set; } = "SR";
    public string PurchaseReturnPrefix { get; set; } = "PR";
    public string CreditNotePrefix { get; set; } = "CN";

    /// <summary>When true, completed sale invoices can be opened and saved again.</summary>
    public bool AllowEditSalesBills { get; set; }
    /// <summary>When true, received purchase invoices can be opened and saved again.</summary>
    public bool AllowEditPurchaseBills { get; set; }

    /// <summary>Default sale-bill paper template (A4, A5, 80 mm, 58 mm).</summary>
    public InvoicePaperSize InvoicePaperSize { get; set; } = InvoicePaperSize.A4;

    /// <summary>
    /// Financial year start calendar year to view (e.g. 2024 ⇒ FY 2024-25).
    /// Null or current year ⇒ current FY (editable).
    /// </summary>
    public int? ViewFinancialYearStartYear { get; set; }

    /// <summary>When true, Dashboard shows Today's Sales KPI.</summary>
    public bool ShowDashboardTodaySales { get; set; } = true;

    /// <summary>When true, Dashboard shows Monthly Sales section.</summary>
    public bool ShowDashboardMonthlySales { get; set; } = true;

    // Sale return policy (configurable).
    public int SaleReturnAllowedDays { get; set; } = 30;
    public decimal SaleReturnHighValueThreshold { get; set; } = 5000m;
    public bool SaleReturnBlockExpired { get; set; } = true;
    public bool SaleReturnBlockScheduleDrugs { get; set; } = false;
    public bool SaleReturnBlockRefrigerated { get; set; } = false;
    public bool SaleReturnRefundOriginalPaymentMode { get; set; } = true;
    public int CreditNoteValidityDays { get; set; } = 90;
}
