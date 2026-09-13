using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.Masters;

public interface INewMedicineMappingService
{
    Task<List<NewMedicineMappingRowDto>> ListRowsAsync(
        bool includeVerified,
        string? search = null,
        int take = 500,
        CancellationToken ct = default);

    Task<List<NewMedicineMappingSuggestionDto>> SuggestCatalogueAsync(
        string term,
        int take = 20,
        CancellationToken ct = default);

    Task<Result> SaveAsync(
        IReadOnlyList<NewMedicineMappingSaveItem> items,
        CancellationToken ct = default);
}

public sealed record NewMedicineMappingRowDto(
    int MedicineId,
    string Name,
    string? GenericName,
    bool IsVerified,
    int? MappedCatalogueMedicineId,
    string? MappedCatalogueName);

public sealed record NewMedicineMappingSuggestionDto(
    int MedicineId,
    string Name,
    string? GenericName,
    string? ImagePath);

public sealed record NewMedicineMappingSaveItem(
    int MedicineId,
    int MappedCatalogueMedicineId);
