using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Accounting;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;
using PharmaPOS.WPF.Views;

namespace PharmaPOS.WPF.ViewModels.Accounting;

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
    }

    public IReadOnlyList<PartyKindOption> KindOptions { get; }
    public bool CanCreateVouchers { get; }

    public ObservableCollection<PartyLedgerRowDto> Parties { get; } = new();
    public ObservableCollection<PartyBillRowDto> Bills { get; } = new();

    public event Func<Task>? BillPaid;

    public PartyKindOption SelectedKind
    {
        get => _selectedKind;
        set
        {
            if (value is null) return;
            if (!SetProperty(ref _selectedKind, value)) return;
            _ = RefreshAsync();
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

    public PartyLedgerRowDto? SelectedParty
    {
        get => _selectedParty;
        set
        {
            if (!SetProperty(ref _selectedParty, value)) return;
            _onSelectionChanged(value);
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

    public async Task RefreshAsync()
    {
        var version = Interlocked.Increment(ref _refreshVersion);
        await _refreshGate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (version != _refreshVersion) return;

            IsBusy = true;
            StatusMessage = "Loading parties…";

            var kind = SelectedKind.Kind;
            var term = SearchText;
            var keepId = _selectedParty?.PartyId;

            List<PartyLedgerRowDto> rows;
            using (var scope = _scopeFactory.CreateScope())
            {
                var accounting = scope.ServiceProvider.GetRequiredService<IAccountingService>();
                rows = await accounting.ListPartyLedgersAsync(kind, term, _branchId, owedOnly: true)
                    .ConfigureAwait(true);
            }

            if (version != _refreshVersion) return;

            Parties.Clear();
            foreach (var row in rows)
                Parties.Add(row);

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
            await LoadBillsAsync(kind, next).ConfigureAwait(true);

            StatusMessage = rows.Count == 0
                ? "No parties with open dues."
                : $"{rows.Count} party ledger(s) with open dues.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load ledgers: {ex.Message}";
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
           && parameter is PartyBillRowDto bill
           && bill.BalanceDue > 0.009m;

    private async Task PayNowAsync(object? parameter)
    {
        if (SelectedParty is not PartyLedgerRowDto party) return;
        if (parameter is not PartyBillRowDto bill) return;
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
                bill);

            var window = new PayBillWindow
            {
                DataContext = vm,
                Owner = System.Windows.Application.Current.MainWindow
            };
            if (window.ShowDialog() != true) return;

            StatusMessage = $"Saved payment for {bill.InvoiceNumber}.";
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

    private async Task LoadBillsAsync()
        => await LoadBillsAsync(SelectedKind.Kind, SelectedParty).ConfigureAwait(true);

    private async Task LoadBillsAsync(PartyLedgerKind kind, PartyLedgerRowDto? party)
    {
        Bills.Clear();
        if (party is null) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var accounting = scope.ServiceProvider.GetRequiredService<IAccountingService>();
            var rows = await accounting.ListPartyBillsAsync(kind, party.PartyId, _branchId)
                .ConfigureAwait(true);

            foreach (var row in rows)
                Bills.Add(row);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load bills: {ex.Message}";
        }
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
