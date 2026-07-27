using Microsoft.EntityFrameworkCore;
using PharmaPOS.Domain.Entities.Masters;

namespace PharmaPOS.Application.Common;

/// <summary>Space-insensitive search helpers for EF queries.</summary>
public static class SearchQueryExtensions
{
    /// <summary>Trims and removes spaces so "Para500" matches "Para 500".</summary>
    public static string NormalizeTerm(string? term)
        => (term ?? string.Empty).Trim().Replace(" ", string.Empty);

    /// <summary>
    /// Splits a user query into tokens (on spaces). Multi-token queries match when
    /// every token appears somewhere in the name (e.g. "Dettol LIQ" → "DETTOL 110ML LIQ").
    /// </summary>
    public static string[] GetSearchTokens(string? term)
        => (term ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 0)
            .ToArray();

    public static IQueryable<Medicine> WhereMedicineMatches(
        this IQueryable<Medicine> query,
        string normalizedTerm,
        bool prefixOnly)
        => WhereMedicineMatches(query, normalizedTerm, prefixOnly, tokens: null);

    /// <param name="tokens">
    /// Optional space-separated tokens from the original query. When more than one
    /// token is present, every token must appear in the name (or generic) key.
    /// </param>
    public static IQueryable<Medicine> WhereMedicineMatches(
        this IQueryable<Medicine> query,
        string normalizedTerm,
        bool prefixOnly,
        string[]? tokens)
    {
        if (tokens is { Length: > 1 })
        {
            foreach (var raw in tokens)
            {
                var token = NormalizeTerm(raw);
                if (token.Length == 0) continue;
                query = query.Where(m =>
                    EF.Functions.Like(m.NameSearchKey, "%" + token + "%") ||
                    (m.GenericNameSearchKey != "" && EF.Functions.Like(m.GenericNameSearchKey, "%" + token + "%")) ||
                    (m.BarcodeSearchKey != "" && m.BarcodeSearchKey == token));
            }

            return query;
        }

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
