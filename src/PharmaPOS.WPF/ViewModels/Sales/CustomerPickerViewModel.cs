using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Features.Sales;
using PharmaPOS.WPF.Mvvm;

namespace PharmaPOS.WPF.ViewModels.Sales;

public sealed class CustomerPickerViewModel : ObservableObject
{
    private readonly ISalesService _sales;
    private string _searchText = string.Empty;
    private CustomerLookupDto? _selected;
    private string? _hint;
    private CancellationTokenSource? _cts;

    public CustomerPickerViewModel(ISalesService sales, string? seed = null)
    {
        _sales = sales;
        _searchText = seed?.Trim() ?? string.Empty;
        SearchCommand = new AsyncRelayCommand(_ => SearchAsync());
        SelectCommand = new RelayCommand(_ => Confirm(), _ => Selected is not null);
        CancelCommand = new RelayCommand(_ =>
        {
            Confirmed = false;
            RequestClose?.Invoke();
        });
    }

    public event Action? RequestClose;
    public bool Confirmed { get; private set; }
    public CustomerLookupDto? SelectedCustomer => Confirmed ? Selected : null;

    public ObservableCollection<CustomerLookupDto> Results { get; } = new();

    public ICommand SearchCommand { get; }
    public ICommand SelectCommand { get; }
    public ICommand CancelCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;
            _ = DebouncedSearchAsync();
        }
    }

    public CustomerLookupDto? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public string? Hint
    {
        get => _hint;
        private set => SetProperty(ref _hint, value);
    }

    public async Task EnsureInitialSearchAsync()
    {
        if (!string.IsNullOrWhiteSpace(SearchText))
            await SearchAsync();
    }

    private async Task DebouncedSearchAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        try
        {
            await Task.Delay(250, token);
            await SearchAsync(token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SearchAsync(CancellationToken ct = default)
    {
        var term = SearchText.Trim();
        if (term.Length < 1)
        {
            Results.Clear();
            Hint = "Type a name or mobile number.";
            return;
        }

        var rows = await _sales.SearchCustomersAsync(term, ct);
        Results.Clear();
        foreach (var row in rows)
            Results.Add(row);
        Selected = Results.FirstOrDefault();
        Hint = Results.Count == 0 ? "No matching customers." : null;
    }

    public void Confirm()
    {
        if (Selected is null) return;
        Confirmed = true;
        RequestClose?.Invoke();
    }

    public void MoveSelection(int delta)
    {
        if (Results.Count == 0) return;
        var index = Selected is null ? -1 : Results.IndexOf(Selected);
        index = Math.Clamp(index + delta, 0, Results.Count - 1);
        Selected = Results[index];
    }
}
