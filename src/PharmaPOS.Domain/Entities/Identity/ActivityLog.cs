using PharmaPOS.Domain.Common;

namespace PharmaPOS.Domain.Entities.Identity;

/// <summary>
/// Immutable audit trail entry capturing create/edit/delete and other actions
/// performed by users across the system.
/// </summary>
public class ActivityLog : BaseEntity
{
    public int? UserId { get; set; }
    public User? User { get; set; }

    public string Action { get; set; } = string.Empty;
    public string? EntityName { get; set; }
    public string? EntityId { get; set; }
    public string? Details { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string? MachineName { get; set; }
}
