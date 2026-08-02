using PharmaPOS.Domain.Common;
using PharmaPOS.Domain.Entities.Identity;
using PharmaPOS.Domain.Entities.Masters;
using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Domain.Entities.Inventory;

/// <summary>
/// Inter-store stock transfer for separate databases.
/// Outbound: stock leaves this store and a transfer package file is shared.
/// Inbound: package is imported and stock arrives on this store.
/// <see cref="BranchEntity.BranchId"/> is the local store that owns the document.
/// </summary>
public class StockTransfer : BranchEntity
{
    public string TransferNumber { get; set; } = string.Empty;
    public DateTime TransferDate { get; set; } = DateTime.UtcNow;
    public StockTransferKind Kind { get; set; } = StockTransferKind.Outbound;
    public StockTransferStatus Status { get; set; } = StockTransferStatus.Active;
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancelReason { get; set; }

    /// <summary>Partner store on this DB (destination for outbound, optional source for inbound).</summary>
    public int ToBranchId { get; set; }
    public Branch? ToBranch { get; set; }

    public string FromBranchCode { get; set; } = string.Empty;
    public string FromBranchName { get; set; } = string.Empty;
    public string ToBranchCode { get; set; } = string.Empty;
    public string ToBranchName { get; set; } = string.Empty;

    /// <summary>Unique key embedded in the export package (outbound).</summary>
    public string PackageKey { get; set; } = string.Empty;

    /// <summary>Original package key when this row is an inbound import (prevents double-import).</summary>
    public string? ExternalPackageKey { get; set; }

    public string? Remarks { get; set; }
    public int? TransferredByUserId { get; set; }

    public ICollection<StockTransferItem> Items { get; set; } = new List<StockTransferItem>();
}

public class StockTransferItem : BaseEntity
{
    public int StockTransferId { get; set; }
    public StockTransfer? StockTransfer { get; set; }

    public int MedicineId { get; set; }
    public Medicine? Medicine { get; set; }

    public int? SourceMedicineBatchId { get; set; }
    public MedicineBatch? SourceMedicineBatch { get; set; }

    public int? DestinationMedicineBatchId { get; set; }
    public MedicineBatch? DestinationMedicineBatch { get; set; }

    public string MedicineName { get; set; } = string.Empty;
    public string? MedicineBarcode { get; set; }

    public string BatchNumber { get; set; } = string.Empty;
    public DateTime? ExpiryDate { get; set; }
    public DateTime? ManufacturingDate { get; set; }

    public decimal Quantity { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal Mrp { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal GstPercent { get; set; }
    public string? RackNumber { get; set; }
}
