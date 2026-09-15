using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Accounting;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;
using PharmaPOS.WPF.Views;

namespace PharmaPOS.WPF.ViewModels.Accounting;

/// <summary>Selectable open bill row for multi-bill settlement on Parties.</summary>
public sealed class PartyBillSettleLineViewModel : ObservableObject
{
    private bool _isSelected;
    private decimal _applyAmount;
    private bool _suppressCallback;

    public PartyBillSettleLineViewModel(PartyBillRowDto bill)
    {
        Bill = bill;
        TransactionId = bill.TransactionId;
        InvoiceNumber = bill.InvoiceNumber;
        SupplierBillNumber = bill.SupplierBillNumber;
        InvoiceDate = bill.InvoiceDate;
        InvoiceDateLabel = bill.InvoiceDateLabel;
        GrandTotal = bill.GrandTotal;
        BalanceDue = bill.BalanceDue;
        AdjustedAmount = bill.AdjustedAmount;
        IsPurchaseReturn = bill.IsPurchaseReturn;
        CanSelect = !bill.IsPurchaseReturn && bill.BalanceDue > 0.009m;
    }

    public PartyBillRowDto Bill { get; }
    public int TransactionId { get; }
    public string InvoiceNumber { get; }
    public string? SupplierBillNumber { get; }
    public DateTime InvoiceDate { get; }
    public string InvoiceDateLabel { get; }
    public decimal GrandTotal { get; }
    public decimal AdjustedAmount { get; }
    public decimal BalanceDue { get; }
    public bool IsPurchaseReturn { get; }
    public bool CanSelect { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!CanSelect || !SetProperty(ref _isSelected, value)) return;
            if (!value)
            {
                _suppressCallback = true;
                ApplyAmount = 0m;
                _suppressCallback = false;
            }
            Changed?.Invoke();
        }
    }

    public decimal ApplyAmount
    {
        get => _applyAmount;
        set
        {
            var clamped = Math.Clamp(Math.Round(value, 2), 0m, Math.Max(0m, BalanceDue));
            if (!SetProperty(ref _applyAmount, clamped)) return;
            if (clamped > 0 && !_isSelected && CanSelect)
                SetProperty(ref _isSelected, true, nameof(IsSelected));
            if (!_suppressCallback)
                Changed?.Invoke();
        }
    }

    public Action? Changed { get; set; }

    public void SetApplySilent(decimal amount)
    {
        _suppressCallback = true;
        var clamped = Math.Clamp(Math.Round(amount, 2), 0m, Math.Max(0m, BalanceDue));
        _applyAmount = clamped;
        OnPropertyChanged(nameof(ApplyAmount));
        if (clamped > 0 && !_isSelected && CanSelect)
        {
            _isSelected = true;
            OnPropertyChanged(nameof(IsSelected));
        }
        if (clamped <= 0 && _isSelected)
        {
            // keep selection; amount may be zero when payment pot is exhausted
        }
        _suppressCallback = false;
    }
}

public class PartyLedgerTabViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDialogService _dialog;
    private readonly int? _branchId;
    private readonly Action<PartyLedgerRowDto?> _onSelectionChanged;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private string _searchText = string.Empty;
    private PartyKindOption _selectedKind;
    private PartyLedgerRowDto? _selectedParty;
    private string? _statusMessage;
    private bool _isBusy;
    private bool _showUnpaidBills = true;
    private bool _showPaidBills = true;
    private PartyBillsSummaryDto _selectedPartySummary = PartyBillsSummaryDto.Empty;
    private decimal _totalReceivables;
    private decimal _totalPayables;
    private decimal _paymentAmount;
    private AccountLookupDto? _selectedAccount;
    private string? _settleNarration;
    private bool _reallocating;
    private CancellationTokenSource? _searchCts;
    private int _refreshVersion;

    public PartyLedgerTabViewModel(
        IServiceScopeFactory scopeFactory,
        ICurrentUserService currentUser,
        IDialogService dialog,
        bool canCreateVouchers,
        Action<PartyLedgerRowDto?> onSelectionChanged)
    {
        _scopeFactory = scopeFactory;
        _dialog = dialog;
        _branchId = currentUser.CurrentUser?.BranchId;
        _onSelectionChanged = onSelectionChanged;
        CanCreateVouchers = canCreateVouchers;

        KindOptions =
        [
            new(PartyLedgerKind.Supplier, "Suppliers (Payables)"),
            new(PartyLedgerKind.Customer, "Customers (Receivables)")
        ];
        _selectedKind = KindOptions[0];

        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync(), _ => !IsBusy);
        PayNowCommand = new AsyncRelayCommand(PayNowAsync, CanPayNow);
        SettleSelectedCommand = new AsyncRelayCommand(SettleSelectedAsync, () => CanSettle);
        ClearSelectionCommand = new RelayCommand(_ => ClearSettlementSelection(), _ => !IsBusy);
        FillPaymentFromDueCommand = new RelayCommand(_ => PaymentAmount = SelectedAppliedTotal > 0
            ? SelectedAppliedTotal
            : Bills.Where(b => b.CanSelect).Sum(b => b.BalanceDue), _ => !IsBusy && SelectedParty is not null);

        if (CanCreateVouchers)
            _ = LoadAccountsAsync();
    }

    public IReadOnlyList<PartyKindOption> KindOptions { get; }
    public bool CanCreateVouchers { get; }

    public ObservableCollection<PartyLedgerRowDto> Parties { get; } = new();
    public ObservableCollection<PartyBillSettleLineViewModel> Bills { get; } = new();
    public ObservableCollection<AccountLookupDto> CashBankAccounts { get; } = new();

    public event Func<Task>? BillPaid;

    public PartyKindOption SelectedKind
    {
        get => _selectedKind;
        set
        {
            if (value is null) return;
            if (!SetProperty(ref _selectedKind, value)) return;
            ClearSettlementFields();
            _ = RefreshAsync();
            OnPropertyChanged(nameof(PaymentAmountLabel));
            OnPropertyChanged(nameof(SettleButtonLabel));
            OnPropertyChanged(nameof(AccountLabel));
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                _ = DebouncedSearchAsync();
        }
    }

    public bool ShowUnpaidBills
    {
        get => _showUnpaidBills;
        set
        {
            if (!SetProperty(ref _showUnpaidBills, value)) return;
            _ = RefreshAsync();
        }
    }

    public bool ShowPaidBills
    {
        get => _showPaidBills;
        set
        {
            if (!SetProperty(ref _showPaidBills, value)) return;
            _ = RefreshAsync();
        }
    }

    public PartyBillsSummaryDto SelectedPartySummary
    {
        get => _selectedPartySummary;
        private set => SetProperty(ref _selectedPartySummary, value);
    }

    public decimal TotalReceivables
    {
        get => _totalReceivables;
        private set => SetProperty(ref _totalReceivables, value);
    }

    public decimal TotalPayables
    {
        get => _totalPayables;
        private set => SetProperty(ref _totalPayables, value);
    }

    public decimal PaymentAmount
    {
        get => _paymentAmount;
        set
        {
            var rounded = Math.Max(0m, Math.Round(value, 2));
            if (!SetProperty(ref _paymentAmount, rounded)) return;
            ReallocateSelectedBills();
            NotifySettlementProps();
        }
    }

    public AccountLookupDto? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (SetProperty(ref _selectedAccount, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public string? SettleNarration
    {
        get => _settleNarration;
        set => SetProperty(ref _settleNarration, value);
    }

    public decimal SelectedAppliedTotal => Bills.Where(b => b.IsSelected).Sum(b => b.ApplyAmount);
    public decimal RemainingPaymentAmount => Math.Round(PaymentAmount - SelectedAppliedTotal, 2);
    public int SelectedBillCount => Bills.Count(b => b.IsSelected && b.ApplyAmount > 0.009m);
    public bool HasPaymentPool => PaymentAmount > 0.009m;
    public bool IsSupplierMode => SelectedKind.Kind == PartyLedgerKind.Supplier;
    public string PaymentAmountLabel => IsSupplierMode ? "Payment amount" : "Receipt amount";
    public string SettleButtonLabel => IsSupplierMode ? "Pay selected" : "Collect selected";
    public string AccountLabel => IsSupplierMode ? "Pay from" : "Receive in";

    public bool CanSettle =>
        CanCreateVouchers
        && !IsBusy
        && SelectedParty is not null
        && SelectedAccount is not null
        && SelectedAppliedTotal > 0.009m
        && SelectedAppliedTotal <= PaymentAmount + 0.009m;

    public PartyLedgerRowDto? SelectedParty
    {
        get => _selectedParty;
        set
        {
            if (!SetProperty(ref _selectedParty, value)) return;
            _onSelectionChanged(value);
            ClearSettlementFields();
            _ = LoadBillsAsync();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public ICommand RefreshCommand { get; }
    public ICommand PayNowCommand { get; }
    public ICommand SettleSelectedCommand { get; }
    public ICommand ClearSelectionCommand { get; }
    public ICommand FillPaymentFromDueCommand { get; }

    public async Task RefreshAsync()
    {
        var version = Interlocked.Increment(ref _refreshVersion);
        await _refreshGate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (version != _refreshVersion) return;

            IsBusy = true;
            StatusMessage = null;

            var kind = SelectedKind.Kind;
            var term = SearchText;
            var keepId = _selectedParty?.PartyId;

            List<PartyLedgerRowDto> rows;
            AccountingSummaryDto acctSummary;
            using (var scope = _scopeFactory.CreateScope())
            {
                var accounting = scope.ServiceProvider.GetRequiredService<IAccountingService>();
                rows = await accounting.ListPartyLedgersAsync(kind, term, _branchId, owedOnly: !ShowPaidBills)
                    .ConfigureAwait(true);
                acctSummary = await accounting.GetSummaryAsync(_branchId).ConfigureAwait(true);
            }

            if (version != _refreshVersion) return;

            Parties.Clear();
            foreach (var row in rows)
                Parties.Add(row);

            TotalReceivables = acctSummary.TotalReceivables;
            TotalPayables = acctSummary.TotalPayables;

            var next = keepId is int id
                ? Parties.FirstOrDefault(p => p.PartyId == id) ?? Parties.FirstOrDefault(p => p.OutstandingBalance > 0) ?? Parties.FirstOrDefault()
                : Parties.FirstOrDefault(p => p.OutstandingBalance > 0) ?? Parties.FirstOrDefault();

            if (_selectedParty is not null)
            {
                _selectedParty = null;
                OnPropertyChanged(nameof(SelectedParty));
            }

            _selectedParty = next;
            OnPropertyChanged(nameof(SelectedParty));
            _onSelectionChanged(next);
            ClearSettlementFields();
            await LoadBillsAsync(kind, next).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load ledgers: {ex.Message}";
            SelectedPartySummary = PartyBillsSummaryDto.Empty;
            TotalReceivables = 0;
            TotalPayables = 0;
        }
        finally
        {
            IsBusy = false;
            _refreshGate.Release();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private bool CanPayNow(object? parameter)
        => CanCreateVouchers
           && !IsBusy
           && SelectedParty is not null
           && parameter is PartyBillSettleLineViewModel line
           && line.BalanceDue > 0.009m;

    private async Task PayNowAsync(object? parameter)
    {
        if (SelectedParty is not PartyLedgerRowDto party) return;
        if (parameter is not PartyBillSettleLineViewModel line) return;
        if (!CanCreateVouchers) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var accounting = scope.ServiceProvider.GetRequiredService<IAccountingService>();
            var vm = new PayBillViewModel(
                accounting,
                _dialog,
                _branchId,
                SelectedKind.Kind,
                party,
                line.Bill);

            var window = new PayBillWindow
            {
                DataContext = vm,
                Owner = System.Windows.Application.Current.MainWindow
            };
            if (window.ShowDialog() != true) return;

            StatusMessage = $"Saved payment for {line.InvoiceNumber}.";
            if (BillPaid is not null)
                await BillPaid.Invoke().ConfigureAwait(true);
            else
                await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Pay Now failed: {ex.Message}";
            _dialog.ShowError(ex.Message, "Pay Now");
        }
    }

    private async Task SettleSelectedAsync()
    {
        if (!CanSettle || SelectedParty is null || SelectedAccount is null) return;

        var applied = Bills.Where(b => b.IsSelected && b.ApplyAmount > 0.009m).ToList();
        if (applied.Count == 0) return;

        var settleAmount = Math.Round(applied.Sum(b => b.ApplyAmount), 2);
        if (settleAmount <= 0 || settleAmount > PaymentAmount + 0.009m)
        {
            _dialog.ShowError(
                $"Allocated ₹{settleAmount:N2} cannot exceed payment amount ₹{PaymentAmount:N2}.",
                SettleButtonLabel);
            return;
        }

        IsBusy = true;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var accounting = scope.ServiceProvider.GetRequiredService<IAccountingService>();
            var narration = string.IsNullOrWhiteSpace(SettleNarration)
                ? $"{(IsSupplierMode ? "Payment" : "Receipt")} — {applied.Count} bill(s)"
                : SettleNarration.Trim();

            if (IsSupplierMode)
            {
                var result = await accounting.CreatePaymentAsync(new CreatePaymentRequest
                {
                    SupplierId = SelectedParty.PartyId,
                    Amount = settleAmount,
                    CashOrBankAccountId = SelectedAccount.Id,
                    EntryDate = DateTime.Today,
                    Narration = narration,
                    AllocationMode = PaymentAllocationMode.BillWise,
                    BillAllocations = applied.Select(b => new BillPaymentAllocationDto
                    {
                        PurchaseId = b.TransactionId,
                        Amount = b.ApplyAmount
                    }).ToList()
                }, _branchId).ConfigureAwait(true);

                if (!result.IsSuccess)
                {
                    _dialog.ShowError(result.Error ?? "Could not save payment.", SettleButtonLabel);
                    return;
                }
            }
            else
            {
                var result = await accounting.CreateReceiptAsync(new CreateReceiptRequest
                {
                    CustomerId = SelectedParty.PartyId,
                    Amount = settleAmount,
                    CashOrBankAccountId = SelectedAccount.Id,
                    EntryDate = DateTime.Today,
                    Narration = narration,
                    AllocationMode = PaymentAllocationMode.BillWise,
                    BillAllocations = applied.Select(b => new BillReceiptAllocationDto
                    {
                        SaleId = b.TransactionId,
                        Amount = b.ApplyAmount
                    }).ToList()
                }, _branchId).ConfigureAwait(true);

                if (!result.IsSuccess)
                {
                    _dialog.ShowError(result.Error ?? "Could not save receipt.", SettleButtonLabel);
                    return;
                }
            }

            StatusMessage = $"{SettleButtonLabel}: ₹{settleAmount:N2} across {applied.Count} bill(s).";
            ClearSettlementFields();
            if (BillPaid is not null)
                await BillPaid.Invoke().ConfigureAwait(true);
            else
                await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = $"{SettleButtonLabel} failed: {ex.Message}";
            _dialog.ShowError(ex.Message, SettleButtonLabel);
        }
        finally
        {
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task LoadAccountsAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var accounting = scope.ServiceProvider.GetRequiredService<IAccountingService>();
            var accounts = await accounting.ListCashAndBankAccountsAsync().ConfigureAwait(true);
            CashBankAccounts.Clear();
            foreach (var a in accounts)
                CashBankAccounts.Add(a);
            SelectedAccount ??= CashBankAccounts.FirstOrDefault();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load cash/bank accounts: {ex.Message}";
        }
    }

    private async Task LoadBillsAsync()
        => await LoadBillsAsync(SelectedKind.Kind, SelectedParty).ConfigureAwait(true);

    private async Task LoadBillsAsync(PartyLedgerKind kind, PartyLedgerRowDto? party)
    {
        Bills.Clear();
        SelectedPartySummary = PartyBillsSummaryDto.Empty;
        NotifySettlementProps();
        if (party is null) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var accounting = scope.ServiceProvider.GetRequiredService<IAccountingService>();
            var rows = await accounting.ListPartyBillsAsync(
                    kind, party.PartyId, _branchId, openOnly: !ShowPaidBills)
                .ConfigureAwait(true);

            if (!ShowUnpaidBills)
                rows = rows.Where(b => b.IsPurchaseReturn || Math.Abs(b.BalanceDue) <= 0.009m).ToList();
            if (!ShowPaidBills)
                rows = rows.Where(b => b.IsPurchaseReturn || Math.Abs(b.BalanceDue) > 0.009m).ToList();
            if (!ShowUnpaidBills && !ShowPaidBills)
                rows = rows.Where(b => b.IsPurchaseReturn).ToList();

            foreach (var row in rows)
            {
                var line = new PartyBillSettleLineViewModel(row) { Changed = OnBillLineChanged };
                Bills.Add(line);
            }

            SelectedPartySummary = await accounting.GetPartyBillsSummaryAsync(
                    kind, party.PartyId, _branchId)
                .ConfigureAwait(true);
            SyncSelectedPartyOutstanding(SelectedPartySummary.PendingAmount);
            NotifySettlementProps();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load bills: {ex.Message}";
            SelectedPartySummary = PartyBillsSummaryDto.Empty;
        }
    }

    /// <summary>Keep left-list Outstanding in lockstep with sum of Due on the RHS grid.</summary>
    private void SyncSelectedPartyOutstanding(decimal outstanding)
    {
        if (SelectedParty is null) return;
        var rounded = Math.Round(outstanding, 2);
        if (Math.Abs(SelectedParty.OutstandingBalance - rounded) < 0.005m) return;

        var updated = SelectedParty with { OutstandingBalance = rounded };
        var idx = -1;
        for (var i = 0; i < Parties.Count; i++)
        {
            if (Parties[i].PartyId != updated.PartyId) continue;
            idx = i;
            break;
        }

        if (idx >= 0)
            Parties[idx] = updated;

        _selectedParty = updated;
        OnPropertyChanged(nameof(SelectedParty));
    }

    private void OnBillLineChanged()
    {
        if (_reallocating) return;
        _reallocating = true;
        try
        {
            // Newly checked bills with no apply yet take from the remaining pool.
            foreach (var line in Bills.Where(b => b.IsSelected && b.CanSelect && b.ApplyAmount <= 0.009m))
            {
                var usedByOthers = Bills.Where(b => !ReferenceEquals(b, line)).Sum(b => b.ApplyAmount);
                var rem = Math.Round(PaymentAmount - usedByOthers, 2);
                line.SetApplySilent(Math.Min(line.BalanceDue, Math.Max(0m, rem)));
            }

            CapAppliesToPaymentPool();
        }
        finally
        {
            _reallocating = false;
        }
        NotifySettlementProps();
    }

    /// <summary>
    /// Distributes <see cref="PaymentAmount"/> across checked bills in list order,
    /// capping each at its balance due. Used when the payment amount changes.
    /// </summary>
    private void ReallocateSelectedBills()
    {
        if (_reallocating) return;
        _reallocating = true;
        try
        {
            var remaining = PaymentAmount;
            foreach (var line in Bills)
            {
                if (line.IsPurchaseReturn || !line.IsSelected || !line.CanSelect)
                {
                    if (!line.IsPurchaseReturn && !line.IsSelected)
                        line.SetApplySilent(0m);
                    continue;
                }

                var apply = Math.Min(line.BalanceDue, remaining);
                line.SetApplySilent(apply);
                remaining = Math.Round(remaining - apply, 2);
            }
        }
        finally
        {
            _reallocating = false;
        }
    }

    private void CapAppliesToPaymentPool()
    {
        var remaining = PaymentAmount;
        foreach (var line in Bills)
        {
            if (line.IsPurchaseReturn || !line.IsSelected || !line.CanSelect)
            {
                if (!line.IsPurchaseReturn && !line.IsSelected)
                    line.SetApplySilent(0m);
                continue;
            }

            if (line.ApplyAmount > remaining)
                line.SetApplySilent(remaining);
            remaining = Math.Round(remaining - line.ApplyAmount, 2);
        }
    }

    private void ClearSettlementSelection()
    {
        _reallocating = true;
        try
        {
            foreach (var line in Bills)
            {
                if (!line.IsSelected && line.ApplyAmount == 0) continue;
                line.IsSelected = false;
            }
        }
        finally
        {
            _reallocating = false;
        }
        NotifySettlementProps();
    }

    private void ClearSettlementFields()
    {
        _paymentAmount = 0m;
        _settleNarration = null;
        OnPropertyChanged(nameof(PaymentAmount));
        OnPropertyChanged(nameof(SettleNarration));
        ClearSettlementSelection();
    }

    private void NotifySettlementProps()
    {
        OnPropertyChanged(nameof(SelectedAppliedTotal));
        OnPropertyChanged(nameof(RemainingPaymentAmount));
        OnPropertyChanged(nameof(SelectedBillCount));
        OnPropertyChanged(nameof(HasPaymentPool));
        OnPropertyChanged(nameof(CanSettle));
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task DebouncedSearchAsync()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        try
        {
            await Task.Delay(300, token).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
    }
}
