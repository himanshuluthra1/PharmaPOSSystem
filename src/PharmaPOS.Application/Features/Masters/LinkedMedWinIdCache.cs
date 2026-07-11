using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaPOS.Application.Common;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Domain.Entities.Masters;
using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Application.Features.Masters;

/// <summary>Caches MedWin IDs already linked on OneMG catalogue rows.</summary>
public interface ILinkedMedWinIdCache
{
    Task<IReadOnlySet<int>> GetAsync(CancellationToken ct = default);
    void Invalidate();
}

public sealed class LinkedMedWinIdCache : ILinkedMedWinIdCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private IReadOnlySet<int>? _cache;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LinkedMedWinIdCache(IServiceScopeFactory scopeFactory)
        => _scopeFactory = scopeFactory;

    public async Task<IReadOnlySet<int>> GetAsync(CancellationToken ct = default)
    {
        if (_cache is not null) return _cache;

        await _gate.WaitAsync(ct);
        try
        {
            if (_cache is null)
            {
                using var scope = _scopeFactory.CreateScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                _cache = await LoadFromDbAsync(uow, ct);
            }

            return _cache;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate() => _cache = null;

    private static async Task<HashSet<int>> LoadFromDbAsync(IUnitOfWork uow, CancellationToken ct)
    {
        var ids = await uow.Repository<MedicineMedWinMapping>().Query().AsNoTracking()
            .Select(m => m.MedWinId)
            .ToListAsync(ct);

        return ids.Count > 0 ? ids.ToHashSet() : await LoadLegacyLinkedIdsFromNotesAsync(uow, ct);
    }

    private static async Task<HashSet<int>> LoadLegacyLinkedIdsFromNotesAsync(IUnitOfWork uow, CancellationToken ct)
    {
        var notesList = await uow.Repository<Medicine>().Query().AsNoTracking()
            .Where(m => !m.IsDeleted
                        && m.Status == EntityStatus.Active
                        && m.Notes != null
                        && EF.Functions.Like(m.Notes!, "%OneMG-ID:%")
                        && EF.Functions.Like(m.Notes!, "%MedWinId:%"))
            .Select(m => m.Notes!)
            .ToListAsync(ct);

        var linked = new HashSet<int>();
        foreach (var notes in notesList)
        {
            foreach (var id in MedicineNotesHelper.ParseAllMedWinIds(notes))
                linked.Add(id);
        }

        return linked;
    }
}
