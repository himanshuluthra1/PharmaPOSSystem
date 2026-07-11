using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.Masters;

public interface IMedicineMappingService
{
    /// <summary>All unmatched MedWin-only medicines, optionally filtered by name.</summary>
    Task<List<MedicineMappingListItemDto>> ListUnmatchedMedWinMedicinesAsync(
        string? filterTerm, CancellationToken ct = default);

    Task<List<MedicineMappingListItemDto>> SearchOneMgByBrandPrefixAsync(
        string brandPrefix, string? additionalTerm, bool includeMatched, CancellationToken ct = default);

    /// <summary>OneMG catalogue rows whose name starts with the same first word as the MedWin medicine.</summary>
    Task<List<MedicineMappingListItemDto>> SearchOneMgForMedWinAsync(
        int medWinMedicineId, string? additionalTerm, bool includeMatched, CancellationToken ct = default);

    Task<List<MedicineMappingListItemDto>> SearchOneMgMedicinesAsync(
        string term, bool includeMatched, CancellationToken ct = default);

    Task<List<MedicineMappingListItemDto>> SearchMedWinMedicinesAsync(
        string term, bool includeMatched, CancellationToken ct = default);

    Task<Result> MapMedWinToOneMgAsync(int oneMgMedicineId, int medWinMedicineId, CancellationToken ct = default);

    Task<MedicineMappingBatchResult> MapMedWinToOneMgBatchAsync(
        IReadOnlyList<MedicineMappingPair> mappings, CancellationToken ct = default);

    Task<List<MedicineMedWinMappingDto>> ListAppliedMappingsAsync(
        string? filterTerm, int take = 500, CancellationToken ct = default);

    /// <summary>Suggests confident MedWin → OneMG pairs for auto-mapping (does not save).</summary>
    Task<AutoMapSuggestionResult> SuggestAutoMappingsAsync(
        bool includeMatched,
        IReadOnlySet<int>? excludeMedWinMedicineIds = null,
        CancellationToken ct = default);
}
