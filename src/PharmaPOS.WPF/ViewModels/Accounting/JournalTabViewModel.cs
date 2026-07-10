using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Accounting;
using PharmaPOS.WPF.Mvvm;

namespace PharmaPOS.WPF.ViewModels.Accounting;

public class JournalTabViewModel : ObservableObject
{
    private readonly IAccountingService _accounting;
    private readonly int? _branchId;

    private string _searchText = string.Empty;
    private JournalEntryListDto? _selectedEntry;
    private string? _statusMessage;
    private decimal _totalDebit;
    private decimal _totalCredit;
    private CancellationTokenSource? _searchCts;

    public JournalTabViewModel(IAccountingService accounting, ICurrentUserService currentUser)
    {
        _accounting = accounting;
        _branchId = currentUser.CurrentUser?.BranchId;
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
    }

    public ObservableCollection<JournalEntryListDto> Entries { get; } = new();
    public ObservableCollection<JournalLineDto> Lines { get; } = new();
    public ObservableCollection<TrialBalanceRowDto> TrialBalance { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                _ = DebouncedSearchAsync();
        }
    }

    public JournalEntryListDto? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetProperty(ref _selectedEntry, value))
                _ = LoadLinesAsync();
        }
    }

    public decimal TotalDebit
    {
        get => _totalDebit;
        set
        {
            if (SetProperty(ref _totalDebit, value))
                OnPropertyChanged(nameof(TrialTotalsText));
        }
    }

    public decimal TotalCredit
    {
        get => _totalCredit;
        set
        {
            if (SetProperty(ref _totalCredit, value))
                OnPropertyChanged(nameof(TrialTotalsText));
        }
    }

    public string TrialTotalsText =>
        $"Trial total Dr {_totalDebit:N2}   Cr {_totalCredit:N2}";

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
            var entries = await _accounting.ListJournalEntriesAsync(SearchText, _branchId);
            Entries.Clear();
            foreach (var e in entries)
                Entries.Add(e);

            var trial = await _accounting.GetTrialBalanceAsync();
            TrialBalance.Clear();
            foreach (var row in trial)
                TrialBalance.Add(row);

            TotalDebit = trial.Sum(t => t.Debit);
            TotalCredit = trial.Sum(t => t.Credit);

            SelectedEntry = Entries.FirstOrDefault();
            StatusMessage = $"{entries.Count} journal voucher(s) loaded.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load journal: {ex.Message}";
        }
    }

    private async Task LoadLinesAsync()
    {
        Lines.Clear();
        if (SelectedEntry is not JournalEntryListDto entry) return;

        try
        {
            var lines = await _accounting.GetJournalLinesAsync(entry.Id);
            foreach (var line in lines)
                Lines.Add(line);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load voucher lines: {ex.Message}";
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
