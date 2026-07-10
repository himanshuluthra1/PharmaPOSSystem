using PharmaPOS.WPF.Mvvm;

namespace PharmaPOS.WPF.ViewModels.Inventory;

public class StockAdjustmentLineViewModel : ObservableObject
{
    private decimal _physicalQuantity;
    private string? _remarks;

    public StockAdjustmentLineViewModel(
        int medicineId,
        int batchId,
        string medicineName,
        string batchNumber,
        decimal systemQuantity)
    {
        MedicineId = medicineId;
        BatchId = batchId;
        MedicineName = medicineName;
        BatchNumber = batchNumber;
        SystemQuantity = systemQuantity;
        _physicalQuantity = systemQuantity;
    }

    public int MedicineId { get; }
    public int BatchId { get; }
    public string MedicineName { get; }
    public string BatchNumber { get; }
    public decimal SystemQuantity { get; }

    public decimal PhysicalQuantity
    {
        get => _physicalQuantity;
        set
        {
            if (SetProperty(ref _physicalQuantity, value))
                OnPropertyChanged(nameof(Difference));
        }
    }

    public decimal Difference => PhysicalQuantity - SystemQuantity;

    public string? Remarks
    {
        get => _remarks;
        set => SetProperty(ref _remarks, value);
    }
}
