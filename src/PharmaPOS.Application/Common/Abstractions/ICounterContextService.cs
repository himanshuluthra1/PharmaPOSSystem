using PharmaPOS.Application.Features.Counters;

namespace PharmaPOS.Application.Common.Abstractions;

/// <summary>
/// Process-wide active billing counter / operator session for the logged-in user.
/// </summary>
public interface ICounterContextService
{
    CounterSessionDto? ActiveSession { get; }
    int? ActiveCounterId { get; }
    int? ActiveSessionId { get; }
    string? ActiveCounterDisplay { get; }
    bool HasActiveCounter { get; }

    void SetActiveSession(CounterSessionDto session);
    void Clear();
}
