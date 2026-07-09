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
}
