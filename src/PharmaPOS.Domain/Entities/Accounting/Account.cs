using PharmaPOS.Domain.Common;
using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Domain.Entities.Accounting;

/// <summary>A chart-of-accounts ledger head (cash, bank, sales, purchase, etc.).</summary>
public class Account : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AccountType Type { get; set; }
    public int? ParentAccountId { get; set; }
    public Account? ParentAccount { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal CurrentBalance { get; set; }
    public bool IsSystemAccount { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;
}
