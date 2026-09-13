using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaPOS.Application.Common;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Domain.Entities.Inventory;
using PharmaPOS.Domain.Entities.Masters;
using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Application.Features.Sales;

/// <summary>
/// In-memory medicine catalogue for typeahead. Warm once after login; search is CPU-only.
/// </summary>
public interface IMedicineSearchIndex
{
    bool IsWarm { get; }

    Task EnsureWarmAsync(CancellationToken ct = default);

    /// <summary>Prefix-first then contains, matching billing search rules. Stock is always 0.</summary>
    IReadOnlyList<MedicineLookupDto> Search(string term, int take = 25);

    Task<Dictionary<int, decimal>> GetStockByMedicineIdsAsync(
        IReadOnlyList<int> medicineIds, int? branchId, CancellationToken ct = default);

    /// <summary>Reload one medicine after create/update (or remove if inactive/deleted).</summary>
    Task UpsertAsync(int medicineId, CancellationToken ct = default);

    void Remove(int medicineId);

    void Invalidate();
}

public sealed class MedicineSearchIndex : IMedicineSearchIndex
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile MedicineSearchEntry[] _entries = [];
    private volatile Dictionary<int, MedicineSearchEntry> _byId = new();
    private volatile bool _warm;

    public MedicineSearchIndex(IServiceScopeFactory scopeFactory)
        => _scopeFactory = scopeFactory;

    public bool IsWarm => _warm;

    public async Task EnsureWarmAsync(CancellationToken ct = default)
    {
        if (_warm) return;

        await _gate.WaitAsync(ct);
        try
        {
            if (_warm) return;
            using var scope = _scopeFactory.CreateScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var rows = await LoadAllAsync(uow, ct);
            ReplaceAll(rows);
            _warm = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public IReadOnlyList<MedicineLookupDto> Search(string term, int take = 25)
    {
        var normalized = SearchQueryExtensions.NormalizeTerm(term);
        if (normalized.Length < 2 || take <= 0) return [];

        var tokens = SearchQueryExtensions.GetSearchTokens(term);
        var snapshot = _entries;
        if (snapshot.Length == 0) return [];

        List<MedicineSearchEntry> hits;
        if (tokens.Length <= 1)
        {
            hits = MatchPrefix(snapshot, normalized, take);
            if (hits.Count == 0)
                hits = MatchContains(snapshot, normalized, tokens, take);
        }
        else
        {
            hits = MatchContains(snapshot, normalized, tokens, take);
        }

        return hits.Select(ToLookup).ToList();
    }

    public async Task<Dictionary<int, decimal>> GetStockByMedicineIdsAsync(
        IReadOnlyList<int> medicineIds, int? branchId, CancellationToken ct = default)
    {
        if (medicineIds.Count == 0) return new Dictionary<int, decimal>();

        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var ids = medicineIds as List<int> ?? medicineIds.ToList();

        var q = uow.Repository<MedicineBatch>().Query().AsNoTracking()
            .Where(b => ids.Contains(b.MedicineId) && b.QuantityAvailable > 0);
        if (branchId.HasValue) q = q.Where(b => b.BranchId == branchId);

        return await q
            .GroupBy(b => b.MedicineId)
            .Select(g => new { MedicineId = g.Key, Stock = g.Sum(x => x.QuantityAvailable) })
            .ToDictionaryAsync(x => x.MedicineId, x => x.Stock, ct);
    }

    public async Task UpsertAsync(int medicineId, CancellationToken ct = default)
    {
        if (medicineId <= 0) return;

        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var row = await uow.Repository<Medicine>().QueryIncludingDeleted().AsNoTracking()
            .Where(m => m.Id == medicineId)
            .Select(m => new MedicineSearchEntry(
                m.Id, m.Name, m.NameSearchKey, m.GenericName, m.GenericNameSearchKey,
                m.Barcode, m.BarcodeSearchKey, m.GstPercent, m.DefaultDiscountPercent,
                m.PrescriptionRequired, m.RackNumber, m.BinNumber, m.Brand, m.ScheduleType,
                m.PackInfo ?? (m.UnitsPerPack > 1 ? $"x{m.UnitsPerPack}" : null),
                m.PurchasePrice, m.HsnCode, m.Mrp, m.Status, m.IsDeleted))
            .FirstOrDefaultAsync(ct);

        await _gate.WaitAsync(ct);
        try
        {
            var map = new Dictionary<int, MedicineSearchEntry>(_byId);
            if (row is null || row.IsDeleted || row.Status != EntityStatus.Active)
            {
                map.Remove(medicineId);
            }
            else
            {
                map[medicineId] = row;
            }

            ReplaceAll(map.Values.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList());
            _warm = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Remove(int medicineId)
    {
        if (medicineId <= 0) return;

        _gate.Wait();
        try
        {
            if (!_byId.ContainsKey(medicineId)) return;
            var map = new Dictionary<int, MedicineSearchEntry>(_byId);
            map.Remove(medicineId);
            ReplaceAll(map.Values.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList());
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate()
    {
        _warm = false;
        _entries = [];
        _byId = new Dictionary<int, MedicineSearchEntry>();
    }

    private void ReplaceAll(IReadOnlyList<MedicineSearchEntry> rows)
    {
        _entries = rows.ToArray();
        _byId = rows.ToDictionary(r => r.Id);
    }

    private static async Task<List<MedicineSearchEntry>> LoadAllAsync(IUnitOfWork uow, CancellationToken ct)
        => await uow.Repository<Medicine>().Query().AsNoTracking()
            .Where(m => m.Status == EntityStatus.Active)
            .OrderBy(m => m.Name)
            .Select(m => new MedicineSearchEntry(
                m.Id, m.Name, m.NameSearchKey, m.GenericName, m.GenericNameSearchKey,
                m.Barcode, m.BarcodeSearchKey, m.GstPercent, m.DefaultDiscountPercent,
                m.PrescriptionRequired, m.RackNumber, m.BinNumber, m.Brand, m.ScheduleType,
                m.PackInfo ?? (m.UnitsPerPack > 1 ? $"x{m.UnitsPerPack}" : null),
                m.PurchasePrice, m.HsnCode, m.Mrp, m.Status, m.IsDeleted))
            .ToListAsync(ct);

    private static List<MedicineSearchEntry> MatchPrefix(
        MedicineSearchEntry[] snapshot, string normalized, int take)
    {
        var hits = new List<MedicineSearchEntry>(Math.Min(take, 32));
        foreach (var m in snapshot)
        {
            if (hits.Count >= take) break;
            if (IsPrefixMatch(m, normalized))
                hits.Add(m);
        }

        return hits;
    }

    private static List<MedicineSearchEntry> MatchContains(
        MedicineSearchEntry[] snapshot, string normalized, string[] tokens, int take)
    {
        var hits = new List<MedicineSearchEntry>(Math.Min(take, 32));
        foreach (var m in snapshot)
        {
            if (hits.Count >= take) break;
            if (tokens.Length > 1)
            {
                if (MatchesAllTokens(m, tokens))
                    hits.Add(m);
            }
            else if (IsContainsMatch(m, normalized))
            {
                hits.Add(m);
            }
        }

        return hits;
    }

    private static bool IsPrefixMatch(MedicineSearchEntry m, string normalized)
        => StartsWithOrdinal(m.NameSearchKey, normalized)
           || (!string.IsNullOrEmpty(m.BarcodeSearchKey) && string.Equals(m.BarcodeSearchKey, normalized, StringComparison.OrdinalIgnoreCase))
           || (!string.IsNullOrEmpty(m.GenericNameSearchKey) && StartsWithOrdinal(m.GenericNameSearchKey, normalized))
           || ContainsIgnoreCase(m.RackNumber, normalized)
           || ContainsIgnoreCase(m.BinNumber, normalized);

    private static bool IsContainsMatch(MedicineSearchEntry m, string normalized)
        => ContainsOrdinal(m.NameSearchKey, normalized)
           || (!string.IsNullOrEmpty(m.GenericNameSearchKey) && ContainsOrdinal(m.GenericNameSearchKey, normalized))
           || ContainsIgnoreCase(m.RackNumber, normalized)
           || ContainsIgnoreCase(m.BinNumber, normalized);

    private static bool MatchesAllTokens(MedicineSearchEntry m, string[] tokens)
    {
        foreach (var raw in tokens)
        {
            var token = SearchQueryExtensions.NormalizeTerm(raw);
            if (token.Length == 0) continue;
            var ok = ContainsOrdinal(m.NameSearchKey, token)
                     || (!string.IsNullOrEmpty(m.GenericNameSearchKey) && ContainsOrdinal(m.GenericNameSearchKey, token))
                     || (!string.IsNullOrEmpty(m.BarcodeSearchKey)
                         && string.Equals(m.BarcodeSearchKey, token, StringComparison.OrdinalIgnoreCase))
                     || ContainsIgnoreCase(m.RackNumber, token)
                     || ContainsIgnoreCase(m.BinNumber, token);
            if (!ok) return false;
        }

        return true;
    }

    private static bool StartsWithOrdinal(string? haystack, string needle)
        => !string.IsNullOrEmpty(haystack)
           && haystack.StartsWith(needle, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsOrdinal(string? haystack, string needle)
        => !string.IsNullOrEmpty(haystack)
           && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsIgnoreCase(string? haystack, string needle)
        => !string.IsNullOrEmpty(haystack)
           && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static MedicineLookupDto ToLookup(MedicineSearchEntry m)
        => new(
            m.Id, m.Name, m.GenericName, m.Barcode,
            m.GstPercent, m.DefaultDiscountPercent, m.PrescriptionRequired,
            TotalStock: 0m,
            m.RackNumber, m.BinNumber, m.Brand, m.ScheduleType,
            m.PackLabel, m.Cost, m.HsnCode, m.Mrp);

    private sealed record MedicineSearchEntry(
        int Id,
        string Name,
        string NameSearchKey,
        string? GenericName,
        string GenericNameSearchKey,
        string? Barcode,
        string BarcodeSearchKey,
        decimal GstPercent,
        decimal DefaultDiscountPercent,
        bool PrescriptionRequired,
        string? RackNumber,
        string? BinNumber,
        string? Brand,
        ScheduleDrugType ScheduleType,
        string? PackLabel,
        decimal Cost,
        string? HsnCode,
        decimal Mrp,
        EntityStatus Status,
        bool IsDeleted);
}
