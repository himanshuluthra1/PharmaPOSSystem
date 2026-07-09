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
    private bool _suppressSearch;
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
            OnPropertyChanged(nameof(ShowPatientSuggestions));
            ClearResults();
            if (!string.IsNullOrWhiteSpace(SearchText))
                _ = SearchAsync(SearchText);
        }
    }

    public BillSearchType SearchType => SelectedCriteria.Type;

    public ObservableCollection<string> PatientSuggestions { get; } = new();
    public ObservableCollection<BillSearchResultDto> Results { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value) && !_suppressSearch)
                _ = SearchAsync(value);
        }
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (SetProperty(ref _selectedIndex, value))
            {
                SelectedSuggestionIndex = -1;
                OnPropertyChanged(nameof(SelectedBill));
            }
        }
    }

    public int SelectedSuggestionIndex
    {
        get => _selectedSuggestionIndex;
        set
        {
            if (SetProperty(ref _selectedSuggestionIndex, value))
            {
                if (value >= 0)
                    SelectedIndex = -1;
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

    public bool ShowPatientSuggestions =>
        SearchType == BillSearchType.PatientName && PatientSuggestions.Count > 0;

    public bool ShowMatchedMedicine => SearchType == BillSearchType.MedicineName;

    public void SelectPatientSuggestion(string name)
    {
        _suppressSearch = true;
        SearchText = name;
        _suppressSearch = false;
        PatientSuggestions.Clear();
        OnPropertyChanged(nameof(ShowPatientSuggestions));
        _ = SearchBillsOnlyAsync(name);
    }

    public void MoveSelection(int delta)
    {
        if (ShowPatientSuggestions)
        {
            if (SelectedSuggestionIndex < 0 && delta > 0)
            {
                SelectedSuggestionIndex = 0;
                return;
            }

            if (SelectedSuggestionIndex >= 0)
            {
                var next = SelectedSuggestionIndex + delta;
                if (next >= 0 && next < PatientSuggestions.Count)
                {
                    SelectedSuggestionIndex = next;
                    return;
                }

                if (next >= PatientSuggestions.Count && Results.Count > 0)
                {
                    SelectedSuggestionIndex = -1;
                    SelectedIndex = 0;
                    return;
                }

                if (next < 0)
                {
                    SelectedSuggestionIndex = -1;
                    return;
                }
            }
        }

        if (Results.Count == 0)
        {
            SelectedIndex = -1;
            return;
        }

        if (SelectedIndex < 0)
            SelectedIndex = 0;
        else
            SelectedIndex = Math.Clamp(SelectedIndex + delta, 0, Results.Count - 1);
    }

    public bool TryConfirmSelection()
    {
        if (SelectedSuggestionIndex >= 0 && SelectedSuggestionIndex < PatientSuggestions.Count)
        {
            SelectPatientSuggestion(PatientSuggestions[SelectedSuggestionIndex]);
            return false;
        }

        return SelectedBill is not null;
    }

    private void ClearResults()
    {
        PatientSuggestions.Clear();
        Results.Clear();
        SelectedIndex = -1;
        SelectedSuggestionIndex = -1;
        Hint = null;
        OnPropertyChanged(nameof(ShowPatientSuggestions));
    }

    private async Task SearchAsync(string term)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        Results.Clear();
        SelectedIndex = -1;
        SelectedSuggestionIndex = -1;

        term = term.Trim();
        if (term.Length == 0)
        {
            PatientSuggestions.Clear();
            OnPropertyChanged(nameof(ShowPatientSuggestions));
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

                if (SearchType == BillSearchType.PatientName)
                {
                    var suggestions = await _salesService.SuggestPatientNamesAsync(term, _branchId, token);
                    if (token.IsCancellationRequested) return;

                    PatientSuggestions.Clear();
                    foreach (var name in suggestions)
                        PatientSuggestions.Add(name);
                    OnPropertyChanged(nameof(ShowPatientSuggestions));
                }
                else
                {
                    PatientSuggestions.Clear();
                    OnPropertyChanged(nameof(ShowPatientSuggestions));
                }

                await LoadBillsAsync(term, token);
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

    private async Task SearchBillsOnlyAsync(string term)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        Hint = "Searching...";
        try
        {
            await _searchGate.WaitAsync(token);
            try
            {
                if (token.IsCancellationRequested) return;
                await LoadBillsAsync(term.Trim(), token);
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

        SelectedIndex = rows.Count > 0 ? 0 : -1;
        Hint = rows.Count == 0 ? $"No bills found for \"{term}\"." : null;
    }
}
