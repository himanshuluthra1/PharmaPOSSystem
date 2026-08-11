using PharmaPOS.Domain.Common;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Domain.Entities.Identity;

namespace PharmaPOS.Domain.Entities.Sales;

/// <summary>
/// A physical billing counter / till within a branch. Multiple counters share
/// the same stock but keep separate operators and cash collections.
/// </summary>
public class BillingCounter : BranchEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;

    public ICollection<CounterSession> Sessions { get; set; } = new List<CounterSession>();
}

/// <summary>
/// An open (or closed) operator session on a billing counter. Cash collections
/// for the session are derived from sales stamped with this session id.
/// </summary>
public class CounterSession : BaseEntity
{
    public int CounterId { get; set; }
    public BillingCounter? Counter { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public DateTime OpenedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAtUtc { get; set; }

    public decimal OpeningFloat { get; set; }
    public decimal? DeclaredClosingCash { get; set; }
    public string? MachineName { get; set; }
    public string? Remarks { get; set; }

    public CounterSessionStatus Status { get; set; } = CounterSessionStatus.Open;
}
