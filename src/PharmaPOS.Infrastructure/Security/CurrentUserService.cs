using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Common.Models;
using PharmaPOS.Shared.Security;

namespace PharmaPOS.Infrastructure.Security;

/// <summary>
/// In-memory holder of the authenticated session for the desktop process.
/// Registered as a singleton because a desktop app has exactly one active user.
/// </summary>
public class CurrentUserService : ICurrentUserService
{
    private readonly object _gate = new();
    private UserSession? _session;

    public UserSession? CurrentUser
    {
        get { lock (_gate) return _session; }
    }

    public bool IsAuthenticated => CurrentUser is not null;

    public void SetCurrentUser(UserSession session)
    {
        lock (_gate) _session = session;
    }

    public void Clear()
    {
        lock (_gate) _session = null;
    }

    public bool HasPermission(string permissionKey)
    {
        var perms = CurrentUser?.Permissions;
        return perms is not null && PermissionResolver.Has(perms, permissionKey);
    }

    public bool HasAnyPermission(params string[] permissionKeys)
    {
        var perms = CurrentUser?.Permissions;
        return perms is not null && PermissionResolver.HasAny(perms, permissionKeys);
    }

    public bool CanAccessModule(string module)
    {
        var perms = CurrentUser?.Permissions;
        return perms is not null && PermissionResolver.CanAccessModule(perms, module);
    }
}
