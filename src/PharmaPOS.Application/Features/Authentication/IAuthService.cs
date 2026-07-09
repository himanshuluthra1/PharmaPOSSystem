using PharmaPOS.Application.Common.Models;
using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.Authentication;

/// <summary>Authentication and credential-management operations.</summary>
public interface IAuthService
{
    Task<Result<UserSession>> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<Result> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default);
    Task LogoutAsync(int userId, CancellationToken ct = default);
}
