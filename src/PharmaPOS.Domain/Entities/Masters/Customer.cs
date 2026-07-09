using PharmaPOS.Domain.Common;
using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Domain.Entities.Masters;

/// <summary>
/// A buyer: walk-in retail, regular, doctor, hospital, clinic or corporate account.
/// Carries credit and loyalty information used by the sales and CRM modules.
/// </summary>
public class Customer : BranchEntity
{
    public string Name { get; set; } = string.Empty;
    public CustomerType Type { get; set; } = CustomerType.Retail;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? GstNumber { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Gender { get; set; }

    public decimal CreditLimit { get; set; }
    public decimal OutstandingBalance { get; set; }
    public int RewardPoints { get; set; }
    public bool IsMember { get; set; }
    public string? MembershipNumber { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;
}
