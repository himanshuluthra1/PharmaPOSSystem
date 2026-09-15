using System.Collections.ObjectModel;
using PharmaPOS.Application.Features.Sales;
using PharmaPOS.WPF.Mvvm;

namespace PharmaPOS.WPF.ViewModels.Sales;

public sealed class BillSearchCriteriaOption(BillSearchType type, string label)
{
    public BillSearchType Type { get; } = type;
    public string Label { get; } = label;
}

/// <summary>View model for the fast-billing bill search popup.</summary>
public class BillSearchViewModel : ObservableObject
{
    private readonly ISalesService _salesService;
    private readonly int? _branchId;

    private BillSearchCriteriaOption _selectedCriteria;
    private string _searchText = string.Empty;
    private int _selectedIndex = -1;
    private int _selectedSuggestionIndex = -1;
    private string? _hint;
    private bool _suppressSuggestionReload;
    private CancellationTokenSource? _searchCts;
    private readonly SemaphoreSlim _searchGate = new(1, 1);

    public BillSearchViewModel(ISalesService salesService, int? branchId)
    {
        _salesService = salesService;
        _branchId = branchId;

        CriteriaOptions =
        [
            new(BillSearchType.PatientName, "Patient Name"),
            new(BillSearchType.MobileNumber, "Mobile Number"),
            new(BillSearchType.MedicineName, "Medicine Name")
        ];
        _selectedCriteria = CriteriaOptions[0];
        Hint = SearchHint;
    }

    public IReadOnlyList<BillSearchCriteriaOption> CriteriaOptions { get; }

    public BillSearchCriteriaOption SelectedCriteria
    {
        get => _selectedCriteria;
        set
        {
            if (!SetProperty(ref _selectedCriteria, value)) return;
            OnPropertyChanged(nameof(SearchType));
            OnPropertyChanged(nameof(SearchHint));
            OnPropertyChanged(nameof(SuggestionsHeader));
            OnPropertyChanged(nameof(ShowSuggestions));
            ClearResults();
            if (!string.IsNullOrWhiteSpace(SearchText))
                _ = SearchAsync(SearchText);
        }
    }

    public BillSearchType SearchType => SelectedCriteria.Type;

    public ObservableCollection<BillSearchSuggestionDto> Suggestions { get; } = new();
    public ObservableCollection<BillSearchResultDto> Results { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                _ = SearchAsync(value);
        }
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (SetProperty(ref _selectedIndex, value))
                OnPropertyChanged(nameof(SelectedBill));
        }
    }

    public int SelectedSuggestionIndex
    {
        get => _selectedSuggestionIndex;
        set
        {
            if (!SetProperty(ref _selectedSuggestionIndex, value)) return;
            if (_suppressSuggestionReload) return;
            if (value >= 0 && value < Suggestions.Count)
            {
                SelectedIndex = -1;
                _ = LoadBillsForValueAsync(Suggestions[value].Value);
            }
        }
    }

    public BillSearchResultDto? SelectedBill =>
        SelectedIndex >= 0 && SelectedIndex < Results.Count ? Results[SelectedIndex] : null;

    public string? Hint
    {
        get => _hint;
        private set => SetProperty(ref _hint, value);
    }

    public string SearchHint => SearchType switch
    {
        BillSearchType.PatientName => "Type patient name — matching names are suggested as you type",
        BillSearchType.MobileNumber => "Type mobile number (minimum 3 digits)",
        BillSearchType.MedicineName => "Type medicine name (minimum 2 characters)",
        _ => string.Empty
    };

    public string SuggestionsHeader => SearchType switch
    {
        BillSearchType.PatientName => "Patient suggestions",
        BillSearchType.MobileNumber => "Mobile suggestions",
        BillSearchType.MedicineName => "Medicine suggestions",
        _ => "Suggestions"
    };

    public bool ShowSuggestions => Suggestions.Count > 0;

    /// <summary>Highlight a suggestion and load its matching bills (keeps suggestion list).</summary>
    public void FocusSuggestion(BillSearchSuggestionDto suggestion)
    {
        var index = Suggestions.IndexOf(suggestion);
        if (index < 0) return;
        SelectedSuggestionIndex = index;
    }

    public void MoveSelection(int delta)
    {
        // Prefer navigating suggestions until user moves into the bills list.
        if (ShowSuggestions && SelectedIndex < 0)
        {
            if (SelectedSuggestionIndex < 0)
            {
                if (delta > 0 && Suggestions.Count > 0)
                    SelectedSuggestionIndex = 0;
                return;
            }

            var next = SelectedSuggestionIndex + delta;
            if (next >= 0 && next < Suggestions.Count)
            {
                SelectedSuggestionIndex = next;
                return;
            }

            if (next >= Suggestions.Count && Results.Count > 0)
            {
                SelectedIndex = 0;
                return;
            }

            return;
        }

        if (Results.Count == 0)
        {
            SelectedIndex = -1;
            return;
        }

        if (SelectedIndex < 0)
        {
            SelectedIndex = delta > 0 ? 0 : Results.Count - 1;
            return;
        }

        var nextBill = SelectedIndex + delta;
        if (nextBill < 0)
        {
            SelectedIndex = -1;
            if (ShowSuggestions && SelectedSuggestionIndex < 0)
                SelectedSuggestionIndex = 0;
            return;
        }

        SelectedIndex = Math.Clamp(nextBill, 0, Results.Count - 1);
    }

    /// <summary>True when a matching bill is selected and should be opened.</summary>
    public bool TryConfirmBillSelection() => SelectedBill is not null;

    private void ClearResults()
    {
        Suggestions.Clear();
        Results.Clear();
        SelectedIndex = -1;
        _suppressSuggestionReload = true;
        SelectedSuggestionIndex = -1;
        _suppressSuggestionReload = false;
        Hint = null;
        OnPropertyChanged(nameof(ShowSuggestions));
    }

    private async Task SearchAsync(string term)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        Results.Clear();
        SelectedIndex = -1;
        _suppressSuggestionReload = true;
        SelectedSuggestionIndex = -1;
        _suppressSuggestionReload = false;

        term = term.Trim();
        if (term.Length == 0)
        {
            Suggestions.Clear();
            OnPropertyChanged(nameof(ShowSuggestions));
            Hint = SearchHint;
            return;
        }

        Hint = "Searching...";
        try
        {
            await Task.Delay(250, token);
            if (token.IsCancellationRequested) return;

            await _searchGate.WaitAsync(token);
            try
            {
                if (token.IsCancellationRequested) return;

                var suggestions = await _salesService.SuggestBillSearchAsync(SearchType, term, _branchId, token);
                if (token.IsCancellationRequested) return;

                Suggestions.Clear();
                foreach (var row in suggestions)
                    Suggestions.Add(row);
                OnPropertyChanged(nameof(ShowSuggestions));

                if (Suggestions.Count > 0)
                {
                    _suppressSuggestionReload = true;
                    SelectedSuggestionIndex = 0;
                    _suppressSuggestionReload = false;
                    await LoadBillsAsync(Suggestions[0].Value, token);
                }
                else
                {
                    await LoadBillsAsync(term, token);
                }
            }
            finally
            {
                _searchGate.Release();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Hint = $"Search failed: {ex.Message}";
        }
    }

    private async Task LoadBillsForValueAsync(string value)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        Hint = "Loading bills...";
        try
        {
            await _searchGate.WaitAsync(token);
            try
            {
                if (token.IsCancellationRequested) return;
                await LoadBillsAsync(value.Trim(), token);
            }
            finally
            {
                _searchGate.Release();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Hint = $"Search failed: {ex.Message}";
        }
    }

    private async Task LoadBillsAsync(string term, CancellationToken token)
    {
        if (SearchType == BillSearchType.PatientName && term.Length < 1 ||
            SearchType == BillSearchType.MobileNumber && term.Length < 3 ||
            SearchType == BillSearchType.MedicineName && term.Length < 2)
        {
            Results.Clear();
            SelectedIndex = -1;
            Hint = SearchHint;
            return;
        }

        var rows = await _salesService.SearchBillsAsync(SearchType, term, _branchId, token);
        if (token.IsCancellationRequested) return;

        Results.Clear();
        foreach (var row in rows)
            Results.Add(row);

        SelectedIndex = -1;
        Hint = rows.Count == 0
            ? $"No bills found for \"{term}\"."
            : $"{rows.Count} bill(s) — ↑↓ suggestions / bills  •  Enter or double-click opens invoice";
    }
}
