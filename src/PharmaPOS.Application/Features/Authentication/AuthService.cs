using Microsoft.EntityFrameworkCore;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Common.Models;
using PharmaPOS.Domain.Entities.Identity;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.Authentication;

/// <summary>
/// Default authentication service. Validates credentials with BCrypt, enforces
/// lockout after repeated failures, records login history and hydrates the
/// <see cref="UserSession"/> (including effective permissions) on success.
/// </summary>
public class AuthService : IAuthService
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly IUnitOfWork _uow;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public AuthService(
        IUnitOfWork uow,
        IPasswordHasher passwordHasher,
        ICurrentUserService currentUser,
        IDateTimeProvider clock)
    {
        _uow = uow;
        _passwordHasher = passwordHasher;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<UserSession>> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return Result.Failure<UserSession>("Username and password are required.");

        var user = await _uow.Repository<User>().Query()
            .Include(u => u.Role)!
                .ThenInclude(r => r!.RolePermissions)
                    .ThenInclude(rp => rp.Permission)
            .Include(u => u.Branch)
            .FirstOrDefaultAsync(u => u.Username == request.Username, ct);

        if (user is null)
            return Result.Failure<UserSession>("Invalid username or password.");

        if (user.IsLockedOut && user.LockoutEndUtc > _clock.UtcNow)
            return Result.Failure<UserSession>($"Account locked. Try again after {user.LockoutEndUtc:t}.");

        if (user.Status != EntityStatus.Active)
            return Result.Failure<UserSession>("This account is inactive. Contact your administrator.");

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            await RecordFailedAttemptAsync(user, ct);
            return Result.Failure<UserSession>("Invalid username or password.");
        }

        // Successful login: reset counters, stamp last login, record history.
        user.FailedLoginAttempts = 0;
        user.IsLockedOut = false;
        user.LockoutEndUtc = null;
        user.LastLoginUtc = _clock.UtcNow;

        await _uow.Repository<UserLoginHistory>().AddAsync(new UserLoginHistory
        {
            UserId = user.Id,
            LoginTimeUtc = _clock.UtcNow,
            MachineName = Environment.MachineName,
            WasSuccessful = true
        }, ct);

        await _uow.SaveChangesAsync(ct);

        var permissions = user.Role?.RolePermissions
            .Where(rp => rp.Permission is not null)
            .Select(rp => rp.Permission!.Key)
            .ToArray() ?? Array.Empty<string>();

        var session = new UserSession
        {
            UserId = user.Id,
            Username = user.Username,
            FullName = user.FullName,
            RoleName = user.Role?.Name ?? string.Empty,
            BranchId = user.BranchId,
            BranchName = user.Branch?.Name,
            Permissions = permissions,
            LoginTimeUtc = _clock.UtcNow
        };

        _currentUser.SetCurrentUser(session);
        return Result.Success(session);
    }

    public async Task<Result> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default)
    {
        var user = await _uow.Repository<User>().GetByIdAsync(request.UserId, ct);
        if (user is null)
            return Result.Failure("User not found.");

        if (!_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            return Result.Failure("Current password is incorrect.");

        if (request.NewPassword.Length < 6)
            return Result.Failure("New password must be at least 6 characters.");

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        user.MustChangePassword = false;
        _uow.Repository<User>().Update(user);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task LogoutAsync(int userId, CancellationToken ct = default)
    {
        var lastLogin = await _uow.Repository<UserLoginHistory>().Query()
            .Where(h => h.UserId == userId && h.LogoutTimeUtc == null)
            .OrderByDescending(h => h.LoginTimeUtc)
            .FirstOrDefaultAsync(ct);

        if (lastLogin is not null)
        {
            lastLogin.LogoutTimeUtc = _clock.UtcNow;
            _uow.Repository<UserLoginHistory>().Update(lastLogin);
            await _uow.SaveChangesAsync(ct);
        }

        _currentUser.Clear();
    }

    private async Task RecordFailedAttemptAsync(User user, CancellationToken ct)
    {
        user.FailedLoginAttempts++;
        if (user.FailedLoginAttempts >= MaxFailedAttempts)
        {
            user.IsLockedOut = true;
            user.LockoutEndUtc = _clock.UtcNow.Add(LockoutDuration);
        }

        await _uow.Repository<UserLoginHistory>().AddAsync(new UserLoginHistory
        {
            UserId = user.Id,
            LoginTimeUtc = _clock.UtcNow,
            MachineName = Environment.MachineName,
            WasSuccessful = false,
            FailureReason = "Invalid password"
        }, ct);

        await _uow.SaveChangesAsync(ct);
    }
}
