using System.Linq.Expressions;
using PharmaPOS.Domain.Common;

namespace PharmaPOS.Application.Common.Abstractions;

/// <summary>
/// Generic repository abstraction over an aggregate/entity type. Exposes a
/// composable <see cref="Query"/> for read scenarios plus basic write operations.
/// Persistence is committed via <see cref="IUnitOfWork.SaveChangesAsync"/>.
/// </summary>
public interface IRepository<T> where T : BaseEntity
{
    /// <summary>Composable, no-tracking-agnostic query root for advanced reads.</summary>
    IQueryable<T> Query();

    /// <summary>Query root that includes soft-deleted rows.</summary>
    IQueryable<T> QueryIncludingDeleted();

    Task<T?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<List<T>> ListAsync(CancellationToken ct = default);
    Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);
    Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    Task AddAsync(T entity, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default);
    void Update(T entity);
    void Remove(T entity);
}
