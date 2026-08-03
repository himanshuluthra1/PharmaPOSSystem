using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Accounting;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Shared.Results;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Accounting;

public sealed class BillAllocationLineViewModel : ObservableObject
{
    private bool _isSelected;
    private decimal _applyAmount;

    public BillAllocationLineViewModel(PartyBillRowDto bill)
    {
        PurchaseId = bill.TransactionId;
        InvoiceNumber = bill.InvoiceNumber;
        InvoiceDateLabel = bill.InvoiceDateLabel;
        BalanceDue = bill.BalanceDue;
    }

    public int PurchaseId { get; }
    public string InvoiceNumber { get; }
    public string InvoiceDateLabel { get; }
    public decimal BalanceDue { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!SetProperty(ref _isSelected, value)) return;
            if (!value) ApplyAmount = 0m;
            SelectionChanged?.Invoke();
        }
    }

    public decimal ApplyAmount
    {
        get => _applyAmount;
        set
        {
            var clamped = Math.Clamp(value, 0m, BalanceDue);
            if (SetProperty(ref _applyAmount, clamped))
            {
                if (clamped > 0 && !_isSelected)
                    SetProperty(ref _isSelected, true, nameof(IsSelected));
                AmountChanged?.Invoke();
            }
        }
    }

    public Action? SelectionChanged { get; set; }
    public Action? AmountChanged { get; set; }
}

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
    private PaymentAllocationMode _allocationMode = PaymentAllocationMode.Fifo;

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
        FillFifoCheckedCommand = new RelayCommand(_ => FillFifoAmongChecked(), _ => ShowBillWiseAllocation);
        ApplyFullDueCheckedCommand = new RelayCommand(_ => ApplyFullDueOnChecked(), _ => ShowBillWiseAllocation);
        _ = InitializeAsync();
    }

    public IReadOnlyList<VoucherKindOption> KindOptions { get; }

    public ObservableCollection<PartyLedgerRowDto> PartySuggestions { get; } = new();
    public ObservableCollection<AccountLookupDto> CashAccounts { get; } = new();
    public ObservableCollection<AccountLookupDto> ExpenseAccounts { get; } = new();
    public ObservableCollection<BillAllocationLineViewModel> BillAllocations { get; } = new();

    public VoucherKindOption SelectedKind
    {
        get => _selectedKind;
        set
        {
            if (!SetProperty(ref _selectedKind, value)) return;
            OnPropertyChanged(nameof(ShowPartyFields));
            OnPropertyChanged(nameof(ShowExpenseFields));
            OnPropertyChanged(nameof(ShowPaymentAllocation));
            OnPropertyChanged(nameof(ShowBillWiseAllocation));
            OnPropertyChanged(nameof(PartyHint));
            ClearParty();
            _ = PreviewVoucherAsync();
        }
    }

    public bool ShowPartyFields => SelectedKind.Kind is VoucherKind.Payment or VoucherKind.Receipt;
    public bool ShowExpenseFields => SelectedKind.Kind == VoucherKind.Expense;
    public bool ShowPaymentAllocation => SelectedKind.Kind == VoucherKind.Payment && _selectedPartyId.HasValue;
    public bool ShowBillWiseAllocation => ShowPaymentAllocation && AllocationMode == PaymentAllocationMode.BillWise;

    public string PartyHint => SelectedKind.Kind == VoucherKind.Payment
        ? "Search supplier"
        : "Search customer";

    public PaymentAllocationMode AllocationMode
    {
        get => _allocationMode;
        set
        {
            if (!SetProperty(ref _allocationMode, value)) return;
            OnPropertyChanged(nameof(IsFifoAllocation));
            OnPropertyChanged(nameof(IsBillWiseAllocation));
            OnPropertyChanged(nameof(ShowBillWiseAllocation));
            if (value == PaymentAllocationMode.BillWise)
                _ = LoadOpenBillsAsync();
        }
    }

    public bool IsFifoAllocation
    {
        get => AllocationMode == PaymentAllocationMode.Fifo;
        set { if (value) AllocationMode = PaymentAllocationMode.Fifo; }
    }

    public bool IsBillWiseAllocation
    {
        get => AllocationMode == PaymentAllocationMode.BillWise;
        set { if (value) AllocationMode = PaymentAllocationMode.BillWise; }
    }

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
        set
        {
            if (SetProperty(ref _amount, value))
                CommandManager.InvalidateRequerySuggested();
        }
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
    public ICommand FillFifoCheckedCommand { get; }
    public ICommand ApplyFullDueCheckedCommand { get; }

    /// <summary>Raised after a payment, receipt, or expense voucher is saved.</summary>
    public event Func<Task>? VoucherSaved;

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
        OnPropertyChanged(nameof(ShowPaymentAllocation));
        OnPropertyChanged(nameof(ShowBillWiseAllocation));
        if (SelectedKind.Kind == VoucherKind.Payment)
            _ = LoadOpenBillsAsync();
        else
            BillAllocations.Clear();
    }

    private void ClearParty()
    {
        _selectedPartyId = null;
        _selectedPartyName = null;
        _partyOutstanding = 0;
        PartySearchText = string.Empty;
        PartySuggestions.Clear();
        BillAllocations.Clear();
        OnPropertyChanged(nameof(PartyOutstandingLabel));
        OnPropertyChanged(nameof(ShowPartySuggestions));
        OnPropertyChanged(nameof(ShowPaymentAllocation));
        OnPropertyChanged(nameof(ShowBillWiseAllocation));
    }

    private async Task LoadOpenBillsAsync()
    {
        BillAllocations.Clear();
        if (_selectedPartyId is not int supplierId || SelectedKind.Kind != VoucherKind.Payment)
            return;

        try
        {
            var bills = await _accounting.ListPartyBillsAsync(
                PartyLedgerKind.Supplier, supplierId, _branchId);
            foreach (var bill in bills.OrderBy(b => b.InvoiceDate).ThenBy(b => b.TransactionId))
            {
                var line = new BillAllocationLineViewModel(bill);
                line.SelectionChanged = () => CommandManager.InvalidateRequerySuggested();
                line.AmountChanged = () => CommandManager.InvalidateRequerySuggested();
                BillAllocations.Add(line);
            }
            OnPropertyChanged(nameof(ShowBillWiseAllocation));
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private void FillFifoAmongChecked()
    {
        var remaining = Amount;
        foreach (var line in BillAllocations)
        {
            if (!line.IsSelected || remaining <= 0)
            {
                if (line.IsSelected && remaining <= 0) line.ApplyAmount = 0;
                continue;
            }

            var apply = Math.Min(remaining, line.BalanceDue);
            line.ApplyAmount = apply;
            remaining -= apply;
        }
    }

    private void ApplyFullDueOnChecked()
    {
        foreach (var line in BillAllocations.Where(l => l.IsSelected))
            line.ApplyAmount = line.BalanceDue;
        Amount = BillAllocations.Where(l => l.IsSelected).Sum(l => l.ApplyAmount);
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
            VoucherKind.Payment => _selectedPartyId.HasValue && CanSavePaymentAllocation(),
            _ => _selectedPartyId.HasValue
        };
    }

    private bool CanSavePaymentAllocation()
    {
        if (AllocationMode != PaymentAllocationMode.BillWise) return true;
        var applied = BillAllocations.Where(b => b.ApplyAmount > 0).Sum(b => b.ApplyAmount);
        return Math.Abs(applied - Amount) <= 0.01m;
    }

    private async Task SaveAsync()
    {
        if (SelectedCashAccount is null) return;

        if (SelectedKind.Kind == VoucherKind.Payment
            && AllocationMode == PaymentAllocationMode.BillWise
            && !CanSavePaymentAllocation())
        {
            _dialog.ShowError("Bill-wise apply amounts must equal the payment amount.");
            return;
        }

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
                        Narration = Narration,
                        AllocationMode = AllocationMode,
                        BillAllocations = BillAllocations
                            .Where(b => b.ApplyAmount > 0)
                            .Select(b => new BillPaymentAllocationDto
                            {
                                PurchaseId = b.PurchaseId,
                                Amount = b.ApplyAmount
                            })
                            .ToList()
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
            if (VoucherSaved is not null)
                await VoucherSaved.Invoke();
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
        AllocationMode = PaymentAllocationMode.Fifo;
        ClearParty();
        EntryDate = DateTime.Today;
        StatusMessage = null;
    }
}
