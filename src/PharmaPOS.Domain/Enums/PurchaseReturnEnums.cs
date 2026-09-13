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

public enum PurchaseReturnKind
{
    Standard = 0,
    ExpiryToCompany = 1
}

public enum ExpiryClaimStatus
{
    AwaitingCreditNote = 0,
    CreditReceived = 1,
    Cancelled = 2
}

/// <summary>How supplier credit for an expiry claim was documented.</summary>
public enum ExpiryCreditSettlementKind
{
    /// <summary>Standalone credit / debit note from the company.</summary>
    CreditNote = 0,
    /// <summary>Credit adjusted on a subsequent purchase bill.</summary>
    PurchaseBill = 1
}

/// <summary>How the supplier documented credit for a purchase return.</summary>
public enum PurchaseReturnReceiptSettlementKind
{
    /// <summary>Supplier return receipt / debit note number.</summary>
    SupplierReceipt = 0,
    /// <summary>Credit adjusted on a subsequent purchase bill.</summary>
    PurchaseBill = 1
}
