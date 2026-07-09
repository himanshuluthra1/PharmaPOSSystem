using PharmaPOS.Domain.Common;

namespace PharmaPOS.Domain.Entities.Identity;

/// <summary>Records each authentication attempt for auditing and login history.</summary>
public class UserLoginHistory : BaseEntity
{
    public int UserId { get; set; }
    public User? User { get; set; }

    public DateTime LoginTimeUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LogoutTimeUtc { get; set; }
    public string? MachineName { get; set; }
    public string? IpAddress { get; set; }
    public bool WasSuccessful { get; set; }
    public string? FailureReason { get; set; }
}
