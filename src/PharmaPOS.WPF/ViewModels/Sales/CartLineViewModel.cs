using PharmaPOS.Application.Features.Sales;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Sales;

/// <summary>
/// A single editable line in the billing cart. Prices are MRP / GST-inclusive;
/// discount is derived from MRP vs sale price (unit price).
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
        set
        {
            if (!SetProperty(ref _mrp, value)) return;
            UpdateDiscountFromPrices();
            Recalculate();
        }
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
            if (!SetProperty(ref _unitPrice, value)) return;
            UpdateDiscountFromPrices();
            Recalculate();
        }
    }

    public decimal DiscountPercent
    {
        get => _discountPercent;
        set
        {
            var clamped = Math.Clamp(value, 0m, 100m);
            if (!SetProperty(ref _discountPercent, clamped)) return;
            if (Mrp > 0)
            {
                var newUnit = SaleLinePricing.UnitPriceFromDiscount(Mrp, clamped);
                if (_unitPrice != newUnit)
                {
                    _unitPrice = newUnit;
                    OnPropertyChanged(nameof(UnitPrice));
                }
            }
            Recalculate();
        }
    }

    public bool IsEmpty => MedicineId == 0;

    public decimal Gross => SaleLinePricing.GrossAtMrp(Mrp, Quantity);
    public decimal DiscountAmount => SaleLinePricing.DiscountAmount(Mrp, UnitPrice, Quantity);
    public decimal NetInclusive => SaleLinePricing.LineTotal(UnitPrice, Quantity);
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
        GstPercent = selection.GstPercent;
        AvailableStock = selection.AvailableStock;
        _mrp = selection.Mrp;
        _unitPrice = selection.UnitPrice;
        OnPropertyChanged(nameof(Mrp));
        OnPropertyChanged(nameof(UnitPrice));

        if (selection.DefaultDiscountPercent > 0 && selection.Mrp > 0)
            DiscountPercent = selection.DefaultDiscountPercent;
        else
            UpdateDiscountFromPrices();

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
        GstPercent = line.GstPercent;
        AvailableStock = line.AvailableStock;
        _mrp = line.Mrp > 0 ? line.Mrp : line.UnitPrice;
        _unitPrice = line.UnitPrice;
        OnPropertyChanged(nameof(Mrp));
        OnPropertyChanged(nameof(UnitPrice));
        UpdateDiscountFromPrices();
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

    private void UpdateDiscountFromPrices()
    {
        var pct = SaleLinePricing.DiscountPercent(Mrp, UnitPrice);
        if (_discountPercent == pct) return;
        _discountPercent = pct;
        OnPropertyChanged(nameof(DiscountPercent));
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
