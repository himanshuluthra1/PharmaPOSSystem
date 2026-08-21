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

public sealed class CounterDayCloseDto
{
    public int SessionId { get; init; }
    public string CompanyName { get; init; } = "PharmaPOS";
    public string CounterCode { get; init; } = string.Empty;
    public string CounterName { get; init; } = string.Empty;
    public string OperatorName { get; init; } = string.Empty;
    public DateTime OpenedAtLocal { get; init; }
    public DateTime? ClosedAtLocal { get; init; }
    public decimal OpeningFloat { get; init; }
    public int BillCount { get; init; }
    public decimal CashCollected { get; init; }
    public decimal CardCollected { get; init; }
    public decimal UpiCollected { get; init; }
    public decimal OtherCollected { get; init; }
    public decimal CreditCollected { get; init; }
    public decimal ExpectedCashInDrawer { get; init; }
    public decimal CountedCash { get; init; }
    public string? Remarks { get; init; }
    public string? MachineName { get; init; }
    public bool IsClosed { get; init; }

    public decimal Variance => Math.Round(CountedCash - ExpectedCashInDrawer, 2);
    public decimal Shortage => Variance < 0 ? Math.Abs(Variance) : 0m;
    public decimal Excess => Variance > 0 ? Variance : 0m;
    public string VarianceLabel =>
        Variance > 0.009m ? "Excess" : Variance < -0.009m ? "Shortage" : "Matched";
}

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
    Task<Result<CounterDayCloseDto>> PreviewDayCloseAsync(int sessionId, CancellationToken ct = default);
    Task<Result<CounterDayCloseDto>> CloseDayAsync(
        int sessionId, decimal countedCash, string? remarks, CancellationToken ct = default);
}
