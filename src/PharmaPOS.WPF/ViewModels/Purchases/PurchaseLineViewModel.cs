using PharmaPOS.Application.Features.Purchases;
using PharmaPOS.WPF.Mvvm;

namespace PharmaPOS.WPF.ViewModels.Purchases;

/// <summary>
/// A single editable line on the purchase-entry grid. Purchase prices are
/// tax-exclusive; the line derives taxable, GST and total amounts to mirror the
/// server-side math.
/// </summary>
public class PurchaseLineViewModel : ObservableObject
{
    private int _medicineId;
    private string _medicineName = string.Empty;
    private string? _genericName;
    private string _batchNumber = string.Empty;
    private DateTime? _manufacturingDate;
    private DateTime? _expiryDate;
    private decimal _quantity = 1;
    private decimal _freeQuantity;
    private decimal _purchasePrice;
    private decimal _mrp;
    private decimal _sellingPrice;
    private decimal _discountPercent;
    private decimal _gstPercent;

    public event Action? Changed;

    public static PurchaseLineViewModel CreateEmpty() => new();

    public bool IsEmpty => MedicineId <= 0;

    public int MedicineId
    {
        get => _medicineId;
        private set
        {
            if (SetProperty(ref _medicineId, value))
                OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public string MedicineName
    {
        get => _medicineName;
        private set => SetProperty(ref _medicineName, value);
    }

    public string? GenericName
    {
        get => _genericName;
        private set => SetProperty(ref _genericName, value);
    }

    public string BatchNumber
    {
        get => _batchNumber;
        set => SetProperty(ref _batchNumber, value);
    }

    public DateTime? ManufacturingDate
    {
        get => _manufacturingDate;
        set => SetProperty(ref _manufacturingDate, value);
    }

    public DateTime? ExpiryDate
    {
        get => _expiryDate;
        set => SetProperty(ref _expiryDate, value);
    }

    public decimal Quantity
    {
        get => _quantity;
        set { if (SetProperty(ref _quantity, value)) Recalculate(); }
    }

    public decimal FreeQuantity
    {
        get => _freeQuantity;
        set { if (SetProperty(ref _freeQuantity, value)) Recalculate(); }
    }

    public decimal PurchasePrice
    {
        get => _purchasePrice;
        set { if (SetProperty(ref _purchasePrice, value)) Recalculate(); }
    }

    public decimal Mrp
    {
        get => _mrp;
        set => SetProperty(ref _mrp, value);
    }

    public decimal SellingPrice
    {
        get => _sellingPrice;
        set => SetProperty(ref _sellingPrice, value);
    }

    public decimal DiscountPercent
    {
        get => _discountPercent;
        set { if (SetProperty(ref _discountPercent, value)) Recalculate(); }
    }

    public decimal GstPercent
    {
        get => _gstPercent;
        set { if (SetProperty(ref _gstPercent, value)) Recalculate(); }
    }

    public decimal Gross => Math.Round(PurchasePrice * Quantity, 2);
    public decimal DiscountAmount => Math.Round(Gross * DiscountPercent / 100m, 2);
    public decimal Taxable => Gross - DiscountAmount;
    public decimal TaxAmount => Math.Round(Taxable * GstPercent / 100m, 2);
    public decimal LineTotal => Taxable + TaxAmount;

    public void ApplyMedicine(PurchaseMedicineDto medicine)
    {
        MedicineId = medicine.Id;
        MedicineName = medicine.Name;
        GenericName = medicine.GenericName;
        PurchasePrice = medicine.PurchasePrice;
        Mrp = medicine.Mrp;
        SellingPrice = medicine.SellingPrice > 0 ? medicine.SellingPrice : medicine.Mrp;
        GstPercent = medicine.GstPercent;
        ExpiryDate ??= DateTime.Today.AddYears(2);
        Quantity = Quantity <= 0 ? 1 : Quantity;
        Recalculate();
    }

    public void LoadFrom(PurchaseLoadLineDto line)
    {
        MedicineId = line.MedicineId;
        MedicineName = line.MedicineName;
        GenericName = line.GenericName;
        BatchNumber = line.BatchNumber;
        ManufacturingDate = line.ManufacturingDate;
        ExpiryDate = line.ExpiryDate;
        Quantity = line.Quantity;
        FreeQuantity = line.FreeQuantity;
        PurchasePrice = line.PurchasePrice;
        Mrp = line.Mrp;
        SellingPrice = line.SellingPrice;
        DiscountPercent = line.DiscountPercent;
        GstPercent = line.GstPercent;
        Recalculate();
    }

    private void Recalculate()
    {
        OnPropertyChanged(nameof(Gross));
        OnPropertyChanged(nameof(DiscountAmount));
        OnPropertyChanged(nameof(Taxable));
        OnPropertyChanged(nameof(TaxAmount));
        OnPropertyChanged(nameof(LineTotal));
        Changed?.Invoke();
    }
}
