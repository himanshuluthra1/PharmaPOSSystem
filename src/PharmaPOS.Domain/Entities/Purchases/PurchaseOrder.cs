using PharmaPOS.Domain.Common;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Domain.Entities.Masters;

namespace PharmaPOS.Domain.Entities.Purchases;

/// <summary>Purchase order raised to a supplier before goods are received.</summary>
public class PurchaseOrder : BranchEntity
{
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;
    public DateTime? ExpectedDate { get; set; }

    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public PurchaseStatus Status { get; set; } = PurchaseStatus.Draft;
    public decimal TotalAmount { get; set; }
    public string? Remarks { get; set; }

    public ICollection<PurchaseOrderItem> Items { get; set; } = new List<PurchaseOrderItem>();
}

public class PurchaseOrderItem : BaseEntity
{
    public int PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }

    public int MedicineId { get; set; }
    public Medicine? Medicine { get; set; }

    public decimal Quantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal EstimatedPrice { get; set; }
}
