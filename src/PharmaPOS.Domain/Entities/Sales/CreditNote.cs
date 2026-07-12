using PharmaPOS.Domain.Common;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Domain.Entities.Masters;

namespace PharmaPOS.Domain.Entities.Sales;

/// <summary>Credit note issued instead of cash/card refund.</summary>
public class CreditNote : BaseEntity
{
    public string CreditNoteNumber { get; set; } = string.Empty;
    public int SaleReturnId { get; set; }
    public SaleReturn? SaleReturn { get; set; }

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public decimal Amount { get; set; }
    public decimal RedeemedAmount { get; set; }
    public DateTime IssueDate { get; set; } = DateTime.UtcNow;
    public DateTime ExpiryDate { get; set; }
    public CreditNoteStatus Status { get; set; } = CreditNoteStatus.Active;
    public string? Remarks { get; set; }
}
