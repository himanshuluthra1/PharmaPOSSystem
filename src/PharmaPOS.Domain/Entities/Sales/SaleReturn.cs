using PharmaPOS.Domain.Common;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Domain.Entities.Masters;

namespace PharmaPOS.Domain.Entities.Sales;

/// <summary>Customer sale return header linked to the original sale invoice.</summary>
public class SaleReturn : BranchEntity
{
    public string ReturnNumber { get; set; } = string.Empty;
    public int SaleId { get; set; }
    public Sale? Sale { get; set; }

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public DateTime ReturnDate { get; set; } = DateTime.UtcNow;
    public RefundMode RefundMode { get; set; } = RefundMode.Cash;
    public decimal RefundAmount { get; set; }
    public decimal SubTotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal RoundOff { get; set; }
    public decimal GrandTotal { get; set; }

    public int RewardPointsReversed { get; set; }
    public int RewardPointsRestored { get; set; }

    public SaleReturnStatus Status { get; set; } = SaleReturnStatus.Completed;
    public bool IsFullReturn { get; set; }
    public bool ManagerOverrideUsed { get; set; }
    public string? ManagerOverrideReason { get; set; }
    public string? Remarks { get; set; }

    public int? ExchangeSaleId { get; set; }
    public decimal ExchangeAmount { get; set; }
    public decimal CustomerPaysAmount { get; set; }
    public decimal CustomerReceivesAmount { get; set; }

    public ICollection<SaleReturnItem> Items { get; set; } = new List<SaleReturnItem>();
    public ICollection<ReturnRefund> Refunds { get; set; } = new List<ReturnRefund>();
    public CreditNote? CreditNote { get; set; }
}
