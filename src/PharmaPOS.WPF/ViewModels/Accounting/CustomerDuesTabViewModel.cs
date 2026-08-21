using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Accounting;
using PharmaPOS.Application.Features.Settings;
using PharmaPOS.Shared.Constants;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;
using PharmaPOS.WPF.Views;

namespace PharmaPOS.WPF.ViewModels.Accounting;

public sealed class CustomerDuesTabViewModel : ObservableObject
{
    private readonly IAccountingService _accounting;
    private readonly ISettingsService _settings;
    private readonly IBillShareService _billShare;
    private readonly IInvoicePrintService _print;
    private readonly IDialogService _dialog;
    private readonly int? _branchId;

    private string _searchText = string.Empty;
    private PartyLedgerRowDto? _selected;
    private string? _statusMessage;
    private bool _isBusy;
    private CancellationTokenSource? _searchCts;

    public CustomerDuesTabViewModel(
        IAccountingService accounting,
        ISettingsService settings,
        IBillShareService billShare,
        IInvoicePrintService print,
        ICurrentUserService currentUser,
        IDialogService dialog)
    {
        _accounting = accounting;
        _settings = settings;
        _billShare = billShare;
        _print = print;
        _dialog = dialog;
        _branchId = currentUser.CurrentUser?.BranchId;

        CanCollect = currentUser.HasAnyPermission(
            AppConstants.Permissions.AccountingVouchers, AppConstants.Permissions.AccountingManage);

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        RemindCommand = new AsyncRelayCommand(RemindAsync, () => !IsBusy && SelectedDue is not null);
        CollectCommand = new AsyncRelayCommand(CollectAsync, () => !IsBusy && CanCollect && SelectedDue is not null);
    }

    public ObservableCollection<PartyLedgerRowDto> Dues { get; } = new();
    public ObservableCollection<PartyBillRowDto> OpenBills { get; } = new();

    public bool CanCollect { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                _ = DebouncedSearchAsync();
        }
    }

    public PartyLedgerRowDto? SelectedDue
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            CommandManager.InvalidateRequerySuggested();
            _ = LoadBillsAsync();
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

    public decimal TotalOutstanding => Dues.Sum(d => d.OutstandingBalance);

    public ICommand RefreshCommand { get; }
    public ICommand RemindCommand { get; }
    public ICommand CollectCommand { get; }

    public event Func<Task>? DuesChanged;

    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var rows = await _accounting.ListPartyLedgersAsync(
                PartyLedgerKind.Customer, SearchText, _branchId);

            var owed = rows.Where(r => r.OutstandingBalance > 0.009m).ToList();
            Dues.Clear();
            foreach (var row in owed)
                Dues.Add(row);

            OnPropertyChanged(nameof(TotalOutstanding));

            var keepId = SelectedDue?.PartyId;
            SelectedDue = keepId is int id
                ? Dues.FirstOrDefault(d => d.PartyId == id) ?? Dues.FirstOrDefault()
                : Dues.FirstOrDefault();

            StatusMessage = owed.Count == 0
                ? "No customer dues right now."
                : $"{owed.Count} customer(s) · ₹{TotalOutstanding:N2} outstanding";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load dues: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadBillsAsync()
    {
        OpenBills.Clear();
        if (SelectedDue is null) return;
        try
        {
            var bills = await _accounting.ListPartyBillsAsync(
                PartyLedgerKind.Customer, SelectedDue.PartyId, _branchId);
            foreach (var bill in bills)
                OpenBills.Add(bill);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load bills: {ex.Message}";
        }
    }

    private async Task RemindAsync()
    {
        if (SelectedDue is null) return;
        string? companyName = null;
        try
        {
            var company = await _settings.GetCompanyProfileAsync();
            companyName = company?.CompanyName;
        }
        catch { /* optional */ }

        var bills = OpenBills
            .Select(b => (b.InvoiceNumber, b.BalanceDue))
            .ToList();

        _billShare.OfferDuesReminder(
            SelectedDue.Name,
            SelectedDue.Phone,
            SelectedDue.OutstandingBalance,
            companyName,
            bills);
    }

    private async Task CollectAsync()
    {
        if (SelectedDue is null || !CanCollect) return;

        var vm = new CollectDueViewModel(_accounting, _dialog, _branchId, SelectedDue);
        var window = new CollectDueWindow { DataContext = vm };
        var owner = System.Windows.Application.Current?.MainWindow;
        if (owner is not null && owner.IsLoaded && owner.IsVisible)
        {
            try { window.Owner = owner; } catch { /* ignore */ }
        }

        if (window.ShowDialog() != true || window.ResultReceipt is null)
            return;

        var receipt = window.ResultReceipt;
        _print.ShowCollectionPreview(receipt);

        if (_dialog.Confirm("Send this receipt to the customer on WhatsApp / SMS?", "Collect now"))
            _billShare.OfferCollectionShare(receipt);

        await RefreshAsync();
        if (DuesChanged is not null)
            await DuesChanged.Invoke();
    }

    private async Task DebouncedSearchAsync()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        try
        {
            await Task.Delay(300, token);
            await RefreshAsync();
        }
        catch (OperationCanceledException) { }
    }
}
