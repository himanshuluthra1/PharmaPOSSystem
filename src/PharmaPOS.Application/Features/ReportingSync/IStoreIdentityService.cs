namespace PharmaPOS.Application.Features.ReportingSync;

/// <summary>
/// Machine-bound store identity.
/// StoreId = system unique key used for all VPS sync.
/// StoreCode = owner-chosen display name (not used as sync tenant key).
/// </summary>
public interface IStoreIdentityService
{
    bool IsConfigured { get; }

    /// <summary>Auto-generated unique store id (VPS mapping key).</summary>
    string? StoreId { get; }

    /// <summary>Owner-chosen store code / name for display.</summary>
    string? StoreCode { get; }

    string MachineId { get; }

    void Load();

    Task<bool> TryRestoreFromServerAsync(CancellationToken ct = default);

    Task<bool> ValidateAgainstServerAsync(CancellationToken ct = default);

    /// <summary>Request/complete activation. <paramref name="storeCode"/> is owner-chosen display code.</summary>
    Task<StoreActivationResult> ActivateAsync(string storeCode, CancellationToken ct = default);

    void InvalidateIfMachineMismatch();

    void ClearLocal();
}

public sealed class StoreActivationResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;

    public static StoreActivationResult Ok(string message) => new() { Success = true, Message = message };
    public static StoreActivationResult Fail(string message) => new() { Success = false, Message = message };
}

public sealed class NullStoreIdentityService : IStoreIdentityService
{
    public bool IsConfigured => false;
    public string? StoreId => null;
    public string? StoreCode => null;
    public string MachineId => "UNKNOWN";
    public void Load() { }
    public Task<bool> TryRestoreFromServerAsync(CancellationToken ct = default) => Task.FromResult(false);
    public Task<bool> ValidateAgainstServerAsync(CancellationToken ct = default) => Task.FromResult(false);
    public Task<StoreActivationResult> ActivateAsync(string storeCode, CancellationToken ct = default)
        => Task.FromResult(StoreActivationResult.Fail("Store identity is not available."));
    public void InvalidateIfMachineMismatch() { }
    public void ClearLocal() { }
}
