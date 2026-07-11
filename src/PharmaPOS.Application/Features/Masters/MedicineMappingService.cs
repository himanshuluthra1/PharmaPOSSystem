using Microsoft.EntityFrameworkCore;
using PharmaPOS.Application.Common;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Domain.Entities.Inventory;
using PharmaPOS.Domain.Entities.Masters;
using PharmaPOS.Domain.Entities.Purchases;
using PharmaPOS.Domain.Entities.Sales;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.Masters;

public class MedicineMappingService : IMedicineMappingService
{
    private const int DefaultTake = 100;
    private const int UnmatchedPageSize = 500;
    private const int UnmatchedSearchPageSize = 2_000;

    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _clock;
    private readonly ILinkedMedWinIdCache _linkedMedWinIdCache;

    public MedicineMappingService(
        IUnitOfWork uow, IDateTimeProvider clock, ILinkedMedWinIdCache linkedMedWinIdCache)
    {
        _uow = uow;
        _clock = clock;
        _linkedMedWinIdCache = linkedMedWinIdCache;
    }

    public async Task<List<MedicineMappingListItemDto>> ListUnmatchedMedWinMedicinesAsync(
        string? filterTerm, CancellationToken ct = default)
    {
        filterTerm = (filterTerm ?? string.Empty).Trim();
        var pageSize = filterTerm.Length > 0 ? UnmatchedSearchPageSize : UnmatchedPageSize;
        var fetchSize = Math.Min(pageSize + 200, 10_000);

        var query = _uow.Repository<Medicine>().Query().AsNoTracking()
            .Where(m => m.Status == EntityStatus.Active
                        && m.Notes != null
                        && EF.Functions.Like(m.Notes!, "MedWinId:%")
                        && !EF.Functions.Like(m.Notes!, "%|%"));

        if (filterTerm.Length > 0)
        {
            var normalized = SearchQueryExtensions.NormalizeTerm(filterTerm);
            query = query.WhereMedicineMatches(normalized, prefixOnly: false);
        }

        var linkedMedWinIds = await _linkedMedWinIdCache.GetAsync(ct);
        var rows = await query.OrderBy(m => m.Name).Take(fetchSize)
            .Select(m => new { m.Id, m.Name, m.GenericName, m.PackInfo, m.Notes })
            .ToListAsync(ct);

        return rows
            .Where(m => MedicineNotesHelper.IsPendingMedWinMap(m.Notes, linkedMedWinIds))
            .Take(pageSize)
            .Select(m =>
            {
                var medWinId = MedicineNotesHelper.ParseMedWinId(m.Notes);
                return ToDto(m.Id, m.Name, m.GenericName, m.PackInfo, medWinId?.ToString(), false);
            }).ToList();
    }

    public async Task<List<MedicineMappingListItemDto>> SearchOneMgForMedWinAsync(
        int medWinMedicineId, string? additionalTerm, bool includeMatched, CancellationToken ct = default)
    {
        var medWin = await _uow.Repository<Medicine>().Query().AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == medWinMedicineId && m.Status == EntityStatus.Active, ct);
        if (medWin is null) return [];

        var brandPrefix = MedicineMappingHelper.GetFirstWordPrefix(medWin.Name);
        if (brandPrefix is null) return [];

        return await SearchOneMgByBrandPrefixAsync(brandPrefix, additionalTerm, includeMatched, ct);
    }

    public async Task<List<MedicineMappingListItemDto>> SearchOneMgByBrandPrefixAsync(
        string brandPrefix, string? additionalTerm, bool includeMatched, CancellationToken ct = default)
    {
        brandPrefix = SearchQueryExtensions.NormalizeTerm(brandPrefix).ToUpperInvariant();
        if (brandPrefix.Length < 2) return [];

        var likePattern = brandPrefix + "%";
        var query = _uow.Repository<Medicine>().Query().AsNoTracking()
            .Where(m => m.Status == EntityStatus.Active
                        && m.Notes != null
                        && EF.Functions.Like(m.Notes, "%OneMG-ID:%")
                        && EF.Functions.Like(m.NameSearchKey, likePattern));

        if (!includeMatched)
        {
            query = query.Where(m => !_uow.Repository<MedicineMedWinMapping>().Query()
                .Any(x => x.OneMgMedicineId == m.Id));
        }

        additionalTerm = (additionalTerm ?? string.Empty).Trim();
        if (additionalTerm.Length > 0)
        {
            var normalized = SearchQueryExtensions.NormalizeTerm(additionalTerm);
            query = query.WhereMedicineMatches(normalized, prefixOnly: true);
        }

        var rows = await query.OrderBy(m => m.Name).Take(DefaultTake)
            .Select(m => new { m.Id, m.Name, m.GenericName, m.PackInfo, m.Notes })
            .ToListAsync(ct);

        var mappedOneMgIds = await GetMappedOneMgIdsAsync(rows.Select(r => r.Id), ct);

        return rows.Select(m => ToDto(
            m.Id, m.Name, m.GenericName, m.PackInfo,
            MedicineNotesHelper.ParseOneMgId(m.Notes),
            mappedOneMgIds.Contains(m.Id))).ToList();
    }

    public async Task<List<MedicineMappingListItemDto>> SearchOneMgMedicinesAsync(
        string term, bool includeMatched, CancellationToken ct = default)
    {
        term = (term ?? string.Empty).Trim();
        if (term.Length < 2) return [];

        var normalized = SearchQueryExtensions.NormalizeTerm(term);
        var query = _uow.Repository<Medicine>().Query().AsNoTracking()
            .Where(m => m.Status == EntityStatus.Active
                        && m.Notes != null
                        && EF.Functions.Like(m.Notes, "%OneMG-ID:%"));

        if (!includeMatched)
        {
            query = query.Where(m => !_uow.Repository<MedicineMedWinMapping>().Query()
                .Any(x => x.OneMgMedicineId == m.Id));
        }

        query = query.WhereMedicineMatches(normalized, prefixOnly: false);

        var rows = await query.OrderBy(m => m.Name).Take(DefaultTake)
            .Select(m => new { m.Id, m.Name, m.GenericName, m.PackInfo, m.Notes })
            .ToListAsync(ct);

        var mappedOneMgIds = await GetMappedOneMgIdsAsync(rows.Select(r => r.Id), ct);

        return rows.Select(m => ToDto(
            m.Id, m.Name, m.GenericName, m.PackInfo,
            MedicineNotesHelper.ParseOneMgId(m.Notes),
            mappedOneMgIds.Contains(m.Id))).ToList();
    }

    public async Task<List<MedicineMappingListItemDto>> SearchMedWinMedicinesAsync(
        string term, bool includeMatched, CancellationToken ct = default)
    {
        term = (term ?? string.Empty).Trim();
        if (term.Length < 2) return [];

        var normalized = SearchQueryExtensions.NormalizeTerm(term);
        var query = _uow.Repository<Medicine>().Query().AsNoTracking()
            .Where(m => m.Status == EntityStatus.Active
                        && m.Notes != null
                        && EF.Functions.Like(m.Notes, "%MedWinId:%"));

        if (!includeMatched)
        {
            query = query.Where(m =>
                EF.Functions.Like(m.Notes!, "MedWinId:%")
                && !EF.Functions.Like(m.Notes!, "%|%"));
        }

        query = query.WhereMedicineMatches(normalized, prefixOnly: false);

        var linkedMedWinIds = !includeMatched
            ? await _linkedMedWinIdCache.GetAsync(ct)
            : null;

        var rows = await query.OrderBy(m => m.Name).Take(DefaultTake)
            .Select(m => new { m.Id, m.Name, m.GenericName, m.PackInfo, m.Notes })
            .ToListAsync(ct);

        return rows
            .Where(m => includeMatched || MedicineNotesHelper.IsPendingMedWinMap(m.Notes, linkedMedWinIds!))
            .Select(m =>
            {
                var medWinId = MedicineNotesHelper.ParseMedWinId(m.Notes);
                return ToDto(
                    m.Id, m.Name, m.GenericName, m.PackInfo,
                    medWinId?.ToString(),
                    MedicineNotesHelper.HasOneMgLink(m.Notes));
            }).ToList();
    }

    public async Task<List<MedicineMedWinMappingDto>> ListAppliedMappingsAsync(
        string? filterTerm, int take = 500, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 5_000);
        filterTerm = (filterTerm ?? string.Empty).Trim();

        var query = _uow.Repository<MedicineMedWinMapping>().Query().AsNoTracking();
        if (filterTerm.Length > 0)
        {
            query = query.Where(m =>
                EF.Functions.Like(m.MedWinMedicineName, $"%{filterTerm}%")
                || EF.Functions.Like(m.OneMgMedicineName, $"%{filterTerm}%")
                || m.MedWinId.ToString() == filterTerm
                || (m.OneMgCatalogId != null && m.OneMgCatalogId.Contains(filterTerm)));
        }

        var rows = await query
            .OrderByDescending(m => m.CreatedAtUtc)
            .ThenBy(m => m.MedWinMedicineName)
            .Take(take)
            .Select(m => new
            {
                m.Id,
                m.OneMgMedicineId,
                m.OneMgMedicineName,
                m.OneMgCatalogId,
                m.MedWinMedicineId,
                m.MedWinId,
                m.MedWinMedicineName,
                m.CreatedAtUtc
            })
            .ToListAsync(ct);

        var nameLookup = await BuildMedWinNameLookupAsync(
            rows.Select(r => (r.MedWinMedicineId, r.MedWinId)).ToList(), ct);

        return rows.Select(m => new MedicineMedWinMappingDto(
            m.Id,
            m.OneMgMedicineId,
            m.OneMgMedicineName,
            m.OneMgCatalogId,
            m.MedWinMedicineId,
            m.MedWinId,
            ResolveMedWinDisplayName(m.MedWinMedicineName, m.MedWinMedicineId, m.MedWinId, nameLookup),
            m.CreatedAtUtc)).ToList();
    }

    public async Task<AutoMapSuggestionResult> SuggestAutoMappingsAsync(
        bool includeMatched,
        IReadOnlySet<int>? excludeMedWinMedicineIds = null,
        CancellationToken ct = default)
    {
        excludeMedWinMedicineIds ??= new HashSet<int>();

        var linkedMedWinIds = await _linkedMedWinIdCache.GetAsync(ct);
        var medWinRows = await _uow.Repository<Medicine>().Query().AsNoTracking()
            .Where(m => m.Status == EntityStatus.Active
                        && m.Notes != null
                        && EF.Functions.Like(m.Notes!, "MedWinId:%")
                        && !EF.Functions.Like(m.Notes!, "%|%"))
            .OrderBy(m => m.Name)
            .Select(m => new { m.Id, m.Name, m.GenericName, m.Notes })
            .ToListAsync(ct);

        var pendingOrphans = medWinRows
            .Where(m => MedicineNotesHelper.IsPendingMedWinMap(m.Notes, linkedMedWinIds))
            .ToList();

        var unmatched = pendingOrphans
            .Where(m => !excludeMedWinMedicineIds.Contains(m.Id))
            .Select(m => new MedicineMappingListItemDto(
                m.Id,
                m.Name,
                m.GenericName,
                MedicineNotesHelper.ExtractPackInfo(m.Notes),
                MedicineNotesHelper.ParseMedWinId(m.Notes)?.ToString(),
                false))
            .ToList();

        var suggestions = new List<AutoMapSuggestion>();
        var skippedNoPrefix = 0;
        var skippedNoCandidates = 0;
        var skippedAmbiguous = 0;
        var skippedAlreadyQueued = pendingOrphans.Count - unmatched.Count;

        var groups = unmatched
            .Select(m => new { MedWin = m, Prefix = MedicineMappingHelper.GetFirstWordPrefix(m.Name) })
            .GroupBy(x => x.Prefix)
            .ToList();

        foreach (var group in groups)
        {
            if (string.IsNullOrWhiteSpace(group.Key))
            {
                skippedNoPrefix += group.Count();
                continue;
            }

            var candidates = await SearchOneMgByBrandPrefixAsync(group.Key!, null, includeMatched, ct);
            foreach (var entry in group)
            {
                var best = MedicineMappingHelper.PickBestOneMgMatch(
                    entry.MedWin.Name, entry.MedWin.GenericName, candidates);

                if (best is null)
                {
                    if (candidates.Count == 0)
                        skippedNoCandidates++;
                    else
                        skippedAmbiguous++;
                    continue;
                }

                suggestions.Add(new AutoMapSuggestion(
                    entry.MedWin.Id,
                    entry.MedWin.Name,
                    entry.MedWin.ExternalId,
                    best.Id,
                    best.Name,
                    best.ExternalId,
                    best.PackInfo));
            }
        }

        return new AutoMapSuggestionResult(
            suggestions,
            unmatched.Count,
            skippedNoPrefix,
            skippedNoCandidates,
            skippedAmbiguous,
            excludeMedWinMedicineIds.Count);
    }

    public async Task<Result> MapMedWinToOneMgAsync(
        int oneMgMedicineId, int medWinMedicineId, CancellationToken ct = default)
    {
        var result = await TryMapMedWinToOneMgAsync(oneMgMedicineId, medWinMedicineId, ct);
        if (result.IsFailure) return result;

        await _uow.SaveChangesAsync(ct);
        _linkedMedWinIdCache.Invalidate();
        return Result.Success();
    }

    public async Task<MedicineMappingBatchResult> MapMedWinToOneMgBatchAsync(
        IReadOnlyList<MedicineMappingPair> mappings, CancellationToken ct = default)
    {
        if (mappings.Count == 0)
            return new MedicineMappingBatchResult(0, []);

        try
        {
            var result = await _uow.ExecuteInTransactionAsync(async innerCt =>
            {
                var errors = new List<MedicineMappingBatchError>();
                foreach (var pair in mappings)
                {
                    var mapResult = await TryMapMedWinToOneMgAsync(
                        pair.OneMgMedicineId, pair.MedWinMedicineId, innerCt);
                    if (mapResult.IsFailure)
                    {
                        errors.Add(new MedicineMappingBatchError(
                            pair.MedWinMedicineId, pair.OneMgMedicineId,
                            mapResult.Error ?? "Mapping failed."));
                    }
                }

                if (errors.Count > 0)
                    throw new MedicineMappingBatchException(errors);

                return new MedicineMappingBatchResult(mappings.Count, []);
            }, ct);

            _linkedMedWinIdCache.Invalidate();
            return result;
        }
        catch (MedicineMappingBatchException ex)
        {
            return new MedicineMappingBatchResult(0, ex.Errors);
        }
        catch (Exception ex)
        {
            return new MedicineMappingBatchResult(0,
                [new MedicineMappingBatchError(0, 0, ex.Message)]);
        }
    }

    private sealed class MedicineMappingBatchException : Exception
    {
        public MedicineMappingBatchException(IReadOnlyList<MedicineMappingBatchError> errors)
            : base("One or more mappings failed validation.")
        {
            Errors = errors;
        }

        public IReadOnlyList<MedicineMappingBatchError> Errors { get; }
    }

    private async Task<Result> TryMapMedWinToOneMgAsync(
        int oneMgMedicineId, int medWinMedicineId, CancellationToken ct)
    {
        if (oneMgMedicineId <= 0 || medWinMedicineId <= 0)
            return Result.Failure("Select both an OneMG and a MedWin medicine.");

        if (oneMgMedicineId == medWinMedicineId)
            return Result.Failure("Select different medicines on each side.");

        var oneMg = await _uow.Repository<Medicine>().GetByIdAsync(oneMgMedicineId, ct);
        var medWin = await _uow.Repository<Medicine>().GetByIdAsync(medWinMedicineId, ct);

        if (oneMg is null || oneMg.IsDeleted)
            return Result.Failure("OneMG medicine not found.");
        if (medWin is null || medWin.IsDeleted)
            return Result.Failure("MedWin medicine not found.");

        if (!MedicineNotesHelper.HasOneMgLink(oneMg.Notes))
            return Result.Failure("The selected OneMG medicine is not a catalogue entry.");

        var medWinId = MedicineNotesHelper.ParseMedWinId(medWin.Notes);
        if (medWinId is null or <= 0)
            return Result.Failure("The selected MedWin medicine does not have a MedWin ID.");

        if (!MedicineNotesHelper.IsMedWinOnlyOrphan(medWin.Notes))
            return Result.Failure("The selected MedWin medicine is already linked.");

        var existingMapping = await _uow.Repository<MedicineMedWinMapping>().Query()
            .FirstOrDefaultAsync(m => m.MedWinId == medWinId.Value, ct);
        if (existingMapping is not null && existingMapping.OneMgMedicineId != oneMg.Id)
        {
            return Result.Failure(
                $"MedWin ID {medWinId} is already mapped to OneMG medicine \"{existingMapping.OneMgMedicineName}\".");
        }

        var legacyConflict = await AnyLegacyMedWinIdConflictAsync(
            oneMg.Id, medWin.Id, medWinId.Value, ct);
        if (legacyConflict)
            return Result.Failure($"MedWin ID {medWinId} is already linked to another medicine.");

        if (!MedicineNotesHelper.NotesContainsMedWinId(oneMg.Notes, medWinId.Value))
            oneMg.Notes = MedicineNotesHelper.AppendMedWinNote(oneMg.Notes, medWinId.Value);
        if (medWin.Mrp > 0) oneMg.Mrp = medWin.Mrp;
        if (medWin.PurchasePrice > 0) oneMg.PurchasePrice = medWin.PurchasePrice;
        if (medWin.SellingPrice > 0) oneMg.SellingPrice = medWin.SellingPrice;
        if (string.IsNullOrWhiteSpace(oneMg.RackNumber) && !string.IsNullOrWhiteSpace(medWin.RackNumber))
            oneMg.RackNumber = medWin.RackNumber;

        await MigrateMedicineReferencesAsync(medWin.Id, oneMg.Id, ct);

        if (existingMapping is null)
        {
            await _uow.Repository<MedicineMedWinMapping>().AddAsync(new MedicineMedWinMapping
            {
                OneMgMedicineId = oneMg.Id,
                MedWinMedicineId = medWin.Id,
                MedWinId = medWinId.Value,
                MedWinMedicineName = medWin.Name,
                OneMgMedicineName = oneMg.Name,
                OneMgCatalogId = MedicineNotesHelper.ParseOneMgId(oneMg.Notes),
            }, ct);
        }

        medWin.IsDeleted = true;
        medWin.DeletedAtUtc = _clock.UtcNow;
        medWin.Status = EntityStatus.Inactive;

        _uow.Repository<Medicine>().Update(oneMg);
        _uow.Repository<Medicine>().Update(medWin);
        return Result.Success();
    }

    private async Task MigrateMedicineReferencesAsync(int fromMedicineId, int toMedicineId, CancellationToken ct)
    {
        await _uow.Repository<MedicineBatch>().Query()
            .Where(b => b.MedicineId == fromMedicineId)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.MedicineId, toMedicineId), ct);

        await _uow.Repository<SaleItem>().Query()
            .Where(i => i.MedicineId == fromMedicineId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.MedicineId, toMedicineId), ct);

        await _uow.Repository<PurchaseItem>().Query()
            .Where(i => i.MedicineId == fromMedicineId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.MedicineId, toMedicineId), ct);

        await _uow.Repository<PurchaseOrderItem>().Query()
            .Where(i => i.MedicineId == fromMedicineId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.MedicineId, toMedicineId), ct);

        await _uow.Repository<StockAdjustmentItem>().Query()
            .Where(i => i.MedicineId == fromMedicineId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.MedicineId, toMedicineId), ct);

        await _uow.Repository<StockMovement>().Query()
            .Where(i => i.MedicineId == fromMedicineId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.MedicineId, toMedicineId), ct);
    }

    private async Task<bool> AnyLegacyMedWinIdConflictAsync(
        int oneMgMedicineId, int medWinMedicineId, int medWinId, CancellationToken ct)
    {
        var inMappingTable = await _uow.Repository<MedicineMedWinMapping>().Query()
            .AnyAsync(m => m.MedWinId == medWinId && m.OneMgMedicineId != oneMgMedicineId, ct);
        if (inMappingTable) return true;

        var tag = MedicineNotesHelper.MedWinNote(medWinId);
        return await _uow.Repository<Medicine>().Query()
            .AnyAsync(m => m.Id != oneMgMedicineId
                           && m.Id != medWinMedicineId
                           && !m.IsDeleted
                           && m.Notes != null
                           && (m.Notes == tag
                               || EF.Functions.Like(m.Notes!, tag + " |%")
                               || EF.Functions.Like(m.Notes!, "%| " + tag + " |%")
                               || EF.Functions.Like(m.Notes!, "%| " + tag)), ct);
    }

    private async Task<HashSet<int>> GetMappedOneMgIdsAsync(IEnumerable<int> oneMgMedicineIds, CancellationToken ct)
    {
        var ids = oneMgMedicineIds.Distinct().ToList();
        if (ids.Count == 0) return [];

        var mapped = await _uow.Repository<MedicineMedWinMapping>().Query().AsNoTracking()
            .Where(m => ids.Contains(m.OneMgMedicineId))
            .Select(m => m.OneMgMedicineId)
            .Distinct()
            .ToListAsync(ct);

        return mapped.ToHashSet();
    }

    private static MedicineMappingListItemDto ToDto(
        int id, string name, string? genericName, string? packInfo, string? externalId, bool isMatched)
        => new(id, name, genericName, packInfo, externalId, isMatched);

    private static bool IsPlaceholderMedWinName(string? name)
        => !string.IsNullOrWhiteSpace(name)
           && name.StartsWith("MedWinId:", StringComparison.OrdinalIgnoreCase);

    private static string ResolveMedWinDisplayName(
        string storedName,
        int? medWinMedicineId,
        int medWinId,
        MedWinNameLookup lookup)
    {
        if (!IsPlaceholderMedWinName(storedName))
            return storedName;

        if (medWinMedicineId is int medicineId
            && lookup.ByMedicineId.TryGetValue(medicineId, out var byMedicineId))
            return byMedicineId;

        if (lookup.ByMedWinId.TryGetValue(medWinId, out var byMedWinId))
            return byMedWinId;

        return storedName;
    }

    private sealed class MedWinNameLookup
    {
        public Dictionary<int, string> ByMedicineId { get; init; } = new();
        public Dictionary<int, string> ByMedWinId { get; init; } = new();
    }

    private async Task<MedWinNameLookup> BuildMedWinNameLookupAsync(
        IReadOnlyList<(int? MedWinMedicineId, int MedWinId)> rows,
        CancellationToken ct)
    {
        var lookup = new MedWinNameLookup();
        if (rows.Count == 0) return lookup;

        var medicineIds = rows
            .Where(r => r.MedWinMedicineId is > 0)
            .Select(r => r.MedWinMedicineId!.Value)
            .Distinct()
            .ToList();

        if (medicineIds.Count > 0)
        {
            var byMedicineId = await _uow.Repository<Medicine>().QueryIncludingDeleted().AsNoTracking()
                .Where(m => medicineIds.Contains(m.Id))
                .Select(m => new { m.Id, m.Name })
                .ToListAsync(ct);

            foreach (var row in byMedicineId)
                lookup.ByMedicineId[row.Id] = row.Name;
        }

        var medWinIds = rows.Select(r => r.MedWinId).Distinct().ToList();
        var orphanRows = await _uow.Repository<Medicine>().QueryIncludingDeleted().AsNoTracking()
            .Where(m => m.Notes != null
                        && EF.Functions.Like(m.Notes!, "MedWinId:%")
                        && !EF.Functions.Like(m.Notes!, "%|%"))
            .Select(m => new { m.Name, m.Notes })
            .ToListAsync(ct);

        foreach (var orphan in orphanRows)
        {
            var medWinId = MedicineNotesHelper.ParseMedWinId(orphan.Notes);
            if (medWinId is > 0 && medWinIds.Contains(medWinId.Value))
                lookup.ByMedWinId[medWinId.Value] = orphan.Name;
        }

        return lookup;
    }
}
