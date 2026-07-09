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
}
