using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Counters;

namespace PharmaPOS.Infrastructure.Security;

public sealed class CounterContextService : ICounterContextService
{
    private readonly object _gate = new();
    private CounterSessionDto? _session;

    public CounterSessionDto? ActiveSession
    {
        get { lock (_gate) return _session; }
    }

    public int? ActiveCounterId => ActiveSession?.CounterId;
    public int? ActiveSessionId => ActiveSession?.SessionId;
    public string? ActiveCounterDisplay
    {
        get
        {
            var s = ActiveSession;
            return s is null ? null : $"{s.CounterCode} · {s.OperatorName}";
        }
    }

    public bool HasActiveCounter => ActiveSession is not null;

    public void SetActiveSession(CounterSessionDto session)
    {
        lock (_gate) _session = session;
    }

    public void Clear()
    {
        lock (_gate) _session = null;
    }
}
