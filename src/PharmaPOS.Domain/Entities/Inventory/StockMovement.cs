using PharmaPOS.Domain.Common;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Domain.Entities.Masters;

namespace PharmaPOS.Domain.Entities.Inventory;

/// <summary>
/// Immutable stock ledger entry. Every quantity change (purchase, sale, return,
/// adjustment, transfer, damage, expiry) creates one movement, enabling a full
/// audit trail and stock valuation.
/// </summary>
public class StockMovement : BranchEntity
{
    public int MedicineId { get; set; }
    public Medicine? Medicine { get; set; }

    public int? MedicineBatchId { get; set; }
    public MedicineBatch? MedicineBatch { get; set; }

    public StockMovementType MovementType { get; set; }
    public decimal Quantity { get; set; }
    public decimal BalanceAfter { get; set; }
    public decimal UnitCost { get; set; }

    public string? ReferenceType { get; set; }
    public int? ReferenceId { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Remarks { get; set; }
    public DateTime MovementDateUtc { get; set; } = DateTime.UtcNow;
}
