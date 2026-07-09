using PharmaPOS.WPF.Mvvm;

namespace PharmaPOS.WPF.ViewModels.Purchases;

/// <summary>
/// A single editable line on the purchase-entry grid. Purchase prices are
/// tax-exclusive; the line derives taxable, GST and total amounts to mirror the
/// server-side math.
/// </summary>
public class PurchaseLineViewModel : ObservableObject
{
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

    public required int MedicineId { get; init; }
    public required string MedicineName { get; init; }
    public string? GenericName { get; init; }

    /// <summary>Raised whenever a value that affects totals changes.</summary>
    public event Action? Changed;

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
