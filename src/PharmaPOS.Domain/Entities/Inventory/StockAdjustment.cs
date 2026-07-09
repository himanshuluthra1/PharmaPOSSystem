using PharmaPOS.Domain.Common;
using PharmaPOS.Domain.Entities.Masters;

namespace PharmaPOS.Domain.Entities.Inventory;

/// <summary>Header for a manual stock adjustment / physical verification event.</summary>
public class StockAdjustment : BranchEntity
{
    public string AdjustmentNumber { get; set; } = string.Empty;
    public DateTime AdjustmentDate { get; set; } = DateTime.UtcNow;
    public string? Reason { get; set; }
    public ICollection<StockAdjustmentItem> Items { get; set; } = new List<StockAdjustmentItem>();
}

public class StockAdjustmentItem : BaseEntity
{
    public int StockAdjustmentId { get; set; }
    public StockAdjustment? StockAdjustment { get; set; }

    public int MedicineId { get; set; }
    public Medicine? Medicine { get; set; }

    public int? MedicineBatchId { get; set; }
    public MedicineBatch? MedicineBatch { get; set; }

    public decimal SystemQuantity { get; set; }
    public decimal PhysicalQuantity { get; set; }
    public decimal Difference => PhysicalQuantity - SystemQuantity;
    public string? Remarks { get; set; }
}
