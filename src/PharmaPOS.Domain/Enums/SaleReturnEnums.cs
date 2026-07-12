namespace PharmaPOS.Domain.Enums;

public enum SaleReturnStatus
{
    Draft = 0,
    Completed = 1,
    Cancelled = 2
}

public enum RefundMode
{
    Cash = 0,
    Card = 1,
    Upi = 2,
    Wallet = 3,
    StoreCredit = 4,
    Exchange = 5,
    CreditNote = 6
}

public enum CreditNoteStatus
{
    Active = 0,
    PartiallyRedeemed = 1,
    Redeemed = 2,
    Expired = 3,
    Cancelled = 4
}
