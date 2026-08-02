using PharmaPOS.Domain.Common;
using PharmaPOS.Domain.Entities.Inventory;
using PharmaPOS.Domain.Entities.Masters;
using PharmaPOS.Domain.Entities.Sales;

namespace PharmaPOS.Domain.Entities.Purchases;

public class PurchaseReturnItem : BaseEntity
{
    public int PurchaseReturnId { get; set; }
    public PurchaseReturn? PurchaseReturn { get; set; }

    /// <summary>Null for direct (no-bill) returns.</summary>
    public int? PurchaseItemId { get; set; }
    public PurchaseItem? PurchaseItem { get; set; }

    public int MedicineId { get; set; }
    public Medicine? Medicine { get; set; }

    public int? MedicineBatchId { get; set; }
    public MedicineBatch? MedicineBatch { get; set; }

    public string? BatchNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }

    public decimal ReturnedQuantity { get; set; }
    public decimal ReturnedFreeQuantity { get; set; }

    public decimal PurchasePrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal GstPercent { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }

    public int? ReturnReasonId { get; set; }
    public ReturnReason? ReturnReason { get; set; }
    public string? ReasonRemarks { get; set; }
}
