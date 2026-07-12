using PharmaPOS.Domain.Common;

namespace PharmaPOS.Domain.Entities.System;

/// <summary>Immutable audit trail for sensitive operations (returns, overrides, etc.).</summary>
public class AuditLogEntry : BaseEntity
{
    public string EntityType { get; set; } = string.Empty;
    public int? EntityId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public string? MachineName { get; set; }
    public string? OldValuesJson { get; set; }
    public string? NewValuesJson { get; set; }
    public bool ManagerApproval { get; set; }
    public string? ApprovalReason { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
