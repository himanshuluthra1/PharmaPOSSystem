using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Inventory;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Inventory;

public class StockAdjustmentTabViewModel : ObservableObject
{
    private readonly IInventoryService _inventory;
    private readonly int? _branchId;
    private readonly IMedicinePickerService _medicinePicker;
    private readonly IDialogService _dialog;

    private string _adjustmentNumber = string.Empty;
    private DateTime _adjustmentDate = DateTime.Today;
    private string? _reason;
    private bool _isBusy;
    private string? _statusMessage;

    public StockAdjustmentTabViewModel(
        IInventoryService inventory,
        ICurrentUserService currentUser,
        IMedicinePickerService medicinePicker,
        IDialogService dialog)
    {
        _inventory = inventory;
        _branchId = currentUser.CurrentUser?.BranchId;
        _medicinePicker = medicinePicker;
        _dialog = dialog;

        AddLineCommand = new AsyncRelayCommand(_ => AddLineAsync(), _ => !IsBusy);
        RemoveLineCommand = new RelayCommand(p => RemoveLine(p as StockAdjustmentLineViewModel));
        SaveCommand = new AsyncRelayCommand(_ => SaveAsync(), _ => !IsBusy && Lines.Count > 0);
        NewCommand = new RelayCommand(_ => ResetForm());
        _ = InitializeAsync();
    }

    public ObservableCollection<StockAdjustmentLineViewModel> Lines { get; } = new();

    public string AdjustmentNumber
    {
        get => _adjustmentNumber;
        private set => SetProperty(ref _adjustmentNumber, value);
    }

    public DateTime AdjustmentDate
    {
        get => _adjustmentDate;
        set => SetProperty(ref _adjustmentDate, value);
    }

    public string? Reason
    {
        get => _reason;
        set => SetProperty(ref _reason, value);
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

    public ICommand AddLineCommand { get; }
    public ICommand RemoveLineCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand NewCommand { get; }

    private async Task InitializeAsync()
    {
        try
        {
            AdjustmentNumber = await _inventory.PreviewNextAdjustmentNumberAsync(_branchId);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not preview adjustment number: {ex.Message}";
        }
    }

    private async Task AddLineAsync()
    {
        var pick = await _medicinePicker.PickMedicineAsync();
        if (pick is null) return;

        if (Lines.Any(l => l.BatchId == pick.BatchId))
        {
            _dialog.ShowInfo("This batch is already on the adjustment.", "Duplicate batch");
            return;
        }

        Lines.Add(new StockAdjustmentLineViewModel(
            pick.MedicineId,
            pick.BatchId,
            pick.MedicineName,
            pick.BatchNumber,
            pick.AvailableStock));

        StatusMessage = $"{Lines.Count} line(s) on adjustment.";
    }

    private void RemoveLine(StockAdjustmentLineViewModel? line)
    {
        if (line is null) return;
        Lines.Remove(line);
        StatusMessage = Lines.Count == 0 ? null : $"{Lines.Count} line(s) on adjustment.";
    }

    private async Task SaveAsync()
    {
        var changedLines = Lines.Where(l => l.Difference != 0).ToList();
        if (changedLines.Count == 0)
        {
            _dialog.ShowInfo("Update physical quantities so at least one line has a difference.", "No changes");
            return;
        }

        IsBusy = true;
        StatusMessage = "Saving adjustment...";
        try
        {
            var request = new CreateStockAdjustmentRequest
            {
                AdjustmentDate = AdjustmentDate,
                Reason = Reason,
                Lines = changedLines.Select(l => new StockAdjustmentLineRequest
                {
                    MedicineId = l.MedicineId,
                    MedicineBatchId = l.BatchId,
                    SystemQuantity = l.SystemQuantity,
                    PhysicalQuantity = l.PhysicalQuantity,
                    Remarks = l.Remarks
                }).ToList()
            };

            var result = await _inventory.CreateStockAdjustmentAsync(request, _branchId);
            if (result.IsFailure || result.Value is null)
            {
                _dialog.ShowError(result.Error ?? "Could not save adjustment.");
                return;
            }

            _dialog.ShowInfo(
                $"Adjustment {result.Value.AdjustmentNumber} saved.\n{result.Value.LinesAdjusted} line(s) updated.");
            ResetForm();
            await InitializeAsync();
        }
        catch (Exception ex)
        {
            _dialog.ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ResetForm()
    {
        Lines.Clear();
        AdjustmentDate = DateTime.Today;
        Reason = null;
        StatusMessage = null;
    }
}
