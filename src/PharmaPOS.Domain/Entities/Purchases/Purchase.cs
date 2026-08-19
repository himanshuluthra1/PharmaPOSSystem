using PharmaPOS.Domain.Common;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Domain.Entities.Masters;

namespace PharmaPOS.Domain.Entities.Purchases;

/// <summary>
/// Purchase invoice / GRN header. Receiving a purchase creates batches and
/// increments stock, and posts a payable to the supplier ledger.
/// </summary>
public class Purchase : BranchEntity
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public string? SupplierInvoiceNumber { get; set; }
    public DateTime InvoiceDate { get; set; } = DateTime.UtcNow;

    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public int? PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }

    public decimal SubTotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal RoundOff { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal PaidAmount { get; set; }

    public PurchaseStatus Status { get; set; } = PurchaseStatus.Draft;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;
    public string? InvoiceDocumentPath { get; set; }
    public string? Remarks { get; set; }

    /// <summary>When true, GRN cannot be changed until unlocked.</summary>
    public bool IsLocked { get; set; }
    public DateTime? LockedAtUtc { get; set; }
    public string? LockedBy { get; set; }

    /// <summary>Required when cash/bank paid is less than grand total.</summary>
    public PurchasePartialPaymentReason? PartialPaymentReason { get; set; }
    public string? PartialPaymentNotes { get; set; }

    /// <summary>Purchase return whose supplier credit was applied toward this bill.</summary>
    public int? LinkedPurchaseReturnId { get; set; }
    public PurchaseReturn? LinkedPurchaseReturn { get; set; }

    /// <summary>Amount of return credit applied onto <see cref="PaidAmount"/>.</summary>
    public decimal ReturnCreditApplied { get; set; }

    public ICollection<PurchaseItem> Items { get; set; } = new List<PurchaseItem>();
}
