using PharmaPOS.Domain.Common;
using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Domain.Entities.Sales;

/// <summary>Refund payment issued for a sale return (does not modify original sale payments).</summary>
public class ReturnRefund : BaseEntity
{
    public int SaleReturnId { get; set; }
    public SaleReturn? SaleReturn { get; set; }

    public RefundMode RefundMode { get; set; }
    public decimal Amount { get; set; }
    public string? TransactionReference { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Refunded;
    public DateTime RefundDateUtc { get; set; } = DateTime.UtcNow;
}
