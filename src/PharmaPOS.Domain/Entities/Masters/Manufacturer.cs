using PharmaPOS.Domain.Common;
using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Domain.Entities.Masters;

/// <summary>Pharmaceutical company that manufactures medicines.</summary>
public class Manufacturer : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? GstNumber { get; set; }
    public string? LicenseNumber { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Pincode { get; set; }
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;

    public ICollection<Medicine> Medicines { get; set; } = new List<Medicine>();
}
