using PharmaPOS.WPF.Mvvm;

namespace PharmaPOS.WPF.ViewModels.Inventory;

public class StockTransferLineViewModel : ObservableObject
{
    private decimal _transferQuantity;
    private string? _remarks;

    public StockTransferLineViewModel(
        int medicineId,
        int batchId,
        string medicineName,
        string batchNumber,
        decimal availableQuantity,
        DateTime? expiryDate)
    {
        MedicineId = medicineId;
        BatchId = batchId;
        MedicineName = medicineName;
        BatchNumber = batchNumber;
        AvailableQuantity = availableQuantity;
        ExpiryDate = expiryDate;
        _transferQuantity = availableQuantity > 0 ? 1 : 0;
    }

    public int MedicineId { get; }
    public int BatchId { get; }
    public string MedicineName { get; }
    public string BatchNumber { get; }
    public decimal AvailableQuantity { get; }
    public DateTime? ExpiryDate { get; }
    public string ExpiryDisplay => ExpiryDate?.ToString("MM/yyyy") ?? "—";

    public decimal TransferQuantity
    {
        get => _transferQuantity;
        set => SetProperty(ref _transferQuantity, value);
    }

    public string? Remarks
    {
        get => _remarks;
        set => SetProperty(ref _remarks, value);
    }
}
