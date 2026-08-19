using PharmaPOS.Application.Common.Abstractions;

namespace PharmaPOS.WPF.Services;

public sealed class DataChangeSignal : IDataChangeSignal
{
    private int _pending;

    public void NotifyChanged() => Interlocked.Exchange(ref _pending, 1);

    public bool HasPendingChanges => Volatile.Read(ref _pending) != 0;

    public void ClearPending() => Interlocked.Exchange(ref _pending, 0);
}
