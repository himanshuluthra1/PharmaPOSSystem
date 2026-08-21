using Microsoft.EntityFrameworkCore;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Domain.Entities.Inventory;
using PharmaPOS.Domain.Entities.Masters;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.ShortageBook;

public sealed class ShortageBookService : IShortageBookService
{
    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _clock;

    public ShortageBookService(IUnitOfWork uow, IDateTimeProvider clock)
    {
        _uow = uow;
        _clock = clock;
    }

    public async Task<Result<ShortageBookListItemDto>> RecordAsync(
        RecordShortageRequest request, int? branchId, string? recordedBy, CancellationToken ct = default)
    {
        if (request.MedicineId <= 0)
            return Result.Failure<ShortageBookListItemDto>("Select a medicine.");

        var requested = Math.Round(Math.Max(0, request.RequestedQuantity), 2);
        if (requested <= 0)
            return Result.Failure<ShortageBookListItemDto>("Requested quantity must be greater than zero.");

        var available = Math.Round(Math.Max(0, request.AvailableQuantity), 2);
        var shortfall = Math.Round(Math.Max(0, requested - available), 2);
        if (shortfall <= 0)
            return Result.Failure<ShortageBookListItemDto>("No shortfall to record — stock covers the request.");

        var medicine = await _uow.Repository<Medicine>().Query().AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == request.MedicineId && !m.IsDeleted, ct);
        if (medicine is null)
            return Result.Failure<ShortageBookListItemDto>("Medicine not found.");

        try
        {
            var dayStart = _clock.UtcNow.Date;
            var dayEnd = dayStart.AddDays(1);

            var existing = await _uow.Repository<ShortageBookEntry>().Query()
                .Where(e => !e.IsDeleted
                            && e.Status == ShortageStatus.Open
                            && e.MedicineId == request.MedicineId
                            && e.RecordedAtUtc >= dayStart
                            && e.RecordedAtUtc < dayEnd)
                .Where(e => !branchId.HasValue || e.BranchId == branchId)
                .OrderByDescending(e => e.Id)
                .FirstOrDefaultAsync(ct);

            ShortageBookEntry entry;
            if (existing is not null)
            {
                existing.RequestedQuantity += requested;
                existing.AvailableQuantity = available;
                existing.ShortfallQuantity += shortfall;
                if (!string.IsNullOrWhiteSpace(request.CustomerName))
                    existing.CustomerName = request.CustomerName.Trim();
                if (!string.IsNullOrWhiteSpace(request.CustomerPhone))
                    existing.CustomerPhone = request.CustomerPhone.Trim();
                if (!string.IsNullOrWhiteSpace(request.Notes))
                    existing.Notes = MergeNotes(existing.Notes, request.Notes);
                existing.Source = request.Source;
                _uow.Repository<ShortageBookEntry>().Update(existing);
                entry = existing;
            }
            else
            {
                entry = new ShortageBookEntry
                {
                    BranchId = branchId,
                    MedicineId = request.MedicineId,
                    RequestedQuantity = requested,
                    AvailableQuantity = available,
                    ShortfallQuantity = shortfall,
                    Status = ShortageStatus.Open,
                    Source = request.Source,
                    CustomerName = string.IsNullOrWhiteSpace(request.CustomerName) ? null : request.CustomerName.Trim(),
                    CustomerPhone = string.IsNullOrWhiteSpace(request.CustomerPhone) ? null : request.CustomerPhone.Trim(),
                    Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                    RecordedAtUtc = _clock.UtcNow,
                    RecordedBy = recordedBy
                };
                await _uow.Repository<ShortageBookEntry>().AddAsync(entry, ct);
            }

            await _uow.SaveChangesAsync(ct);

            var dto = await MapOneAsync(entry.Id, ct);
            return dto is null
                ? Result.Failure<ShortageBookListItemDto>("Saved, but could not reload shortage entry.")
                : Result.Success(dto);
        }
        catch (Exception ex)
        {
            return Result.Failure<ShortageBookListItemDto>(ex.Message);
        }
    }

    public async Task<List<ShortageBookListItemDto>> ListAsync(
        ShortageBookFilter filter, int? branchId, CancellationToken ct = default)
    {
        var q = _uow.Repository<ShortageBookEntry>().Query().AsNoTracking()
            .Include(e => e.Medicine)
            .Include(e => e.PurchaseOrder)
            .Where(e => !e.IsDeleted);

        if (branchId.HasValue)
            q = q.Where(e => e.BranchId == branchId);
        if (filter.Status is ShortageStatus status)
            q = q.Where(e => e.Status == status);

        var search = filter.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            q = q.Where(e =>
                (e.Medicine != null && EF.Functions.Like(e.Medicine.Name, "%" + search + "%")) ||
                (e.CustomerName != null && EF.Functions.Like(e.CustomerName, "%" + search + "%")) ||
                (e.CustomerPhone != null && e.CustomerPhone.Contains(search)));
        }

        var take = filter.Take <= 0 ? 300 : Math.Min(filter.Take, 500);
        var rows = await q
            .OrderByDescending(e => e.RecordedAtUtc)
            .ThenByDescending(e => e.Id)
            .Take(take)
            .ToListAsync(ct);

        return rows.Select(Map).ToList();
    }

    public async Task<List<ShortageAggregateDto>> GetOpenAggregatesAsync(
        int? branchId, CancellationToken ct = default)
    {
        var q = _uow.Repository<ShortageBookEntry>().Query().AsNoTracking()
            .Include(e => e.Medicine)
            .Where(e => !e.IsDeleted && e.Status == ShortageStatus.Open && e.ShortfallQuantity > 0);
        if (branchId.HasValue)
            q = q.Where(e => e.BranchId == branchId);

        var rows = await q.ToListAsync(ct);
        return rows
            .GroupBy(e => e.MedicineId)
            .Select(g =>
            {
                var med = g.First().Medicine;
                return new ShortageAggregateDto(
                    g.Key,
                    med?.Name ?? $"#{g.Key}",
                    g.Sum(x => x.ShortfallQuantity),
                    g.Count(),
                    med?.PurchasePrice ?? 0m);
            })
            .Where(x => x.ShortfallQuantity > 0)
            .OrderByDescending(x => x.ShortfallQuantity)
            .ToList();
    }

    public async Task<Result> CancelAsync(int id, int? branchId, CancellationToken ct = default)
    {
        var entry = await _uow.Repository<ShortageBookEntry>().Query()
            .FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted, ct);
        if (entry is null)
            return Result.Failure("Shortage entry not found.");
        if (branchId.HasValue && entry.BranchId != branchId)
            return Result.Failure("Shortage entry belongs to another branch.");
        if (entry.Status is ShortageStatus.Fulfilled or ShortageStatus.Cancelled)
            return Result.Failure("This shortage entry is already closed.");

        entry.Status = ShortageStatus.Cancelled;
        entry.ResolvedAtUtc = _clock.UtcNow;
        _uow.Repository<ShortageBookEntry>().Update(entry);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task MarkOrderedAsync(
        IReadOnlyDictionary<int, int> medicineIdToPurchaseOrderId,
        int? branchId,
        CancellationToken ct = default)
    {
        if (medicineIdToPurchaseOrderId.Count == 0) return;

        var medicineIds = medicineIdToPurchaseOrderId.Keys.ToList();
        var q = _uow.Repository<ShortageBookEntry>().Query()
            .Where(e => !e.IsDeleted
                        && e.Status == ShortageStatus.Open
                        && medicineIds.Contains(e.MedicineId));
        if (branchId.HasValue)
            q = q.Where(e => e.BranchId == branchId);

        var rows = await q.ToListAsync(ct);
        if (rows.Count == 0) return;

        var now = _clock.UtcNow;
        foreach (var row in rows)
        {
            if (!medicineIdToPurchaseOrderId.TryGetValue(row.MedicineId, out var poId))
                continue;
            row.Status = ShortageStatus.Ordered;
            row.PurchaseOrderId = poId;
            row.ResolvedAtUtc = null;
            _uow.Repository<ShortageBookEntry>().Update(row);
        }

        await _uow.SaveChangesAsync(ct);
    }

    public async Task MarkFulfilledForMedicinesAsync(
        IEnumerable<int> medicineIds, int? branchId, CancellationToken ct = default)
    {
        var ids = medicineIds.Distinct().ToList();
        if (ids.Count == 0) return;

        var q = _uow.Repository<ShortageBookEntry>().Query()
            .Where(e => !e.IsDeleted
                        && ids.Contains(e.MedicineId)
                        && (e.Status == ShortageStatus.Open || e.Status == ShortageStatus.Ordered));
        if (branchId.HasValue)
            q = q.Where(e => e.BranchId == branchId);

        var rows = await q.ToListAsync(ct);
        if (rows.Count == 0) return;

        var now = _clock.UtcNow;
        foreach (var row in rows)
        {
            row.Status = ShortageStatus.Fulfilled;
            row.ResolvedAtUtc = now;
            _uow.Repository<ShortageBookEntry>().Update(row);
        }

        await _uow.SaveChangesAsync(ct);
    }

    public async Task<decimal> GetOnHandQuantityAsync(
        int medicineId, int? branchId, CancellationToken ct = default)
    {
        var q = _uow.Repository<MedicineBatch>().Query().AsNoTracking()
            .Where(b => !b.IsDeleted && b.MedicineId == medicineId);
        if (branchId.HasValue)
            q = q.Where(b => b.BranchId == branchId);

        return await q.SumAsync(b => (decimal?)b.QuantityAvailable, ct) ?? 0m;
    }

    private async Task<ShortageBookListItemDto?> MapOneAsync(int id, CancellationToken ct)
    {
        var entry = await _uow.Repository<ShortageBookEntry>().Query().AsNoTracking()
            .Include(e => e.Medicine)
            .Include(e => e.PurchaseOrder)
            .FirstOrDefaultAsync(e => e.Id == id, ct);
        return entry is null ? null : Map(entry);
    }

    private static ShortageBookListItemDto Map(ShortageBookEntry e) => new(
        e.Id,
        e.MedicineId,
        e.Medicine?.Name ?? $"#{e.MedicineId}",
        e.RequestedQuantity,
        e.AvailableQuantity,
        e.ShortfallQuantity,
        e.Status,
        e.Source,
        e.CustomerName,
        e.CustomerPhone,
        e.Notes,
        e.RecordedAtUtc,
        e.RecordedBy,
        e.PurchaseOrderId,
        e.PurchaseOrder?.OrderNumber,
        e.ResolvedAtUtc);

    private static string MergeNotes(string? existing, string incoming)
    {
        incoming = incoming.Trim();
        if (string.IsNullOrWhiteSpace(existing)) return incoming;
        if (existing.Contains(incoming, StringComparison.OrdinalIgnoreCase)) return existing;
        return $"{existing.Trim()}; {incoming}";
    }
}
