using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Common.Models;

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
        var user = CurrentUser;
        return user is not null && user.Permissions.Contains(permissionKey);
    }
}
