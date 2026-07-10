using PharmaPOS.Domain.Enums;
using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.Accounting;

public interface IAccountingService
{
    Task<AccountingSummaryDto> GetSummaryAsync(int? branchId, CancellationToken ct = default);

    Task<List<PartyLedgerRowDto>> ListPartyLedgersAsync(
        PartyLedgerKind kind,
        string term,
        int? branchId,
        CancellationToken ct = default);

    Task<List<PartyBillRowDto>> ListPartyBillsAsync(
        PartyLedgerKind kind,
        int partyId,
        int? branchId,
        CancellationToken ct = default);

    Task<List<AccountLookupDto>> ListAccountsAsync(
        AccountType? type = null,
        CancellationToken ct = default);

    Task<List<AccountLookupDto>> ListCashAndBankAccountsAsync(CancellationToken ct = default);

    Task<List<CashBookRowDto>> GetCashBookAsync(int? branchId, CancellationToken ct = default);

    Task<List<JournalEntryListDto>> ListJournalEntriesAsync(
        string? term,
        int? branchId,
        int take = 300,
        CancellationToken ct = default);

    Task<List<JournalLineDto>> GetJournalLinesAsync(int journalEntryId, CancellationToken ct = default);

    Task<List<TrialBalanceRowDto>> GetTrialBalanceAsync(CancellationToken ct = default);

    Task<string> PreviewNextVoucherNumberAsync(VoucherKind kind, int? branchId, CancellationToken ct = default);

    Task<Result<VoucherReceiptDto>> CreatePaymentAsync(
        CreatePaymentRequest request,
        int? branchId,
        CancellationToken ct = default);

    Task<Result<VoucherReceiptDto>> CreateReceiptAsync(
        CreateReceiptRequest request,
        int? branchId,
        CancellationToken ct = default);

    Task<Result<VoucherReceiptDto>> CreateExpenseAsync(
        CreateExpenseRequest request,
        int? branchId,
        CancellationToken ct = default);
}
