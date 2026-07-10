using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Inventory;
using PharmaPOS.WPF.Mvvm;

namespace PharmaPOS.WPF.ViewModels.Inventory;

public class StockLedgerTabViewModel : ObservableObject
{
    private readonly IInventoryService _inventory;
    private readonly int? _branchId;

    private string _searchText = string.Empty;
    private string? _filterLabel;
    private int? _filterMedicineId;
    private int? _filterBatchId;
    private bool _isBusy;
    private string? _statusMessage;
    private CancellationTokenSource? _searchCts;

    public StockLedgerTabViewModel(IInventoryService inventory, ICurrentUserService currentUser)
    {
        _inventory = inventory;
        _branchId = currentUser.CurrentUser?.BranchId;
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync(), _ => !IsBusy);
        ClearFilterCommand = new RelayCommand(_ => ClearFilter());
    }

    public ObservableCollection<StockLedgerRowDto> Items { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                _ = DebouncedSearchAsync();
        }
    }

    public string? FilterLabel
    {
        get => _filterLabel;
        private set => SetProperty(ref _filterLabel, value);
    }

    public bool HasFilter => _filterMedicineId.HasValue;

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
    public ICommand ClearFilterCommand { get; }

    public void ApplyStockFilter(int medicineId, int batchId, string? medicineName = null, string? batchNumber = null)
    {
        _filterMedicineId = medicineId;
        _filterBatchId = batchId;
        FilterLabel = $"Filtered: {medicineName ?? "Medicine"} / {batchNumber ?? "batch"}";
        OnPropertyChanged(nameof(HasFilter));
        _ = RefreshAsync();
    }

    public void ClearFilter()
    {
        _filterMedicineId = null;
        _filterBatchId = null;
        FilterLabel = null;
        OnPropertyChanged(nameof(HasFilter));
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var rows = await _inventory.GetStockLedgerAsync(
                SearchText,
                _filterMedicineId,
                _filterBatchId,
                _branchId);

            Items.Clear();
            foreach (var row in rows)
                Items.Add(row);

            StatusMessage = rows.Count == 0
                ? "No stock movements found."
                : $"{rows.Count} movement(s) loaded.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load ledger: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
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
