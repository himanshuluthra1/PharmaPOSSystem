using Microsoft.EntityFrameworkCore;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Domain.Entities.Masters;
using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.Masters;

public sealed class NewMedicineMappingService : INewMedicineMappingService
{
    private readonly IUnitOfWork _uow;

    public NewMedicineMappingService(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<List<NewMedicineMappingRowDto>> ListRowsAsync(
        bool includeVerified,
        string? search = null,
        int take = 500,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 2000);
        var q = _uow.Repository<Medicine>().Query().AsNoTracking()
            .Where(m => m.ImagePath == null || m.ImagePath == "");

        if (!includeVerified)
            q = q.Where(m => !m.IsNewMappingVerified);

        var term = (search ?? string.Empty).Trim();
        if (term.Length > 0)
        {
            q = q.Where(m =>
                EF.Functions.Like(m.Name, "%" + term + "%")
                || (m.GenericName != null && EF.Functions.Like(m.GenericName, "%" + term + "%")));
        }

        var rows = await q
            .OrderBy(m => m.Name)
            .ThenBy(m => m.Id)
            .Take(take)
            .Select(m => new
            {
                m.Id,
                m.Name,
                m.GenericName,
                m.IsNewMappingVerified,
                m.MappedCatalogueMedicineId
            })
            .ToListAsync(ct);

        var mappedIds = rows
            .Where(r => r.MappedCatalogueMedicineId is > 0)
            .Select(r => r.MappedCatalogueMedicineId!.Value)
            .Distinct()
            .ToList();

        var names = mappedIds.Count == 0
            ? new Dictionary<int, string>()
            : await _uow.Repository<Medicine>().Query().AsNoTracking()
                .Where(m => mappedIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m.Name, ct);

        return rows.Select(r => new NewMedicineMappingRowDto(
            r.Id,
            r.Name,
            r.GenericName,
            r.IsNewMappingVerified,
            r.MappedCatalogueMedicineId,
            r.MappedCatalogueMedicineId is int mid && names.TryGetValue(mid, out var n) ? n : null
        )).ToList();
    }

    public async Task<List<NewMedicineMappingSuggestionDto>> SuggestCatalogueAsync(
        string term,
        int take = 20,
        CancellationToken ct = default)
    {
        term = (term ?? string.Empty).Trim();
        if (term.Length < 3) return [];

        take = Math.Clamp(take, 1, 50);
        return await _uow.Repository<Medicine>().Query().AsNoTracking()
            .Where(m => m.ImagePath != null && m.ImagePath != "")
            .Where(m =>
                EF.Functions.Like(m.Name, "%" + term + "%")
                || (m.GenericName != null && EF.Functions.Like(m.GenericName, "%" + term + "%")))
            .OrderBy(m => m.Name)
            .Take(take)
            .Select(m => new NewMedicineMappingSuggestionDto(
                m.Id,
                m.Name,
                m.GenericName,
                m.ImagePath))
            .ToListAsync(ct);
    }

    public async Task<Result> SaveAsync(
        IReadOnlyList<NewMedicineMappingSaveItem> items,
        CancellationToken ct = default)
    {
        if (items.Count == 0)
            return Result.Failure("Nothing to save.");

        var ids = items.Select(i => i.MedicineId).Distinct().ToList();
        var catalogueIds = items.Select(i => i.MappedCatalogueMedicineId).Distinct().ToList();

        var sources = await _uow.Repository<Medicine>().Query()
            .Where(m => ids.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, ct);

        var catalogueIdList = await _uow.Repository<Medicine>().Query().AsNoTracking()
            .Where(m => catalogueIds.Contains(m.Id)
                        && m.ImagePath != null
                        && m.ImagePath != "")
            .Select(m => m.Id)
            .ToListAsync(ct);
        var catalogues = catalogueIdList.ToHashSet();

        foreach (var item in items)
        {
            if (!sources.TryGetValue(item.MedicineId, out var med))
                return Result.Failure($"Medicine #{item.MedicineId} was not found.");

            if (string.IsNullOrWhiteSpace(med.ImagePath) == false)
                return Result.Failure($"\"{med.Name}\" is not a blank-ImagePath row.");

            if (!catalogues.Contains(item.MappedCatalogueMedicineId))
                return Result.Failure($"Catalogue medicine #{item.MappedCatalogueMedicineId} was not found or has no image.");

            if (item.MedicineId == item.MappedCatalogueMedicineId)
                return Result.Failure("Cannot map a medicine onto itself.");

            med.MappedCatalogueMedicineId = item.MappedCatalogueMedicineId;
            med.IsNewMappingVerified = true;
            _uow.Repository<Medicine>().Update(med);
        }

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
