using PharmaPOS.Application.Features.Sales;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Sales;

/// <summary>
/// A single editable line in the billing cart. Prices are MRP / GST-inclusive;
/// the line derives taxable, GST and total amounts to mirror the server-side math.
/// </summary>
public class CartLineViewModel : ObservableObject
{
    private int _medicineId;
    private int _batchId;
    private string _medicineName = string.Empty;
    private string _batchNumber = string.Empty;
    private DateTime? _expiryDate;
    private decimal _mrp;
    private decimal _gstPercent;
    private decimal _availableStock;
    private decimal _quantity;
    private decimal _unitPrice;
    private decimal _discountPercent;

    /// <summary>Raised whenever a value that affects totals changes.</summary>
    public event Action? Changed;

    public int MedicineId
    {
        get => _medicineId;
        private set
        {
            if (SetProperty(ref _medicineId, value))
                OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public int BatchId
    {
        get => _batchId;
        private set => SetProperty(ref _batchId, value);
    }

    public string MedicineName
    {
        get => _medicineName;
        private set => SetProperty(ref _medicineName, value);
    }

    public string BatchNumber
    {
        get => _batchNumber;
        private set => SetProperty(ref _batchNumber, value);
    }

    public DateTime? ExpiryDate
    {
        get => _expiryDate;
        private set
        {
            if (SetProperty(ref _expiryDate, value))
                OnPropertyChanged(nameof(ExpiryDisplay));
        }
    }

    public decimal Mrp
    {
        get => _mrp;
        private set => SetProperty(ref _mrp, value);
    }

    public decimal GstPercent
    {
        get => _gstPercent;
        private set => SetProperty(ref _gstPercent, value);
    }

    public decimal AvailableStock
    {
        get => _availableStock;
        private set => SetProperty(ref _availableStock, value);
    }

    public decimal Quantity
    {
        get => _quantity;
        set { if (SetProperty(ref _quantity, value)) Recalculate(); }
    }

    public decimal UnitPrice
    {
        get => _unitPrice;
        set
        {
            if (SetProperty(ref _unitPrice, value))
            {
                _mrp = value;
                OnPropertyChanged(nameof(Mrp));
                Recalculate();
            }
        }
    }

    public decimal DiscountPercent
    {
        get => _discountPercent;
        set { if (SetProperty(ref _discountPercent, value)) Recalculate(); }
    }

    public bool IsEmpty => MedicineId == 0;

    public decimal Gross => Math.Round(UnitPrice * Quantity, 2);
    public decimal DiscountAmount => Math.Round(Gross * DiscountPercent / 100m, 2);
    public decimal NetInclusive => Gross - DiscountAmount;
    public decimal Taxable => Math.Round(NetInclusive / (1 + GstPercent / 100m), 2);
    public decimal TaxAmount => NetInclusive - Taxable;
    public decimal LineTotal => NetInclusive;

    public decimal OriginalQuantity { get; private set; }

    public string ExpiryDisplay => ExpiryDate?.ToString("MM/yy") ?? "-";

    public static CartLineViewModel CreateEmpty()
    {
        var line = new CartLineViewModel();
        line.Clear();
        return line;
    }

    public void ApplySelection(MedicineBatchSelection selection)
    {
        MedicineId = selection.MedicineId;
        BatchId = selection.BatchId;
        MedicineName = selection.MedicineName;
        BatchNumber = selection.BatchNumber;
        ExpiryDate = selection.ExpiryDate;
        Mrp = selection.Mrp;
        GstPercent = selection.GstPercent;
        AvailableStock = selection.AvailableStock;
        UnitPrice = selection.UnitPrice;
        DiscountPercent = selection.DefaultDiscountPercent;
        if (Quantity <= 0) Quantity = 1;
        OriginalQuantity = 0;
        Recalculate();
    }

    public void LoadFromSaleLine(SaleEditLineDto line)
    {
        MedicineId = line.MedicineId;
        BatchId = line.MedicineBatchId;
        MedicineName = line.MedicineName;
        BatchNumber = line.BatchNumber;
        ExpiryDate = line.ExpiryDate;
        Mrp = line.Mrp;
        GstPercent = line.GstPercent;
        AvailableStock = line.AvailableStock;
        UnitPrice = line.UnitPrice;
        DiscountPercent = line.DiscountPercent;
        Quantity = line.Quantity;
        OriginalQuantity = line.Quantity;
        Recalculate();
    }

    public void Clear()
    {
        MedicineId = 0;
        BatchId = 0;
        MedicineName = string.Empty;
        BatchNumber = string.Empty;
        ExpiryDate = null;
        Mrp = 0;
        GstPercent = 0;
        AvailableStock = 0;
        UnitPrice = 0;
        DiscountPercent = 0;
        OriginalQuantity = 0;
        Quantity = 0;
        Recalculate();
    }

    private void Recalculate()
    {
        OnPropertyChanged(nameof(Gross));
        OnPropertyChanged(nameof(DiscountAmount));
        OnPropertyChanged(nameof(NetInclusive));
        OnPropertyChanged(nameof(Taxable));
        OnPropertyChanged(nameof(TaxAmount));
        OnPropertyChanged(nameof(LineTotal));
        Changed?.Invoke();
    }
}
