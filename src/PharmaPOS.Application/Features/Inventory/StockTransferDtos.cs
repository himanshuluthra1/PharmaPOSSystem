using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.Inventory;

public interface IStockTransferService
{
    Task<string> PreviewNextTransferNumberAsync(int? fromBranchId, CancellationToken ct = default);

    Task<List<StockTransferBranchOptionDto>> ListDestinationBranchesAsync(
        int? fromBranchId, CancellationToken ct = default);

    /// <summary>Deducts stock at this store and returns a package JSON for the other store to import.</summary>
    Task<Result<StockTransferReceiptDto>> CreateOutboundTransferAsync(
        CreateStockTransferRequest request,
        int? fromBranchId,
        int? userId,
        CancellationToken ct = default);

    Task<Result<string>> GetOutboundPackageJsonAsync(int transferId, int? branchId, CancellationToken ct = default);

    /// <summary>
    /// Cancels a sent or received transfer.
    /// Sent: restores stock here and returns a void file for the other store.
    /// Received: removes imported stock here and returns a reverse package so the sender can restore stock.
    /// </summary>
    Task<Result<StockTransferReceiptDto>> CancelTransferAsync(
        int transferId,
        int? branchId,
        string? reason,
        CancellationToken ct = default);

    /// <summary>Imports a package file from another store into this store's stock.</summary>
    Task<Result<StockTransferReceiptDto>> ImportPackageAsync(
        string packageJson,
        int? toBranchId,
        int? userId,
        CancellationToken ct = default);

    Task<List<StockTransferListRowDto>> ListRecentTransfersAsync(
        int? branchId, int take = 50, CancellationToken ct = default);

    Task<Result<StockTransferDetailDto>> GetTransferDetailsAsync(
        int transferId, int? branchId, CancellationToken ct = default);
}

public record StockTransferBranchOptionDto(int Id, string Code, string Name);

public sealed class CreateStockTransferRequest
{
    public DateTime TransferDate { get; set; } = DateTime.Today;
    public int ToBranchId { get; set; }
    public string? Remarks { get; set; }
    public List<StockTransferLineRequest> Lines { get; set; } = [];
}

public sealed class StockTransferLineRequest
{
    public int MedicineId { get; set; }
    public int SourceMedicineBatchId { get; set; }
    public decimal Quantity { get; set; }
}

public sealed class StockTransferReceiptDto
{
    public int TransferId { get; set; }
    public string TransferNumber { get; set; } = string.Empty;
    public DateTime TransferDate { get; set; }
    public string FromBranchName { get; set; } = string.Empty;
    public string ToBranchName { get; set; } = string.Empty;
    public int LinesTransferred { get; set; }
    public decimal TotalQuantity { get; set; }
    public string PackageKey { get; set; } = string.Empty;
    public string? PackageJson { get; set; }
    public string SuggestedFileName { get; set; } = string.Empty;
    public bool IsReturnPackage { get; set; }
}

public sealed class StockTransferListRowDto
{
    public int TransferId { get; set; }
    public string TransferNumber { get; set; } = string.Empty;
    public DateTime TransferDate { get; set; }
    public string FromBranchName { get; set; } = string.Empty;
    public string ToBranchName { get; set; } = string.Empty;
    public int LineCount { get; set; }
    public decimal TotalQuantity { get; set; }
    public string? Remarks { get; set; }
    public bool IsOutgoing { get; set; }
    public bool IsCancelled { get; set; }
    public string DirectionLabel => IsCancelled ? "Cancelled" : IsOutgoing ? "Sent" : "Received";
    public bool CanReExport { get; set; }
    public bool CanCancel { get; set; }
}

public sealed class StockTransferDetailDto
{
    public int TransferId { get; set; }
    public string TransferNumber { get; set; } = string.Empty;
    public DateTime TransferDate { get; set; }
    public string FromBranchName { get; set; } = string.Empty;
    public string ToBranchName { get; set; } = string.Empty;
    public string DirectionLabel { get; set; } = string.Empty;
    public string? Remarks { get; set; }
    public bool IsCancelled { get; set; }
    public List<StockTransferDetailLineDto> Lines { get; set; } = [];
}

public sealed class StockTransferDetailLineDto
{
    public string MedicineName { get; set; } = string.Empty;
    public string? MedicineBarcode { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public DateTime? ExpiryDate { get; set; }
    public string ExpiryDisplay => ExpiryDate?.ToString("MM/yyyy") ?? "—";
    public decimal Quantity { get; set; }
    public decimal Mrp { get; set; }
    public decimal PurchasePrice { get; set; }
}

