using PharmaPOS.Domain.Common;
using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Domain.Entities.Masters;

/// <summary>Vendor/distributor from whom stock is purchased.</summary>
public class Supplier : BranchEntity
{
    public string Name { get; set; } = string.Empty;
    /// <summary>Persisted <see cref="Name"/> without spaces for indexed search.</summary>
    public string NameSearchKey { get; private set; } = string.Empty;
    public string? GstNumber { get; set; }
    public string? DrugLicenseNumber { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Pincode { get; set; }
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    /// <summary>Persisted <see cref="Phone"/> without spaces for indexed search.</summary>
    public string PhoneSearchKey { get; private set; } = string.Empty;
    public string? Email { get; set; }

    public int PaymentTermsDays { get; set; }
    public decimal OpeningBalance { get; set; }
    /// <summary>Current outstanding payable to this supplier (opening +/- transactions).</summary>
    public decimal OutstandingBalance { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;
}
