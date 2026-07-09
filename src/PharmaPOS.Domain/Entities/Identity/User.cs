using PharmaPOS.Domain.Common;
using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Domain.Entities.Identity;

/// <summary>An application user who authenticates and operates the POS.</summary>
public class User : BaseEntity
{
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? AvatarPath { get; set; }

    public int RoleId { get; set; }
    public Role? Role { get; set; }

    public int? BranchId { get; set; }
    public Branch? Branch { get; set; }

    public EntityStatus Status { get; set; } = EntityStatus.Active;
    public bool MustChangePassword { get; set; }
    public DateTime? LastLoginUtc { get; set; }
    public int FailedLoginAttempts { get; set; }
    public bool IsLockedOut { get; set; }
    public DateTime? LockoutEndUtc { get; set; }

    // Password reset support
    public string? PasswordResetToken { get; set; }
    public DateTime? PasswordResetTokenExpiryUtc { get; set; }

    public ICollection<UserLoginHistory> LoginHistory { get; set; } = new List<UserLoginHistory>();
}
