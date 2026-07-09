using PharmaPOS.Domain.Common;
using PharmaPOS.Domain.Entities.Masters;

namespace PharmaPOS.Domain.Entities.Inventory;

/// <summary>
/// A specific batch/lot of a medicine held in a branch, with its own expiry,
/// pricing and quantity on hand. This is the unit of stock the system decrements
/// on sale (using FIFO/FEFO) and increments on purchase.
/// </summary>
public class MedicineBatch : BranchEntity
{
    public int MedicineId { get; set; }
    public Medicine? Medicine { get; set; }

    public string BatchNumber { get; set; } = string.Empty;
    public DateTime? ManufacturingDate { get; set; }
    public DateTime? ExpiryDate { get; set; }

    public decimal QuantityAvailable { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal Mrp { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal GstPercent { get; set; }

    public string? RackNumber { get; set; }

    public bool IsExpired => ExpiryDate.HasValue && ExpiryDate.Value.Date < DateTime.UtcNow.Date;
}
