using PharmaPOS.Domain.Common;
using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Domain.Entities.Accounting;

/// <summary>
/// A double-entry journal voucher. Each voucher has balanced debit/credit lines
/// posted against accounts, forming the general ledger.
/// </summary>
public class JournalEntry : BranchEntity
{
    public string VoucherNumber { get; set; } = string.Empty;
    public DateTime EntryDate { get; set; } = DateTime.UtcNow;
    public string? Narration { get; set; }
    public string? ReferenceType { get; set; }
    public int? ReferenceId { get; set; }

    public ICollection<JournalLine> Lines { get; set; } = new List<JournalLine>();
}

/// <summary>A single debit or credit posting within a <see cref="JournalEntry"/>.</summary>
public class JournalLine : BaseEntity
{
    public int JournalEntryId { get; set; }
    public JournalEntry? JournalEntry { get; set; }

    public int AccountId { get; set; }
    public Account? Account { get; set; }

    public LedgerEntryType EntryType { get; set; }
    public decimal Amount { get; set; }
    public string? Remarks { get; set; }
}
