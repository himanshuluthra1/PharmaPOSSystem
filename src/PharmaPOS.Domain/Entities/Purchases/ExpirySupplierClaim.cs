using PharmaPOS.Domain.Common;
using PharmaPOS.Domain.Entities.Inventory;
using PharmaPOS.Domain.Entities.Masters;
using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Domain.Entities.Purchases;

/// <summary>
/// Expiry-to-company claim: near-expired / expired batches returned to the supplier
/// for a credit note. Stock is deducted via a linked <see cref="PurchaseReturn"/>.
/// </summary>
public class ExpirySupplierClaim : BranchEntity
{
    public string ClaimNumber { get; set; } = string.Empty;
    public DateTime ClaimDate { get; set; }

    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public ExpiryClaimStatus Status { get; set; } = ExpiryClaimStatus.AwaitingCreditNote;

    public int PurchaseReturnId { get; set; }
    public PurchaseReturn? PurchaseReturn { get; set; }

    public decimal ExpectedCreditAmount { get; set; }

    /// <summary>Whether credit was recorded via a CN or against a purchase bill.</summary>
    public ExpiryCreditSettlementKind CreditSettlementKind { get; set; } = ExpiryCreditSettlementKind.CreditNote;

    public string? CreditNoteNumber { get; set; }
    public DateTime? CreditNoteDate { get; set; }
    public decimal? CreditNoteAmount { get; set; }

    /// <summary>When <see cref="CreditSettlementKind"/> is PurchaseBill, the bill that absorbed the credit.</summary>
    public int? SettledAgainstPurchaseId { get; set; }
    public Purchase? SettledAgainstPurchase { get; set; }

    public string? Remarks { get; set; }

    public ICollection<ExpirySupplierClaimItem> Items { get; set; } = new List<ExpirySupplierClaimItem>();
}

public class ExpirySupplierClaimItem : BaseEntity
{
    public int ClaimId { get; set; }
    public ExpirySupplierClaim? Claim { get; set; }

    public int MedicineId { get; set; }
    public Medicine? Medicine { get; set; }

    public int MedicineBatchId { get; set; }
    public MedicineBatch? MedicineBatch { get; set; }

    public int? PurchaseId { get; set; }
    public Purchase? Purchase { get; set; }

    public string BatchNumber { get; set; } = string.Empty;
    public DateTime? ExpiryDate { get; set; }

    public decimal StockQuantity { get; set; }
    public decimal ClaimQuantity { get; set; }

    public decimal PurchasePrice { get; set; }
    public decimal GstPercent { get; set; }
    public decimal LineTotal { get; set; }

    /// <summary>Supplier refund of this line as a percent of calculated amount (default 100).</summary>
    public decimal RefundPercent { get; set; } = 100m;
}
