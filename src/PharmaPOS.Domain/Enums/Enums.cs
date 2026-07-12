namespace PharmaPOS.Domain.Enums;

public enum DosageForm
{
    Tablet = 0,
    Capsule = 1,
    Syrup = 2,
    Injection = 3,
    Ointment = 4,
    Cream = 5,
    Drops = 6,
    Inhaler = 7,
    Powder = 8,
    Gel = 9,
    Lotion = 10,
    Suspension = 11,
    Suppository = 12,
    Spray = 13,
    Other = 99
}

/// <summary>Indian schedule drug classification governing sale restrictions.</summary>
public enum ScheduleDrugType
{
    None = 0,
    ScheduleH = 1,
    ScheduleH1 = 2,
    ScheduleX = 3,
    ScheduleG = 4,
    Otc = 5
}

public enum EntityStatus
{
    Inactive = 0,
    Active = 1
}

public enum CustomerType
{
    Retail = 0,
    Regular = 1,
    Doctor = 2,
    Hospital = 3,
    Clinic = 4,
    Corporate = 5
}

public enum PaymentMethod
{
    Cash = 0,
    Card = 1,
    Upi = 2,
    Wallet = 3,
    Credit = 4,
    BankTransfer = 5,
    Cheque = 6
}

public enum PaymentStatus
{
    Unpaid = 0,
    PartiallyPaid = 1,
    Paid = 2,
    Refunded = 3
}

public enum SaleStatus
{
    Draft = 0,
    Hold = 1,
    Completed = 2,
    Returned = 3,
    Cancelled = 4,
    PartiallyReturned = 5
}

public enum PurchaseStatus
{
    Draft = 0,
    Ordered = 1,
    PartiallyReceived = 2,
    Received = 3,
    Returned = 4,
    Cancelled = 5
}

public enum StockMovementType
{
    PurchaseIn = 0,
    SaleOut = 1,
    PurchaseReturn = 2,
    SaleReturn = 3,
    AdjustmentIn = 4,
    AdjustmentOut = 5,
    TransferIn = 6,
    TransferOut = 7,
    Damage = 8,
    Expiry = 9,
    OpeningStock = 10,
  NonSaleableIn = 11
}

/// <summary>Batch consumption strategy for stock allocation.</summary>
public enum StockValuationMethod
{
    Fifo = 0,
    Fefo = 1
}

public enum LedgerEntryType
{
    Debit = 0,
    Credit = 1
}

public enum AccountType
{
    Asset = 0,
    Liability = 1,
    Equity = 2,
    Income = 3,
    Expense = 4
}

public enum NotificationType
{
    LowStock = 0,
    Expiry = 1,
    PaymentDue = 2,
    LicenseRenewal = 3,
    GstReturn = 4,
    Backup = 5,
    SystemUpdate = 6,
    General = 7
}

public enum NotificationSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2
}
