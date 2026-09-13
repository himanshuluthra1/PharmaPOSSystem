using System.Linq.Expressions;
using PharmaPOS.Shared;

namespace PharmaPOS.Application.Common;

/// <summary>EF-friendly date filters for the active Indian financial year.</summary>
public static class FinancialYearQueryExtensions
{
    public static IQueryable<T> WhereInFinancialYear<T>(
        this IQueryable<T> query,
        FinancialYearInfo fy,
        Expression<Func<T, DateTime>> dateSelector)
    {
        var start = fy.Start;
        var end = fy.EndExclusive;
        var param = dateSelector.Parameters[0];
        var body = Expression.AndAlso(
            Expression.GreaterThanOrEqual(dateSelector.Body, Expression.Constant(start)),
            Expression.LessThan(dateSelector.Body, Expression.Constant(end)));
        var lambda = Expression.Lambda<Func<T, bool>>(body, param);
        return query.Where(lambda);
    }

    public static IQueryable<T> WhereInFinancialYear<T>(
        this IQueryable<T> query,
        FinancialYearInfo fy,
        Expression<Func<T, DateTime?>> dateSelector)
    {
        var start = fy.Start;
        var end = fy.EndExclusive;
        var param = dateSelector.Parameters[0];
        var access = dateSelector.Body;
        var notNull = Expression.Property(access, nameof(Nullable<DateTime>.HasValue));
        var value = Expression.Property(access, nameof(Nullable<DateTime>.Value));
        var inRange = Expression.AndAlso(
            Expression.GreaterThanOrEqual(value, Expression.Constant(start)),
            Expression.LessThan(value, Expression.Constant(end)));
        var body = Expression.AndAlso(notNull, inRange);
        var lambda = Expression.Lambda<Func<T, bool>>(body, param);
        return query.Where(lambda);
    }
}
