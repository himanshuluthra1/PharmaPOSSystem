using PharmaPOS.Domain.Common;
using PharmaPOS.Domain.Entities.Masters;
using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Domain.Entities.Purchases;

/// <summary>
/// Goods returned to a supplier. May reference a purchase bill, or be a direct
/// stock return without a bill. Stock is reduced when the return is created; the
/// supplier's debit/return receipt number is often received later and attached via
/// <see cref="SupplierReturnReceiptNumber"/>.
/// </summary>
public class PurchaseReturn : BranchEntity
{
    public string ReturnNumber { get; set; } = string.Empty;

    /// <summary>Null when returning stock directly without an original purchase bill.</summary>
    public int? PurchaseId { get; set; }
    public Purchase? Purchase { get; set; }

    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public DateTime ReturnDate { get; set; } = DateTime.UtcNow;

    public decimal SubTotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal RoundOff { get; set; }
    public decimal GrandTotal { get; set; }

    /// <summary>Amount credited against supplier payable (usually = GrandTotal).</summary>
    public decimal CreditAmount { get; set; }

    public PurchaseReturnSettlementMode SettlementMode { get; set; } = PurchaseReturnSettlementMode.SupplierCredit;
    public PurchaseReturnStatus Status { get; set; } = PurchaseReturnStatus.Completed;
    public bool IsFullReturn { get; set; }
    public string? Remarks { get; set; }

    /// <summary>Debit note / return receipt number issued by the supplier (often filled days later).</summary>
    public string? SupplierReturnReceiptNumber { get; set; }

    public DateTime? SupplierReturnReceiptDate { get; set; }

    public ICollection<PurchaseReturnItem> Items { get; set; } = new List<PurchaseReturnItem>();

    public bool HasSupplierReceipt => !string.IsNullOrWhiteSpace(SupplierReturnReceiptNumber);
    public bool IsDirectReturn => PurchaseId is null;
}
