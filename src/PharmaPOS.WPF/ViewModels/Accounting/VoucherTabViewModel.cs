using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Accounting;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Shared.Results;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Accounting;

public class VoucherTabViewModel : ObservableObject
{
    private readonly IAccountingService _accounting;
    private readonly int? _branchId;
    private readonly IDialogService _dialog;

    private VoucherKindOption _selectedKind;
    private string _partySearchText = string.Empty;
    private int? _selectedPartyId;
    private string? _selectedPartyName;
    private decimal _partyOutstanding;
    private string _voucherNumber = string.Empty;
    private DateTime _entryDate = DateTime.Today;
    private decimal _amount;
    private AccountLookupDto? _selectedCashAccount;
    private AccountLookupDto? _selectedExpenseAccount;
    private string? _narration;
    private bool _isBusy;
    private string? _statusMessage;
    private CancellationTokenSource? _searchCts;

    public VoucherTabViewModel(
        IAccountingService accounting,
        ICurrentUserService currentUser,
        IDialogService dialog)
    {
        _accounting = accounting;
        _branchId = currentUser.CurrentUser?.BranchId;
        _dialog = dialog;

        KindOptions =
        [
            new(VoucherKind.Payment, "Payment (to supplier)"),
            new(VoucherKind.Receipt, "Receipt (from customer)"),
            new(VoucherKind.Expense, "Expense entry")
        ];
        _selectedKind = KindOptions[0];

        SaveCommand = new AsyncRelayCommand(_ => SaveAsync(), _ => !IsBusy && CanSave());
        NewCommand = new RelayCommand(_ => ResetForm());
        _ = InitializeAsync();
    }

    public IReadOnlyList<VoucherKindOption> KindOptions { get; }

    public ObservableCollection<PartyLedgerRowDto> PartySuggestions { get; } = new();
    public ObservableCollection<AccountLookupDto> CashAccounts { get; } = new();
    public ObservableCollection<AccountLookupDto> ExpenseAccounts { get; } = new();

    public VoucherKindOption SelectedKind
    {
        get => _selectedKind;
        set
        {
            if (!SetProperty(ref _selectedKind, value)) return;
            OnPropertyChanged(nameof(ShowPartyFields));
            OnPropertyChanged(nameof(ShowExpenseFields));
            OnPropertyChanged(nameof(PartyHint));
            ClearParty();
            _ = PreviewVoucherAsync();
        }
    }

    public bool ShowPartyFields => SelectedKind.Kind is VoucherKind.Payment or VoucherKind.Receipt;
    public bool ShowExpenseFields => SelectedKind.Kind == VoucherKind.Expense;

    public string PartyHint => SelectedKind.Kind == VoucherKind.Payment
        ? "Search supplier"
        : "Search customer";

    public string PartySearchText
    {
        get => _partySearchText;
        set
        {
            if (SetProperty(ref _partySearchText, value))
                _ = SearchPartiesAsync();
        }
    }

    public string VoucherNumber
    {
        get => _voucherNumber;
        private set => SetProperty(ref _voucherNumber, value);
    }

    public DateTime EntryDate
    {
        get => _entryDate;
        set => SetProperty(ref _entryDate, value);
    }

    public decimal Amount
    {
        get => _amount;
        set => SetProperty(ref _amount, value);
    }

    public AccountLookupDto? SelectedCashAccount
    {
        get => _selectedCashAccount;
        set => SetProperty(ref _selectedCashAccount, value);
    }

    public AccountLookupDto? SelectedExpenseAccount
    {
        get => _selectedExpenseAccount;
        set => SetProperty(ref _selectedExpenseAccount, value);
    }

    public string? Narration
    {
        get => _narration;
        set => SetProperty(ref _narration, value);
    }

    public string? PartyOutstandingLabel =>
        _selectedPartyId.HasValue
            ? $"Outstanding: {_partyOutstanding:N2}"
            : null;

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand NewCommand { get; }

    public void PrefillPayment(int supplierId, string supplierName, decimal outstanding)
    {
        SelectedKind = KindOptions[0];
        SelectParty(supplierId, supplierName, outstanding);
        Amount = outstanding;
    }

    public void PrefillReceipt(int customerId, string customerName, decimal outstanding)
    {
        SelectedKind = KindOptions[1];
        SelectParty(customerId, customerName, outstanding);
        Amount = outstanding;
    }

    public void SelectPartySuggestion(PartyLedgerRowDto party)
    {
        SelectParty(party.PartyId, party.Name, party.OutstandingBalance);
        PartySuggestions.Clear();
        OnPropertyChanged(nameof(ShowPartySuggestions));
    }

    public bool ShowPartySuggestions => PartySuggestions.Count > 0;

    private void SelectParty(int id, string name, decimal outstanding)
    {
        _selectedPartyId = id;
        _selectedPartyName = name;
        _partyOutstanding = outstanding;
        PartySearchText = name;
        OnPropertyChanged(nameof(PartyOutstandingLabel));
    }

    private void ClearParty()
    {
        _selectedPartyId = null;
        _selectedPartyName = null;
        _partyOutstanding = 0;
        PartySearchText = string.Empty;
        PartySuggestions.Clear();
        OnPropertyChanged(nameof(PartyOutstandingLabel));
        OnPropertyChanged(nameof(ShowPartySuggestions));
    }

    private async Task InitializeAsync()
    {
        try
        {
            CashAccounts.Clear();
            foreach (var a in await _accounting.ListCashAndBankAccountsAsync())
                CashAccounts.Add(a);
            SelectedCashAccount = CashAccounts.FirstOrDefault();

            ExpenseAccounts.Clear();
            foreach (var a in await _accounting.ListAccountsAsync(AccountType.Expense))
                ExpenseAccounts.Add(a);
            SelectedExpenseAccount = ExpenseAccounts.FirstOrDefault();

            await PreviewVoucherAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private async Task PreviewVoucherAsync()
    {
        VoucherNumber = await _accounting.PreviewNextVoucherNumberAsync(SelectedKind.Kind, _branchId);
    }

    private async Task SearchPartiesAsync()
    {
        if (!ShowPartyFields) return;

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        var term = PartySearchText.Trim();
        if (term.Length < 1)
        {
            PartySuggestions.Clear();
            OnPropertyChanged(nameof(ShowPartySuggestions));
            return;
        }

        if (_selectedPartyName != null &&
            string.Equals(term, _selectedPartyName, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            await Task.Delay(250, token);
            if (token.IsCancellationRequested) return;

            var kind = SelectedKind.Kind == VoucherKind.Payment
                ? PartyLedgerKind.Supplier
                : PartyLedgerKind.Customer;

            var rows = await _accounting.ListPartyLedgersAsync(kind, term, _branchId, token);
            PartySuggestions.Clear();
            foreach (var row in rows.Take(12))
                PartySuggestions.Add(row);
            OnPropertyChanged(nameof(ShowPartySuggestions));
        }
        catch (OperationCanceledException) { }
    }

    private bool CanSave()
    {
        if (Amount <= 0 || SelectedCashAccount is null) return false;
        return SelectedKind.Kind switch
        {
            VoucherKind.Expense => SelectedExpenseAccount is not null,
            _ => _selectedPartyId.HasValue
        };
    }

    private async Task SaveAsync()
    {
        if (SelectedCashAccount is null) return;

        IsBusy = true;
        StatusMessage = "Saving voucher...";
        try
        {
            var result = SelectedKind.Kind switch
            {
                VoucherKind.Payment when _selectedPartyId is int supplierId =>
                    await _accounting.CreatePaymentAsync(new CreatePaymentRequest
                    {
                        SupplierId = supplierId,
                        Amount = Amount,
                        CashOrBankAccountId = SelectedCashAccount.Id,
                        EntryDate = EntryDate,
                        Narration = Narration
                    }, _branchId),

                VoucherKind.Receipt when _selectedPartyId is int customerId =>
                    await _accounting.CreateReceiptAsync(new CreateReceiptRequest
                    {
                        CustomerId = customerId,
                        Amount = Amount,
                        CashOrBankAccountId = SelectedCashAccount.Id,
                        EntryDate = EntryDate,
                        Narration = Narration
                    }, _branchId),

                VoucherKind.Expense when SelectedExpenseAccount is AccountLookupDto expense =>
                    await _accounting.CreateExpenseAsync(new CreateExpenseRequest
                    {
                        ExpenseAccountId = expense.Id,
                        CashOrBankAccountId = SelectedCashAccount.Id,
                        Amount = Amount,
                        EntryDate = EntryDate,
                        Narration = Narration
                    }, _branchId),

                _ => Result.Failure<VoucherReceiptDto>("Complete all required fields.")
            };

            if (result.IsFailure || result.Value is null)
            {
                _dialog.ShowError(result.Error ?? "Could not save voucher.");
                return;
            }

            _dialog.ShowInfo(
                $"{SelectedKind.Label} {result.Value.VoucherNumber} saved for {result.Value.Amount:N2}.");
            ResetForm();
            await PreviewVoucherAsync();
        }
        catch (Exception ex)
        {
            _dialog.ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ResetForm()
    {
        Amount = 0;
        Narration = null;
        ClearParty();
        EntryDate = DateTime.Today;
        StatusMessage = null;
    }
}
