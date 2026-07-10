using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Inventory;
using PharmaPOS.WPF.Mvvm;

namespace PharmaPOS.WPF.ViewModels.Inventory;

public class StockOnHandTabViewModel : ObservableObject
{
    private readonly IInventoryService _inventory;
    private readonly int? _branchId;
    private readonly Action<StockBatchRowDto?> _onSelectionChanged;

    private string _searchText = string.Empty;
    private StockFilterOption _selectedFilter;
    private StockSummaryDto _summary = new();
    private StockBatchRowDto? _selectedItem;
    private bool _isBusy;
    private string? _statusMessage;
    private CancellationTokenSource? _searchCts;

    public StockOnHandTabViewModel(
        IInventoryService inventory,
        ICurrentUserService currentUser,
        Action<StockBatchRowDto?> onSelectionChanged)
    {
        _inventory = inventory;
        _branchId = currentUser.CurrentUser?.BranchId;
        _onSelectionChanged = onSelectionChanged;

        FilterOptions =
        [
            new(StockFilterKind.All, "All batches"),
            new(StockFilterKind.InStock, "In stock"),
            new(StockFilterKind.LowStock, "Low stock"),
            new(StockFilterKind.NearExpiry, "Near expiry"),
            new(StockFilterKind.Expired, "Expired"),
            new(StockFilterKind.ZeroStock, "Zero stock")
        ];
        _selectedFilter = FilterOptions[1];

        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync(), _ => !IsBusy);
        _ = RefreshAsync();
    }

    public IReadOnlyList<StockFilterOption> FilterOptions { get; }

    public ObservableCollection<StockBatchRowDto> Items { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                _ = DebouncedSearchAsync();
        }
    }

    public StockFilterOption SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (SetProperty(ref _selectedFilter, value))
                _ = SearchAsync();
        }
    }

    public StockSummaryDto Summary
    {
        get => _summary;
        private set
        {
            if (SetProperty(ref _summary, value))
                OnPropertyChanged(nameof(AlertsSummaryText));
        }
    }

    public string AlertsSummaryText =>
        $"Low {_summary.LowStockCount}   Exp {_summary.ExpiredCount}   Near {_summary.NearExpiryCount}";

    public StockBatchRowDto? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
                _onSelectionChanged(value);
        }
    }

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

    public ICommand RefreshCommand { get; }

    private async Task DebouncedSearchAsync()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        try
        {
            await Task.Delay(300, token);
            await SearchAsync(token);
        }
        catch (OperationCanceledException) { }
    }

    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            Summary = await _inventory.GetStockSummaryAsync(_branchId);
            await SearchAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load stock: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SearchAsync(CancellationToken ct = default)
    {
        try
        {
            var rows = await _inventory.SearchStockBatchesAsync(
                SearchText, SelectedFilter.Kind, _branchId, ct);

            Items.Clear();
            foreach (var row in rows)
                Items.Add(row);

            StatusMessage = rows.Count == 0
                ? "No stock batches match the current filter."
                : $"{rows.Count} batch row(s) shown.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Search failed: {ex.Message}";
        }
    }
}
