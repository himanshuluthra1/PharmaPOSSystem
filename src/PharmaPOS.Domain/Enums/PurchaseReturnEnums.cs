namespace PharmaPOS.Domain.Enums;

public enum PurchaseReturnStatus
{
    Draft = 0,
    Completed = 1,
    Cancelled = 2
}

/// <summary>How the return value is settled with the supplier.</summary>
public enum PurchaseReturnSettlementMode
{
    /// <summary>Reduce supplier outstanding / create credit pending debit note.</summary>
    SupplierCredit = 0,
    /// <summary>Supplier refunds cash/bank (still recorded; receipt # attached later).</summary>
    CashRefund = 1
}
