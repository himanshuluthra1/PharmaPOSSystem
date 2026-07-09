using PharmaPOS.Domain.Common;
using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Domain.Entities.Masters;

/// <summary>Staff member record for HR, attendance and commission tracking.</summary>
public class Employee : BranchEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Designation { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public DateTime? DateOfJoining { get; set; }
    public decimal Salary { get; set; }
    public decimal CommissionPercent { get; set; }
    public string? Shift { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;
}
