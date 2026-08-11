using PharmaPOS.Application.Features.Purchases;

namespace PharmaPOS.WPF.Services;

/// <summary>
/// Hands a receive draft from Purchase Orders to the Purchase GRN screen
/// across navigation (both VMs are transient).
/// </summary>
public interface IPurchaseOrderReceiveBridge
{
    void Queue(PurchaseOrderReceiveDraftDto draft);
    PurchaseOrderReceiveDraftDto? TakePending();
}

public sealed class PurchaseOrderReceiveBridge : IPurchaseOrderReceiveBridge
{
    private PurchaseOrderReceiveDraftDto? _pending;

    public void Queue(PurchaseOrderReceiveDraftDto draft) => _pending = draft;

    public PurchaseOrderReceiveDraftDto? TakePending()
    {
        var draft = _pending;
        _pending = null;
        return draft;
    }
}
