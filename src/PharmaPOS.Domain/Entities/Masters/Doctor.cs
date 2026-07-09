using PharmaPOS.Domain.Common;
using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Domain.Entities.Masters;

/// <summary>Prescribing doctor referenced on prescriptions and invoices.</summary>
public class Doctor : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Qualification { get; set; }
    public string? Specialization { get; set; }
    public string? RegistrationNumber { get; set; }
    public string? Hospital { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;
}
