using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Inventory;
using PharmaPOS.Shared.Constants;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Inventory;

/// <summary>Shell view model for the Inventory module.</summary>
public class InventoryViewModel : ObservableObject
{
    private int _selectedTab;
    private StockBatchRowDto? _selectedStockBatch;

    public InventoryViewModel(
        IInventoryService inventory,
        ICurrentUserService currentUser,
        IMedicinePickerService medicinePicker,
        IDialogService dialog)
    {
        CanAdjustStock = currentUser.HasAnyPermission(
            AppConstants.Permissions.InventoryAdjust, AppConstants.Permissions.InventoryManage);

        StockOnHand = new StockOnHandTabViewModel(inventory, currentUser, OnStockBatchSelected);
        StockLedger = new StockLedgerTabViewModel(inventory, currentUser);
        StockAdjustment = new StockAdjustmentTabViewModel(inventory, currentUser, medicinePicker, dialog);

        ViewLedgerCommand = new RelayCommand(_ =>
        {
            if (SelectedStockBatch is not StockBatchRowDto batch) return;
            StockLedger.ApplyStockFilter(batch.MedicineId, batch.BatchId, batch.MedicineName, batch.BatchNumber);
            SelectedTab = 1;
        }, _ => SelectedStockBatch is not null);
    }

    public StockOnHandTabViewModel StockOnHand { get; }
    public StockLedgerTabViewModel StockLedger { get; }
    public StockAdjustmentTabViewModel StockAdjustment { get; }

    public bool CanAdjustStock { get; }

    public int SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (!SetProperty(ref _selectedTab, value)) return;
            if (value == 1)
                _ = StockLedger.RefreshAsync();
        }
    }

    public StockBatchRowDto? SelectedStockBatch
    {
        get => _selectedStockBatch;
        private set => SetProperty(ref _selectedStockBatch, value);
    }

    public ICommand ViewLedgerCommand { get; }

    private void OnStockBatchSelected(StockBatchRowDto? batch)
    {
        SelectedStockBatch = batch;
        CommandManager.InvalidateRequerySuggested();
    }
}
