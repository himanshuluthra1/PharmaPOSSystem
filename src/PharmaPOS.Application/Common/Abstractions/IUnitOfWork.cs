using PharmaPOS.Domain.Common;

namespace PharmaPOS.Application.Common.Abstractions;

/// <summary>
/// Coordinates work across repositories and commits it in a single transaction.
/// Retrieves repositories on demand so callers depend on one abstraction.
/// </summary>
public interface IUnitOfWork
{
    IRepository<T> Repository<T>() where T : BaseEntity;

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    Task BeginTransactionAsync(CancellationToken ct = default);
    Task CommitTransactionAsync(CancellationToken ct = default);
    Task RollbackTransactionAsync(CancellationToken ct = default);

    /// <summary>
    /// Runs <paramref name="operation"/> inside a database transaction using the
    /// provider's execution strategy, so it works even when connection resiliency
    /// (retry-on-failure) is enabled. The operation should perform its own
    /// SaveChanges as needed; the whole unit is committed on success and rolled
    /// back if the operation throws.
    /// </summary>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation, CancellationToken ct = default);
}
