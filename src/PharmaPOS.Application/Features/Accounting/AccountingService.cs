using Microsoft.EntityFrameworkCore;
using PharmaPOS.Application.Common;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Domain.Entities.Accounting;
using PharmaPOS.Domain.Entities.Masters;
using PharmaPOS.Domain.Entities.Purchases;
using PharmaPOS.Domain.Entities.Sales;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.Accounting;

public class AccountingService : IAccountingService
{
    private const string CashAccountCode = "1000";
    private const string BankAccountCode = "1010";
    private const string ReceivableAccountCode = "1200";
    private const string PayableAccountCode = "2000";

    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _clock;

    public AccountingService(IUnitOfWork uow, IDateTimeProvider clock)
    {
        _uow = uow;
        _clock = clock;
    }

    public async Task<AccountingSummaryDto> GetSummaryAsync(int? branchId, CancellationToken ct = default)
    {
        var suppliers = _uow.Repository<Supplier>().Query()
            .Where(s => s.Status == EntityStatus.Active);
        if (branchId.HasValue) suppliers = suppliers.Where(s => s.BranchId == branchId);

        var customers = _uow.Repository<Customer>().Query()
            .Where(c => c.Status == EntityStatus.Active);
        if (branchId.HasValue) customers = customers.Where(c => c.BranchId == branchId);

        var cash = await GetAccountByCodeAsync(CashAccountCode, ct);
        var bank = await GetAccountByCodeAsync(BankAccountCode, ct);

        return new AccountingSummaryDto
        {
            TotalPayables = await suppliers.SumAsync(s => (decimal?)s.OutstandingBalance, ct) ?? 0m,
            TotalReceivables = await customers.SumAsync(c => (decimal?)c.OutstandingBalance, ct) ?? 0m,
            PayableParties = await suppliers.CountAsync(s => s.OutstandingBalance > 0, ct),
            ReceivableParties = await customers.CountAsync(c => c.OutstandingBalance > 0, ct),
            CashInHand = cash?.CurrentBalance ?? 0m,
            BankBalance = bank?.CurrentBalance ?? 0m
        };
    }

    public async Task<List<PartyLedgerRowDto>> ListPartyLedgersAsync(
        PartyLedgerKind kind,
        string term,
        int? branchId,
        CancellationToken ct = default)
    {
        term = term.Trim();

        if (kind == PartyLedgerKind.Supplier)
        {
            var q = _uow.Repository<Supplier>().Query()
                .Where(s => s.Status == EntityStatus.Active);
            if (branchId.HasValue) q = q.Where(s => s.BranchId == branchId);
            if (!string.IsNullOrWhiteSpace(term))
            {
                var normalized = SearchQueryExtensions.NormalizeTerm(term);
                q = q.WhereSupplierMatches(normalized);
            }

            return await q
                .OrderByDescending(s => s.OutstandingBalance)
                .ThenBy(s => s.Name)
                .Select(s => new PartyLedgerRowDto(s.Id, s.Name, s.Phone, s.OutstandingBalance))
                .Take(500)
                .ToListAsync(ct);
        }

        var cq = _uow.Repository<Customer>().Query()
            .Where(c => c.Status == EntityStatus.Active);
        if (branchId.HasValue) cq = cq.Where(c => c.BranchId == branchId);
        if (!string.IsNullOrWhiteSpace(term))
            cq = cq.Where(c => c.Name.Contains(term) || (c.Phone != null && c.Phone.Contains(term)));

        return await cq
            .OrderByDescending(c => c.OutstandingBalance)
            .ThenBy(c => c.Name)
            .Select(c => new PartyLedgerRowDto(c.Id, c.Name, c.Phone, c.OutstandingBalance))
            .Take(500)
            .ToListAsync(ct);
    }

    public Task<List<PartyBillRowDto>> ListPartyBillsAsync(
        PartyLedgerKind kind,
        int partyId,
        int? branchId,
        CancellationToken ct = default)
    {
        if (kind == PartyLedgerKind.Supplier)
        {
            var q = _uow.Repository<Purchase>().Query().AsNoTracking()
                .Where(p => p.SupplierId == partyId && p.Status == PurchaseStatus.Received);
            if (branchId.HasValue) q = q.Where(p => p.BranchId == branchId);

            return q
                .Where(p => p.GrandTotal > p.PaidAmount)
                .OrderByDescending(p => p.InvoiceDate)
                .Select(p => new PartyBillRowDto(
                    p.Id,
                    p.InvoiceNumber,
                    p.InvoiceDate,
                    p.GrandTotal,
                    p.PaidAmount,
                    p.GrandTotal > p.PaidAmount ? p.GrandTotal - p.PaidAmount : 0m))
                .ToListAsync(ct);
        }

        var sq = _uow.Repository<Sale>().Query().AsNoTracking()
            .Where(s => s.CustomerId == partyId && s.Status == SaleStatus.Completed);
        if (branchId.HasValue) sq = sq.Where(s => s.BranchId == branchId);

        return sq
            .Where(s => s.GrandTotal > s.PaidAmount)
            .OrderByDescending(s => s.InvoiceDate)
            .Select(s => new PartyBillRowDto(
                s.Id,
                s.InvoiceNumber,
                s.InvoiceDate,
                s.GrandTotal,
                s.PaidAmount,
                s.GrandTotal > s.PaidAmount ? s.GrandTotal - s.PaidAmount : 0m))
            .ToListAsync(ct);
    }

    public async Task<List<AccountLookupDto>> ListAccountsAsync(
        AccountType? type = null,
        CancellationToken ct = default)
    {
        var q = _uow.Repository<Account>().Query()
            .Where(a => a.Status == EntityStatus.Active);
        if (type.HasValue) q = q.Where(a => a.Type == type.Value);

        return await q
            .OrderBy(a => a.Code)
            .Select(a => new AccountLookupDto(a.Id, a.Code, a.Name, a.Type))
            .ToListAsync(ct);
    }

    public Task<List<AccountLookupDto>> ListCashAndBankAccountsAsync(CancellationToken ct = default)
    {
        var codes = new[] { CashAccountCode, BankAccountCode };
        return _uow.Repository<Account>().Query()
            .Where(a => a.Status == EntityStatus.Active && codes.Contains(a.Code))
            .OrderBy(a => a.Code)
            .Select(a => new AccountLookupDto(a.Id, a.Code, a.Name, a.Type))
            .ToListAsync(ct);
    }

    public async Task<List<CashBookRowDto>> GetCashBookAsync(int? branchId, CancellationToken ct = default)
    {
        var cash = await GetAccountByCodeAsync(CashAccountCode, ct);
        if (cash is null) return [];

        var q = _uow.Repository<JournalLine>().Query().AsNoTracking()
            .Include(l => l.JournalEntry)
            .Where(l => l.AccountId == cash.Id && l.JournalEntry != null);

        if (branchId.HasValue)
            q = q.Where(l => l.JournalEntry!.BranchId == branchId);

        var lines = await q
            .OrderBy(l => l.JournalEntry!.EntryDate)
            .ThenBy(l => l.JournalEntryId)
            .ThenBy(l => l.Id)
            .Select(l => new
            {
                l.JournalEntry!.EntryDate,
                l.JournalEntry.VoucherNumber,
                l.JournalEntry.Narration,
                l.EntryType,
                l.Amount
            })
            .ToListAsync(ct);

        var balance = cash.OpeningBalance;
        var rows = new List<CashBookRowDto>(lines.Count);
        foreach (var line in lines)
        {
            var debit = line.EntryType == LedgerEntryType.Debit ? line.Amount : 0m;
            var credit = line.EntryType == LedgerEntryType.Credit ? line.Amount : 0m;
            balance += debit - credit;
            rows.Add(new CashBookRowDto(
                line.EntryDate,
                line.VoucherNumber,
                line.Narration,
                debit,
                credit,
                balance));
        }

        return rows;
    }

    public async Task<List<JournalEntryListDto>> ListJournalEntriesAsync(
        string? term,
        int? branchId,
        int take = 300,
        CancellationToken ct = default)
    {
        term = term?.Trim() ?? string.Empty;
        var q = _uow.Repository<JournalEntry>().Query().AsNoTracking();
        if (branchId.HasValue) q = q.Where(e => e.BranchId == branchId);

        if (!string.IsNullOrWhiteSpace(term))
        {
            q = q.Where(e =>
                e.VoucherNumber.Contains(term) ||
                (e.Narration != null && e.Narration.Contains(term)) ||
                (e.ReferenceType != null && e.ReferenceType.Contains(term)));
        }

        return await q
            .OrderByDescending(e => e.EntryDate)
            .ThenByDescending(e => e.Id)
            .Take(take)
            .Select(e => new JournalEntryListDto(
                e.Id,
                e.VoucherNumber,
                e.EntryDate,
                e.Narration,
                e.ReferenceType,
                e.Lines.Sum(l => l.EntryType == LedgerEntryType.Debit ? l.Amount : 0m)))
            .ToListAsync(ct);
    }

    public Task<List<JournalLineDto>> GetJournalLinesAsync(int journalEntryId, CancellationToken ct = default)
        => _uow.Repository<JournalLine>().Query().AsNoTracking()
            .Where(l => l.JournalEntryId == journalEntryId)
            .OrderBy(l => l.EntryType)
            .ThenBy(l => l.Id)
            .Select(l => new JournalLineDto(
                l.Account != null ? l.Account.Code : "",
                l.Account != null ? l.Account.Name : "",
                l.EntryType,
                l.Amount,
                l.Remarks))
            .ToListAsync(ct);

    public async Task<List<TrialBalanceRowDto>> GetTrialBalanceAsync(CancellationToken ct = default)
    {
        var accounts = await _uow.Repository<Account>().Query()
            .Where(a => a.Status == EntityStatus.Active)
            .OrderBy(a => a.Code)
            .ToListAsync(ct);

        return accounts.Select(a =>
        {
            var balance = a.OpeningBalance + a.CurrentBalance;
            var (debit, credit) = ToTrialBalance(balance, a.Type);
            return new TrialBalanceRowDto(a.Code, a.Name, a.Type, debit, credit);
        }).ToList();
    }

    public Task<string> PreviewNextVoucherNumberAsync(VoucherKind kind, int? branchId, CancellationToken ct = default)
        => GenerateVoucherNumberAsync(kind, branchId, ct);

    public Task<Result<VoucherReceiptDto>> CreatePaymentAsync(
        CreatePaymentRequest request,
        int? branchId,
        CancellationToken ct = default)
        => PostPartyVoucherAsync(
            VoucherKind.Payment,
            request.SupplierId,
            PartyLedgerKind.Supplier,
            request.Amount,
            request.CashOrBankAccountId,
            request.EntryDate,
            request.Narration,
            branchId,
            ct);

    public Task<Result<VoucherReceiptDto>> CreateReceiptAsync(
        CreateReceiptRequest request,
        int? branchId,
        CancellationToken ct = default)
        => PostPartyVoucherAsync(
            VoucherKind.Receipt,
            request.CustomerId,
            PartyLedgerKind.Customer,
            request.Amount,
            request.CashOrBankAccountId,
            request.EntryDate,
            request.Narration,
            branchId,
            ct);

    public async Task<Result<VoucherReceiptDto>> CreateExpenseAsync(
        CreateExpenseRequest request,
        int? branchId,
        CancellationToken ct = default)
    {
        if (request.Amount <= 0)
            return Result.Failure<VoucherReceiptDto>("Enter a valid expense amount.");

        try
        {
            var receipt = await _uow.ExecuteInTransactionAsync(async token =>
            {
                var expenseAccount = await _uow.Repository<Account>().GetByIdAsync(request.ExpenseAccountId, token);
                var cashAccount = await _uow.Repository<Account>().GetByIdAsync(request.CashOrBankAccountId, token);
                if (expenseAccount is null || cashAccount is null)
                    throw new AccountingException("Select valid expense and cash/bank accounts.");
                if (expenseAccount.Type != AccountType.Expense)
                    throw new AccountingException("The debit account must be an expense account.");

                var voucherNumber = await GenerateVoucherNumberAsync(VoucherKind.Expense, branchId, token);
                var entry = new JournalEntry
                {
                    BranchId = branchId,
                    VoucherNumber = voucherNumber,
                    EntryDate = request.EntryDate,
                    Narration = request.Narration,
                    ReferenceType = nameof(VoucherKind.Expense)
                };
                await _uow.Repository<JournalEntry>().AddAsync(entry, token);
                await _uow.SaveChangesAsync(token);

                await AddLineAsync(entry.Id, expenseAccount, LedgerEntryType.Debit, request.Amount,
                    request.Narration, token);
                await AddLineAsync(entry.Id, cashAccount, LedgerEntryType.Credit, request.Amount,
                    request.Narration, token);
                await _uow.SaveChangesAsync(token);

                return new VoucherReceiptDto
                {
                    JournalEntryId = entry.Id,
                    VoucherNumber = voucherNumber,
                    EntryDate = request.EntryDate,
                    Amount = request.Amount
                };
            }, ct);

            return Result.Success(receipt);
        }
        catch (AccountingException ex)
        {
            return Result.Failure<VoucherReceiptDto>(ex.Message);
        }
        catch (Exception ex)
        {
            return Result.Failure<VoucherReceiptDto>($"Could not save expense: {ex.Message}");
        }
    }

    private async Task<Result<VoucherReceiptDto>> PostPartyVoucherAsync(
        VoucherKind kind,
        int partyId,
        PartyLedgerKind partyKind,
        decimal amount,
        int cashOrBankAccountId,
        DateTime entryDate,
        string? narration,
        int? branchId,
        CancellationToken ct)
    {
        if (amount <= 0)
            return Result.Failure<VoucherReceiptDto>("Enter a valid amount.");

        try
        {
            var receipt = await _uow.ExecuteInTransactionAsync(async token =>
            {
                Account partyAccount;
                string partyName;

                if (partyKind == PartyLedgerKind.Supplier)
                {
                    var supplier = await _uow.Repository<Supplier>().GetByIdAsync(partyId, token);
                    if (supplier is null) throw new AccountingException("Supplier not found.");
                    if (amount > supplier.OutstandingBalance)
                        throw new AccountingException(
                            $"Payment amount exceeds outstanding balance ({supplier.OutstandingBalance:N2}).");

                    partyAccount = await RequireAccountByCodeAsync(PayableAccountCode, token);
                    partyName = supplier.Name;
                    supplier.OutstandingBalance -= amount;
                    _uow.Repository<Supplier>().Update(supplier);
                    await AllocateSupplierPaymentAsync(partyId, amount, branchId, token);
                }
                else
                {
                    var customer = await _uow.Repository<Customer>().GetByIdAsync(partyId, token);
                    if (customer is null) throw new AccountingException("Customer not found.");
                    if (amount > customer.OutstandingBalance)
                        throw new AccountingException(
                            $"Receipt amount exceeds outstanding balance ({customer.OutstandingBalance:N2}).");

                    partyAccount = await RequireAccountByCodeAsync(ReceivableAccountCode, token);
                    partyName = customer.Name;
                    customer.OutstandingBalance -= amount;
                    _uow.Repository<Customer>().Update(customer);
                    await AllocateCustomerReceiptAsync(partyId, amount, branchId, token);
                }

                var cashAccount = await _uow.Repository<Account>().GetByIdAsync(cashOrBankAccountId, token);
                if (cashAccount is null || cashAccount.Type != AccountType.Asset)
                    throw new AccountingException("Select a valid cash or bank account.");

                var voucherNumber = await GenerateVoucherNumberAsync(kind, branchId, token);
                var entry = new JournalEntry
                {
                    BranchId = branchId,
                    VoucherNumber = voucherNumber,
                    EntryDate = entryDate,
                    Narration = narration ?? $"{kind} — {partyName}",
                    ReferenceType = kind.ToString(),
                    ReferenceId = partyId
                };
                await _uow.Repository<JournalEntry>().AddAsync(entry, token);
                await _uow.SaveChangesAsync(token);

                if (kind == VoucherKind.Payment)
                {
                    await AddLineAsync(entry.Id, partyAccount, LedgerEntryType.Debit, amount, narration, token);
                    await AddLineAsync(entry.Id, cashAccount, LedgerEntryType.Credit, amount, narration, token);
                }
                else
                {
                    await AddLineAsync(entry.Id, cashAccount, LedgerEntryType.Debit, amount, narration, token);
                    await AddLineAsync(entry.Id, partyAccount, LedgerEntryType.Credit, amount, narration, token);
                }

                await _uow.SaveChangesAsync(token);

                return new VoucherReceiptDto
                {
                    JournalEntryId = entry.Id,
                    VoucherNumber = voucherNumber,
                    EntryDate = entryDate,
                    Amount = amount
                };
            }, ct);

            return Result.Success(receipt);
        }
        catch (AccountingException ex)
        {
            return Result.Failure<VoucherReceiptDto>(ex.Message);
        }
        catch (Exception ex)
        {
            return Result.Failure<VoucherReceiptDto>($"Could not save voucher: {ex.Message}");
        }
    }

    private async Task AllocateSupplierPaymentAsync(
        int supplierId, decimal amount, int? branchId, CancellationToken ct)
    {
        var remaining = amount;
        var q = _uow.Repository<Purchase>().Query()
            .Where(p => p.SupplierId == supplierId
                        && p.Status == PurchaseStatus.Received
                        && p.GrandTotal > p.PaidAmount);
        if (branchId.HasValue) q = q.Where(p => p.BranchId == branchId);

        var bills = await q
            .OrderBy(p => p.InvoiceDate)
            .ThenBy(p => p.Id)
            .ToListAsync(ct);

        foreach (var bill in bills)
        {
            if (remaining <= 0) break;

            var due = bill.GrandTotal - bill.PaidAmount;
            if (due <= 0) continue;

            var applied = Math.Min(remaining, due);
            bill.PaidAmount += applied;
            bill.PaymentStatus = bill.PaidAmount >= bill.GrandTotal
                ? PaymentStatus.Paid
                : PaymentStatus.PartiallyPaid;
            _uow.Repository<Purchase>().Update(bill);
            remaining -= applied;
        }
    }

    private async Task AllocateCustomerReceiptAsync(
        int customerId, decimal amount, int? branchId, CancellationToken ct)
    {
        var remaining = amount;
        var q = _uow.Repository<Sale>().Query()
            .Where(s => s.CustomerId == customerId
                        && s.Status == SaleStatus.Completed
                        && s.GrandTotal > s.PaidAmount);
        if (branchId.HasValue) q = q.Where(s => s.BranchId == branchId);

        var bills = await q
            .OrderBy(s => s.InvoiceDate)
            .ThenBy(s => s.Id)
            .ToListAsync(ct);

        foreach (var bill in bills)
        {
            if (remaining <= 0) break;

            var due = bill.GrandTotal - bill.PaidAmount;
            if (due <= 0) continue;

            var applied = Math.Min(remaining, due);
            bill.PaidAmount += applied;
            bill.PaymentStatus = bill.PaidAmount >= bill.GrandTotal
                ? PaymentStatus.Paid
                : PaymentStatus.PartiallyPaid;
            _uow.Repository<Sale>().Update(bill);
            remaining -= applied;
        }
    }

    private async Task AddLineAsync(
        int journalEntryId,
        Account account,
        LedgerEntryType entryType,
        decimal amount,
        string? remarks,
        CancellationToken ct)
    {
        await _uow.Repository<JournalLine>().AddAsync(new JournalLine
        {
            JournalEntryId = journalEntryId,
            AccountId = account.Id,
            EntryType = entryType,
            Amount = amount,
            Remarks = remarks
        }, ct);

        ApplyBalanceChange(account, entryType, amount);
        _uow.Repository<Account>().Update(account);
    }

    private static void ApplyBalanceChange(Account account, LedgerEntryType entryType, decimal amount)
    {
        var isDebit = entryType == LedgerEntryType.Debit;
        var naturalDebit = account.Type is AccountType.Asset or AccountType.Expense;

        account.CurrentBalance += naturalDebit
            ? (isDebit ? amount : -amount)
            : (isDebit ? -amount : amount);
    }

    private static (decimal Debit, decimal Credit) ToTrialBalance(decimal balance, AccountType type)
    {
        var naturalDebit = type is AccountType.Asset or AccountType.Expense;
        if (balance == 0) return (0m, 0m);

        if (naturalDebit)
            return balance >= 0 ? (balance, 0m) : (0m, -balance);

        return balance >= 0 ? (0m, balance) : (-balance, 0m);
    }

    private async Task<Account?> GetAccountByCodeAsync(string code, CancellationToken ct)
        => await _uow.Repository<Account>().Query()
            .FirstOrDefaultAsync(a => a.Code == code, ct);

    private async Task<Account> RequireAccountByCodeAsync(string code, CancellationToken ct)
    {
        var account = await GetAccountByCodeAsync(code, ct);
        if (account is null)
            throw new AccountingException($"System account {code} is not configured.");
        return account;
    }

    private async Task<string> GenerateVoucherNumberAsync(VoucherKind kind, int? branchId, CancellationToken ct)
    {
        var prefix = kind switch
        {
            VoucherKind.Payment => "PAY",
            VoucherKind.Receipt => "RCT",
            VoucherKind.Expense => "EXP",
            _ => "JV"
        };

        var today = _clock.Today;
        var tomorrow = today.AddDays(1);
        var q = _uow.Repository<JournalEntry>().Query()
            .Where(e => e.EntryDate >= today && e.EntryDate < tomorrow && e.VoucherNumber.StartsWith(prefix));
        if (branchId.HasValue) q = q.Where(e => e.BranchId == branchId);

        var count = await q.CountAsync(ct);
        return $"{prefix}-{today:yyyyMMdd}-{count + 1:D4}";
    }

    private sealed class AccountingException : Exception
    {
        public AccountingException(string message) : base(message) { }
    }
}
