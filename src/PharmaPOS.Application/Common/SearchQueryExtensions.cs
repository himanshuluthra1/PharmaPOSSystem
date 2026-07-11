using Microsoft.EntityFrameworkCore;
using PharmaPOS.Domain.Entities.Masters;

namespace PharmaPOS.Application.Common;

/// <summary>Space-insensitive search helpers for EF queries.</summary>
public static class SearchQueryExtensions
{
    /// <summary>Trims and removes spaces so "Para500" matches "Para 500".</summary>
    public static string NormalizeTerm(string? term)
        => (term ?? string.Empty).Trim().Replace(" ", string.Empty);

    public static IQueryable<Medicine> WhereMedicineMatches(
        this IQueryable<Medicine> query,
        string normalizedTerm,
        bool prefixOnly)
    {
        if (prefixOnly)
        {
            return query.Where(m =>
                EF.Functions.Like(m.NameSearchKey, normalizedTerm + "%") ||
                (m.BarcodeSearchKey != "" && m.BarcodeSearchKey == normalizedTerm) ||
                (m.GenericNameSearchKey != "" && EF.Functions.Like(m.GenericNameSearchKey, normalizedTerm + "%")));
        }

        return query.Where(m =>
            EF.Functions.Like(m.NameSearchKey, "%" + normalizedTerm + "%") ||
            (m.GenericNameSearchKey != "" && EF.Functions.Like(m.GenericNameSearchKey, "%" + normalizedTerm + "%")));
    }

    public static IQueryable<Supplier> WhereSupplierMatches(
        this IQueryable<Supplier> query,
        string normalizedTerm)
        => query.Where(s =>
            s.NameSearchKey.Contains(normalizedTerm) ||
            (s.PhoneSearchKey != "" && s.PhoneSearchKey.Contains(normalizedTerm)));
}
