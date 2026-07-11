namespace PharmaPOS.Application.Features.Masters;

public record MedicineMappingListItemDto(
    int Id,
    string Name,
    string? GenericName,
    string? PackInfo,
    string? ExternalId,
    bool IsMatched);

public record MedicineMappingPair(int OneMgMedicineId, int MedWinMedicineId);

public record PendingMedicineMappingDto(
    int MedWinMedicineId,
    string MedWinName,
    string? MedWinExternalId,
    int OneMgMedicineId,
    string OneMgName,
    string? OneMgExternalId,
    string? OneMgPackInfo);

public record MedicineMappingBatchError(int MedWinMedicineId, int OneMgMedicineId, string Message);

public record MedicineMappingBatchResult(int Succeeded, IReadOnlyList<MedicineMappingBatchError> Errors)
{
    public bool IsSuccess => Errors.Count == 0 && Succeeded > 0;
}

public record MedicineMedWinMappingDto(
    int Id,
    int OneMgMedicineId,
    string OneMgMedicineName,
    string? OneMgCatalogId,
    int? MedWinMedicineId,
    int MedWinId,
    string MedWinMedicineName,
    DateTime MappedAtUtc);

public record AutoMapSuggestion(
    int MedWinMedicineId,
    string MedWinName,
    string? MedWinExternalId,
    int OneMgMedicineId,
    string OneMgName,
    string? OneMgExternalId,
    string? OneMgPackInfo);

public record AutoMapSuggestionResult(
    IReadOnlyList<AutoMapSuggestion> Suggestions,
    int TotalProcessed,
    int SkippedNoPrefix,
    int SkippedNoCandidates,
    int SkippedAmbiguous,
    int SkippedAlreadyQueued);
