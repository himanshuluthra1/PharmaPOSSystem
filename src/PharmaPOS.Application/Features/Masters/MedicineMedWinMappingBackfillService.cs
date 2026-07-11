using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaPOS.Application.Common;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Domain.Entities.Masters;

namespace PharmaPOS.Application.Features.Masters;

/// <summary>One-time import of legacy Notes-based links into <see cref="MedicineMedWinMapping"/>.</summary>
public interface IMedicineMedWinMappingBackfillService
{
    Task EnsureBackfilledAsync(CancellationToken ct = default);
}

public sealed class MedicineMedWinMappingBackfillService : IMedicineMedWinMappingBackfillService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static volatile bool _completed;
    private static volatile bool _repairCompleted;

    private readonly IServiceScopeFactory _scopeFactory;

    public MedicineMedWinMappingBackfillService(IServiceScopeFactory scopeFactory)
        => _scopeFactory = scopeFactory;

    public async Task EnsureBackfilledAsync(CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            if (!_repairCompleted)
            {
                await RepairPlaceholderMedWinNamesAsync(uow, ct);
                _repairCompleted = true;
            }

            if (_completed) return;

            if (await uow.Repository<MedicineMedWinMapping>().Query().AnyAsync(ct))
            {
                _completed = true;
                return;
            }

            var oneMgRows = await uow.Repository<Medicine>().Query().AsNoTracking()
                .Where(m => !m.IsDeleted
                            && m.Notes != null
                            && EF.Functions.Like(m.Notes, "%OneMG-ID:%")
                            && EF.Functions.Like(m.Notes, "%MedWinId:%"))
                .Select(m => new { m.Id, m.Name, m.Notes })
                .ToListAsync(ct);

            if (oneMgRows.Count == 0)
            {
                _completed = true;
                return;
            }

            var orphanNames = await LoadOrphanMedWinNamesAsync(uow, ct);
            var now = DateTime.UtcNow;
            var insertedMedWinIds = new HashSet<int>();
            var mappings = new List<MedicineMedWinMapping>();

            foreach (var oneMg in oneMgRows)
            {
                foreach (var medWinId in MedicineNotesHelper.ParseAllMedWinIds(oneMg.Notes))
                {
                    if (!insertedMedWinIds.Add(medWinId))
                        continue;

                    mappings.Add(new MedicineMedWinMapping
                    {
                        CreatedAtUtc = now,
                        OneMgMedicineId = oneMg.Id,
                        MedWinId = medWinId,
                        MedWinMedicineName = ResolveMedWinName(medWinId, orphanNames),
                        OneMgMedicineName = oneMg.Name,
                        OneMgCatalogId = MedicineNotesHelper.ParseOneMgId(oneMg.Notes),
                    });
                }
            }

            if (mappings.Count > 0)
            {
                await uow.Repository<MedicineMedWinMapping>().AddRangeAsync(mappings, ct);
                await uow.SaveChangesAsync(ct);
            }

            scope.ServiceProvider.GetService<ILinkedMedWinIdCache>()?.Invalidate();
            _completed = true;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task RepairPlaceholderMedWinNamesAsync(IUnitOfWork uow, CancellationToken ct)
    {
        var placeholders = await uow.Repository<MedicineMedWinMapping>().Query()
            .Where(m => EF.Functions.Like(m.MedWinMedicineName, "MedWinId:%"))
            .ToListAsync(ct);

        if (placeholders.Count == 0) return;

        var orphanNames = await LoadOrphanMedWinNamesAsync(uow, ct);
        var medicineIds = placeholders
            .Where(m => m.MedWinMedicineId is > 0)
            .Select(m => m.MedWinMedicineId!.Value)
            .Distinct()
            .ToList();

        var namesByMedicineId = medicineIds.Count == 0
            ? new Dictionary<int, string>()
            : await uow.Repository<Medicine>().QueryIncludingDeleted().AsNoTracking()
                .Where(m => medicineIds.Contains(m.Id))
                .Select(m => new { m.Id, m.Name })
                .ToDictionaryAsync(m => m.Id, m => m.Name, ct);

        var changed = false;
        foreach (var mapping in placeholders)
        {
            var resolved = mapping.MedWinMedicineName;
            if (mapping.MedWinMedicineId is int medicineId
                && namesByMedicineId.TryGetValue(medicineId, out var byMedicineId))
                resolved = byMedicineId;
            else if (orphanNames.TryGetValue(mapping.MedWinId, out var byMedWinId))
                resolved = byMedWinId;
            else
                resolved = ResolveMedWinName(mapping.MedWinId, orphanNames);

            if (resolved == mapping.MedWinMedicineName) continue;

            mapping.MedWinMedicineName = resolved;
            uow.Repository<MedicineMedWinMapping>().Update(mapping);
            changed = true;
        }

        if (changed)
            await uow.SaveChangesAsync(ct);
    }

    private static async Task<Dictionary<int, string>> LoadOrphanMedWinNamesAsync(
        IUnitOfWork uow, CancellationToken ct)
    {
        var orphanRows = await uow.Repository<Medicine>().QueryIncludingDeleted().AsNoTracking()
            .Where(m => m.Notes != null
                        && EF.Functions.Like(m.Notes!, "MedWinId:%")
                        && !EF.Functions.Like(m.Notes!, "%|%"))
            .Select(m => new { m.Name, m.Notes })
            .ToListAsync(ct);

        var names = new Dictionary<int, string>();
        foreach (var orphan in orphanRows)
        {
            var medWinId = MedicineNotesHelper.ParseMedWinId(orphan.Notes);
            if (medWinId is > 0)
                names[medWinId.Value] = orphan.Name;
        }

        return names;
    }

    private static string ResolveMedWinName(int medWinId, IReadOnlyDictionary<int, string> orphanNames)
        => orphanNames.TryGetValue(medWinId, out var name) ? name : $"MedWinId:{medWinId}";
}
