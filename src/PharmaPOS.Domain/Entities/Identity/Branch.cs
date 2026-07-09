using PharmaPOS.Domain.Common;
using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Domain.Entities.Identity;

/// <summary>
/// A physical store/branch. Central to the multi-branch model: stock, sales,
/// purchases and cash are tracked per branch while reports can be consolidated.
/// </summary>
public class Branch : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Pincode { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? GstNumber { get; set; }
    public string? DrugLicenseNumber { get; set; }
    public bool IsHeadOffice { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;

    public ICollection<User> Users { get; set; } = new List<User>();
}
