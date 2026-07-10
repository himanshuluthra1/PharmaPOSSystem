using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Accounting;
using PharmaPOS.WPF.Mvvm;

namespace PharmaPOS.WPF.ViewModels.Accounting;

public class PartyLedgerTabViewModel : ObservableObject
{
    private readonly IAccountingService _accounting;
    private readonly int? _branchId;
    private readonly Action<PartyLedgerRowDto?> _onSelectionChanged;

    private string _searchText = string.Empty;
    private PartyKindOption _selectedKind;
    private PartyLedgerRowDto? _selectedParty;
    private string? _statusMessage;
    private CancellationTokenSource? _searchCts;

    public PartyLedgerTabViewModel(
        IAccountingService accounting,
        ICurrentUserService currentUser,
        Action<PartyLedgerRowDto?> onSelectionChanged)
    {
        _accounting = accounting;
        _branchId = currentUser.CurrentUser?.BranchId;
        _onSelectionChanged = onSelectionChanged;

        KindOptions =
        [
            new(PartyLedgerKind.Supplier, "Suppliers (Payables)"),
            new(PartyLedgerKind.Customer, "Customers (Receivables)")
        ];
        _selectedKind = KindOptions[0];

        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
    }

    public IReadOnlyList<PartyKindOption> KindOptions { get; }

    public ObservableCollection<PartyLedgerRowDto> Parties { get; } = new();
    public ObservableCollection<PartyBillRowDto> Bills { get; } = new();

    public PartyKindOption SelectedKind
    {
        get => _selectedKind;
        set
        {
            if (SetProperty(ref _selectedKind, value))
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
            if (SetProperty(ref _selectedParty, value))
            {
                _onSelectionChanged(value);
                _ = LoadBillsAsync();
            }
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public ICommand RefreshCommand { get; }

    public async Task RefreshAsync()
    {
        try
        {
            var rows = await _accounting.ListPartyLedgersAsync(
                SelectedKind.Kind, SearchText, _branchId);

            Parties.Clear();
            foreach (var row in rows)
                Parties.Add(row);

            Bills.Clear();
            SelectedParty = Parties.FirstOrDefault(p => p.OutstandingBalance > 0) ?? Parties.FirstOrDefault();
            StatusMessage = rows.Count == 0
                ? "No parties found."
                : $"{rows.Count} party ledger(s) loaded.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load ledgers: {ex.Message}";
        }
    }

    private async Task LoadBillsAsync()
    {
        Bills.Clear();
        if (SelectedParty is not PartyLedgerRowDto party) return;

        try
        {
            var rows = await _accounting.ListPartyBillsAsync(
                SelectedKind.Kind, party.PartyId, _branchId);

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
            await Task.Delay(300, token);
            await RefreshAsync();
        }
        catch (OperationCanceledException) { }
    }
}
