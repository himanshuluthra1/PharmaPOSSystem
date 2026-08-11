using PharmaPOS.Domain.Common;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Domain.Entities.Masters;

namespace PharmaPOS.Domain.Entities.Sales;

/// <summary>Sales invoice header. The core transaction of the POS.</summary>
public class Sale : BranchEntity
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; } = DateTime.UtcNow;

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public int? DoctorId { get; set; }
    public Doctor? Doctor { get; set; }

    /// <summary>Free-text billing details when no master customer/doctor is linked.</summary>
    public string? BillingCustomerName { get; set; }
    public string? BillingCustomerPhone { get; set; }
    public string? BillingCustomerAddress { get; set; }
    public string? BillingDoctorName { get; set; }

    public string? PrescriptionPath { get; set; }

    public decimal SubTotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal RoundOff { get; set; }
    public decimal GrandTotal { get; set; }

    public decimal PaidAmount { get; set; }
    public decimal ChangeReturned { get; set; }
    public int RewardPointsEarned { get; set; }
    public int RewardPointsRedeemed { get; set; }

    public SaleStatus Status { get; set; } = SaleStatus.Draft;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;
    public string? Remarks { get; set; }

    /// <summary>Billing counter that collected this sale (multi-counter shops).</summary>
    public int? CounterId { get; set; }
    public BillingCounter? Counter { get; set; }

    /// <summary>Operator session on the counter when the sale was made.</summary>
    public int? CounterSessionId { get; set; }
    public CounterSession? CounterSession { get; set; }

    public ICollection<SaleItem> Items { get; set; } = new List<SaleItem>();
    public ICollection<SalePayment> Payments { get; set; } = new List<SalePayment>();
}
