using PharmaPOS.Domain.Common;
using PharmaPOS.Domain.Entities.Masters;
using PharmaPOS.Domain.Entities.Purchases;
using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Domain.Entities.Inventory;

/// <summary>
/// Lost-sale / shortage demand when a medicine could not be sold from stock.
/// Drives purchase orders alongside reorder levels.
/// </summary>
public class ShortageBookEntry : BranchEntity
{
    public int MedicineId { get; set; }
    public Medicine? Medicine { get; set; }

    public decimal RequestedQuantity { get; set; }
    public decimal AvailableQuantity { get; set; }
    public decimal ShortfallQuantity { get; set; }

    public ShortageStatus Status { get; set; } = ShortageStatus.Open;
    public ShortageSource Source { get; set; } = ShortageSource.SalesCart;

    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public string? Notes { get; set; }

    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
    public string? RecordedBy { get; set; }

    public int? PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }

    public DateTime? ResolvedAtUtc { get; set; }
}
