using PharmaPOS.Domain.Enums;
using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.ShortageBook;

public record RecordShortageRequest(
    int MedicineId,
    decimal RequestedQuantity,
    decimal AvailableQuantity,
    ShortageSource Source,
    string? CustomerName = null,
    string? CustomerPhone = null,
    string? Notes = null);

public record ShortageBookListItemDto(
    int Id,
    int MedicineId,
    string MedicineName,
    decimal RequestedQuantity,
    decimal AvailableQuantity,
    decimal ShortfallQuantity,
    ShortageStatus Status,
    ShortageSource Source,
    string? CustomerName,
    string? CustomerPhone,
    string? Notes,
    DateTime RecordedAtUtc,
    string? RecordedBy,
    int? PurchaseOrderId,
    string? PurchaseOrderNumber,
    DateTime? ResolvedAtUtc)
{
    public string StatusLabel => Status.ToString();
    public string SourceLabel => Source.ToString();
    public string RecordedAtLabel => RecordedAtUtc.ToLocalTime().ToString("dd-MMM-yyyy hh:mm tt");
}

public record ShortageAggregateDto(
    int MedicineId,
    string MedicineName,
    decimal ShortfallQuantity,
    int EntryCount,
    decimal PurchasePrice);

public record ShortageBookFilter(
    ShortageStatus? Status = null,
    string? Search = null,
    int Take = 300);

public interface IShortageBookService
{
    Task<Result<ShortageBookListItemDto>> RecordAsync(
        RecordShortageRequest request, int? branchId, string? recordedBy, CancellationToken ct = default);

    Task<List<ShortageBookListItemDto>> ListAsync(
        ShortageBookFilter filter, int? branchId, CancellationToken ct = default);

    Task<List<ShortageAggregateDto>> GetOpenAggregatesAsync(int? branchId, CancellationToken ct = default);

    Task<Result> CancelAsync(int id, int? branchId, CancellationToken ct = default);

    Task MarkOrderedAsync(
        IReadOnlyDictionary<int, int> medicineIdToPurchaseOrderId, int? branchId, CancellationToken ct = default);

    Task MarkFulfilledForMedicinesAsync(
        IEnumerable<int> medicineIds, int? branchId, CancellationToken ct = default);

    Task<decimal> GetOnHandQuantityAsync(int medicineId, int? branchId, CancellationToken ct = default);
}
