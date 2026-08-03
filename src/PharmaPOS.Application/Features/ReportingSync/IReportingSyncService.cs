namespace PharmaPOS.Application.Features.ReportingSync;

public static class ReportingSyncEntityTypes
{
    public const string Branch = "Branch";
    public const string Medicine = "Medicine";
    public const string MedicineBatch = "MedicineBatch";
    public const string Customer = "Customer";
    public const string Sale = "Sale";
    public const string SaleReturn = "SaleReturn";
    public const string Purchase = "Purchase";
    public const string PurchaseReturn = "PurchaseReturn";
    public const string StockMovement = "StockMovement";
    public const string StockTransfer = "StockTransfer";
}

public interface IReportingSyncGate
{
    bool IsEnabled { get; }
    string? StoreCodeOverride { get; }
}

public sealed class NullReportingSyncGate : IReportingSyncGate
{
    public bool IsEnabled => false;
    public string? StoreCodeOverride => null;
}

/// <summary>Enqueues entity snapshots for background upload to VPS MySQL.</summary>
public interface IReportingSyncService
{
    Task EnqueueBranchAsync(int branchId, CancellationToken ct = default);
    Task EnqueueMedicineAsync(int medicineId, CancellationToken ct = default);
    Task EnqueueMedicineBatchAsync(int batchId, CancellationToken ct = default);
    Task EnqueueCustomerAsync(int customerId, CancellationToken ct = default);
    Task EnqueueSaleAsync(int saleId, CancellationToken ct = default);
    Task EnqueueSaleReturnAsync(int saleReturnId, CancellationToken ct = default);
    Task EnqueuePurchaseAsync(int purchaseId, CancellationToken ct = default);
    Task EnqueuePurchaseReturnAsync(int purchaseReturnId, CancellationToken ct = default);
    Task EnqueueStockMovementAsync(int movementId, CancellationToken ct = default);
    Task EnqueueStockTransferAsync(int transferId, CancellationToken ct = default);
}

/// <summary>No-op sync used when gate is off or as a safe default.</summary>
public sealed class NullReportingSyncService : IReportingSyncService
{
    public Task EnqueueBranchAsync(int branchId, CancellationToken ct = default) => Task.CompletedTask;
    public Task EnqueueMedicineAsync(int medicineId, CancellationToken ct = default) => Task.CompletedTask;
    public Task EnqueueMedicineBatchAsync(int batchId, CancellationToken ct = default) => Task.CompletedTask;
    public Task EnqueueCustomerAsync(int customerId, CancellationToken ct = default) => Task.CompletedTask;
    public Task EnqueueSaleAsync(int saleId, CancellationToken ct = default) => Task.CompletedTask;
    public Task EnqueueSaleReturnAsync(int saleReturnId, CancellationToken ct = default) => Task.CompletedTask;
    public Task EnqueuePurchaseAsync(int purchaseId, CancellationToken ct = default) => Task.CompletedTask;
    public Task EnqueuePurchaseReturnAsync(int purchaseReturnId, CancellationToken ct = default) => Task.CompletedTask;
    public Task EnqueueStockMovementAsync(int movementId, CancellationToken ct = default) => Task.CompletedTask;
    public Task EnqueueStockTransferAsync(int transferId, CancellationToken ct = default) => Task.CompletedTask;
}
