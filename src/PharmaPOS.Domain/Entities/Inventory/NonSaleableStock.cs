using PharmaPOS.Domain.Common;
using PharmaPOS.Domain.Entities.Inventory;
using PharmaPOS.Domain.Entities.Masters;

namespace PharmaPOS.Domain.Entities.Inventory;

/// <summary>Non-saleable returned stock quarantined from regular batch inventory.</summary>
public class NonSaleableStock : BranchEntity
{
    public int MedicineId { get; set; }
    public Medicine? Medicine { get; set; }

    public int? MedicineBatchId { get; set; }
    public MedicineBatch? MedicineBatch { get; set; }

    public string? BatchNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }

    public decimal Quantity { get; set; }
    public int SaleReturnItemId { get; set; }
    public string? Remarks { get; set; }
    public DateTime ReceivedDateUtc { get; set; } = DateTime.UtcNow;
}
