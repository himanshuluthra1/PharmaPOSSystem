using PharmaPOS.Domain.Common;

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
    public string Currency { get; set; } = "INR";
    public string? CurrencySymbol { get; set; } = "\u20B9";

    // Operational preferences (singleton settings row).
    public int NearExpiryDays { get; set; } = 90;
    public int DefaultLowStockThreshold { get; set; } = 10;
    public string SalesInvoicePrefix { get; set; } = "INV";
    public string PurchaseInvoicePrefix { get; set; } = "PUR";
    public string SaleReturnPrefix { get; set; } = "SR";
    public string CreditNotePrefix { get; set; } = "CN";

    // Sale return policy (configurable).
    public int SaleReturnAllowedDays { get; set; } = 30;
    public decimal SaleReturnHighValueThreshold { get; set; } = 5000m;
    public bool SaleReturnBlockExpired { get; set; } = true;
    public bool SaleReturnBlockScheduleDrugs { get; set; } = false;
    public bool SaleReturnBlockRefrigerated { get; set; } = false;
    public bool SaleReturnRefundOriginalPaymentMode { get; set; } = true;
    public int CreditNoteValidityDays { get; set; } = 90;
}
