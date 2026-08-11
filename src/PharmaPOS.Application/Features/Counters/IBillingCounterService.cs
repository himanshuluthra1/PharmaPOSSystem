using PharmaPOS.Domain.Enums;
using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.Counters;

public record BillingCounterListDto(
    int Id,
    string Code,
    string Name,
    bool IsDefault,
    EntityStatus Status,
    int? BranchId,
    bool HasOpenSession,
    string? OpenOperatorName);

public class BillingCounterDetailDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;
    public int? BranchId { get; set; }
}

public record CounterPickDto(
    int Id,
    string Code,
    string Name,
    bool IsDefault,
    bool HasOpenSession,
    string? OpenOperatorName,
    int? OpenSessionUserId);

public record CounterSessionDto(
    int SessionId,
    int CounterId,
    string CounterCode,
    string CounterName,
    int UserId,
    string OperatorName,
    DateTime OpenedAtLocal,
    decimal OpeningFloat);

public record CounterCashSummaryDto(
    int CounterId,
    string CounterCode,
    string CounterName,
    int BillCount,
    decimal CashCollected,
    decimal CardCollected,
    decimal UpiCollected,
    decimal OtherCollected,
    decimal OpeningFloat,
    decimal ExpectedCashInDrawer,
    string? OperatorName,
    int? OpenSessionId);

public interface IBillingCounterService
{
    Task<List<BillingCounterListDto>> ListAsync(int? branchId, CancellationToken ct = default);
    Task<BillingCounterDetailDto?> GetAsync(int id, CancellationToken ct = default);
    Task<Result<int>> SaveAsync(BillingCounterDetailDto dto, CancellationToken ct = default);
    Task EnsureDefaultCountersAsync(int? branchId, CancellationToken ct = default);

    Task<List<CounterPickDto>> ListForPickerAsync(int? branchId, CancellationToken ct = default);
    Task<Result<CounterSessionDto>> OpenSessionAsync(int counterId, int userId, decimal openingFloat, CancellationToken ct = default);
    Task<Result> CloseSessionAsync(int sessionId, decimal? declaredClosingCash, string? remarks, CancellationToken ct = default);
    Task<CounterSessionDto?> GetOpenSessionForUserAsync(int userId, CancellationToken ct = default);

    Task<List<CounterCashSummaryDto>> GetCashSummaryAsync(int? branchId, DateTime businessDate, CancellationToken ct = default);
    Task<CounterCashSummaryDto?> GetActiveCounterCashAsync(int counterId, int? sessionId, DateTime businessDate, CancellationToken ct = default);
}
