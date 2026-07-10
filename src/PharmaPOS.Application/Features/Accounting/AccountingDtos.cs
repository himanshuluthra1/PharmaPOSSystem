using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Application.Features.Accounting;

public enum PartyLedgerKind
{
    Supplier,
    Customer
}

public enum VoucherKind
{
    Payment,
    Receipt,
    Expense
}

public sealed class VoucherKindOption(VoucherKind kind, string label)
{
    public VoucherKind Kind { get; } = kind;
    public string Label { get; } = label;
}

public sealed class PartyKindOption(PartyLedgerKind kind, string label)
{
    public PartyLedgerKind Kind { get; } = kind;
    public string Label { get; } = label;
}

public class AccountingSummaryDto
{
    public decimal TotalReceivables { get; set; }
    public decimal TotalPayables { get; set; }
    public decimal CashInHand { get; set; }
    public decimal BankBalance { get; set; }
    public int ReceivableParties { get; set; }
    public int PayableParties { get; set; }
}

public record PartyLedgerRowDto(
    int PartyId,
    string Name,
    string? Phone,
    decimal OutstandingBalance);

public record PartyBillRowDto(
    int TransactionId,
    string InvoiceNumber,
    DateTime InvoiceDate,
    decimal GrandTotal,
    decimal PaidAmount,
    decimal BalanceDue)
{
    public string InvoiceDateLabel => InvoiceDate.ToString("dd/MM/yyyy hh:mm tt");
}

public record AccountLookupDto(
    int Id,
    string Code,
    string Name,
    AccountType Type)
{
    public string DisplayLabel => $"{Code} — {Name}";
}

public class CreatePaymentRequest
{
    public int SupplierId { get; set; }
    public decimal Amount { get; set; }
    public int CashOrBankAccountId { get; set; }
    public DateTime EntryDate { get; set; } = DateTime.Today;
    public string? Narration { get; set; }
}

public class CreateReceiptRequest
{
    public int CustomerId { get; set; }
    public decimal Amount { get; set; }
    public int CashOrBankAccountId { get; set; }
    public DateTime EntryDate { get; set; } = DateTime.Today;
    public string? Narration { get; set; }
}

public class CreateExpenseRequest
{
    public int ExpenseAccountId { get; set; }
    public int CashOrBankAccountId { get; set; }
    public decimal Amount { get; set; }
    public DateTime EntryDate { get; set; } = DateTime.Today;
    public string? Narration { get; set; }
}

public class VoucherReceiptDto
{
    public int JournalEntryId { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public DateTime EntryDate { get; set; }
    public decimal Amount { get; set; }
}

public record CashBookRowDto(
    DateTime EntryDate,
    string VoucherNumber,
    string? Narration,
    decimal Debit,
    decimal Credit,
    decimal RunningBalance)
{
    public string EntryDateLabel => EntryDate.ToString("dd/MM/yyyy");
}

public record JournalEntryListDto(
    int Id,
    string VoucherNumber,
    DateTime EntryDate,
    string? Narration,
    string? ReferenceType,
    decimal TotalAmount)
{
    public string EntryDateLabel => EntryDate.ToString("dd/MM/yyyy");
}

public record JournalLineDto(
    string AccountCode,
    string AccountName,
    LedgerEntryType EntryType,
    decimal Amount,
    string? Remarks)
{
    public string EntryTypeLabel => EntryType == LedgerEntryType.Debit ? "Dr" : "Cr";
}

public record TrialBalanceRowDto(
    string Code,
    string Name,
    AccountType Type,
    decimal Debit,
    decimal Credit);
