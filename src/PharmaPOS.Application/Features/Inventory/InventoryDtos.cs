using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Application.Features.Inventory;

public enum StockFilterKind
{
    All,
    InStock,
    LowStock,
    NearExpiry,
    Expired,
    ZeroStock
}

public sealed class StockFilterOption(StockFilterKind kind, string label)
{
    public StockFilterKind Kind { get; } = kind;
    public string Label { get; } = label;
}

/// <summary>Inventory KPI summary for the stock on hand screen.</summary>
public class StockSummaryDto
{
    public int TotalMedicines { get; set; }
    public int TotalBatches { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal StockValue { get; set; }
    public int LowStockCount { get; set; }
    public int NearExpiryCount { get; set; }
    public int ExpiredCount { get; set; }
}

/// <summary>One batch row in the current stock grid.</summary>
public record StockBatchRowDto(
    int BatchId,
    int MedicineId,
    string MedicineName,
    string? GenericName,
    string BatchNumber,
    DateTime? ExpiryDate,
    decimal QuantityAvailable,
    decimal PurchasePrice,
    decimal Mrp,
    decimal SellingPrice,
    string? RackNumber,
    int ReorderLevel,
    decimal MedicineTotalQty,
    bool IsLowStock,
    bool IsNearExpiry,
    bool IsExpired)
{
    public string ExpiryLabel => ExpiryDate?.ToString("dd/MM/yyyy") ?? "—";
    public decimal StockValue => PurchasePrice * QuantityAvailable;
}

/// <summary>One row in the stock movement ledger.</summary>
public record StockLedgerRowDto(
    int MovementId,
    DateTime MovementDateUtc,
    StockMovementType MovementType,
    string MedicineName,
    string? BatchNumber,
    decimal Quantity,
    decimal BalanceAfter,
    decimal UnitCost,
    string? ReferenceNumber,
    string? Remarks)
{
    public string MovementDateLabel => MovementDateUtc.ToLocalTime().ToString("dd/MM/yyyy hh:mm tt");

    public string MovementTypeLabel => MovementType switch
    {
        StockMovementType.PurchaseIn => "Purchase In",
        StockMovementType.SaleOut => "Sale Out",
        StockMovementType.PurchaseReturn => "Purchase Return",
        StockMovementType.SaleReturn => "Sale Return",
        StockMovementType.AdjustmentIn => "Adjustment In",
        StockMovementType.AdjustmentOut => "Adjustment Out",
        StockMovementType.TransferIn => "Transfer In",
        StockMovementType.TransferOut => "Transfer Out",
        StockMovementType.Damage => "Damage",
        StockMovementType.Expiry => "Expiry",
        StockMovementType.OpeningStock => "Opening Stock",
        _ => MovementType.ToString()
    };

    public bool IsInbound => Quantity > 0;
}

public class StockAdjustmentLineRequest
{
    public int MedicineId { get; set; }
    public int MedicineBatchId { get; set; }
    public decimal SystemQuantity { get; set; }
    public decimal PhysicalQuantity { get; set; }
    public string? Remarks { get; set; }
}

public class CreateStockAdjustmentRequest
{
    public DateTime AdjustmentDate { get; set; } = DateTime.Today;
    public string? Reason { get; set; }
    public List<StockAdjustmentLineRequest> Lines { get; set; } = new();
}

public class StockAdjustmentReceiptDto
{
    public int AdjustmentId { get; set; }
    public string AdjustmentNumber { get; set; } = string.Empty;
    public DateTime AdjustmentDate { get; set; }
    public int LinesAdjusted { get; set; }
}
