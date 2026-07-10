using PharmaPOS.Application.Common.Models;

namespace PharmaPOS.Application.Common.Abstractions;

/// <summary>
/// Ambient accessor for the authenticated user. Populated on login and read by
/// auditing, permission checks and branch scoping throughout the app.
/// </summary>
public interface ICurrentUserService
{
    UserSession? CurrentUser { get; }
    bool IsAuthenticated { get; }

    void SetCurrentUser(UserSession session);
    void Clear();

    bool HasPermission(string permissionKey);

    /// <summary>True when the user has any of the listed permissions (or module manage).</summary>
    bool HasAnyPermission(params string[] permissionKeys);

    /// <summary>True when the user has any permission in the module (e.g. sales.* or sales.manage).</summary>
    bool CanAccessModule(string module);
}
