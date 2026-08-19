namespace PharmaPOS.Application.Common.Abstractions;

/// <summary>Signals that shop data was written, used to trigger auto-backup.</summary>
public interface IDataChangeSignal
{
    void NotifyChanged();
    bool HasPendingChanges { get; }
    void ClearPending();
}
