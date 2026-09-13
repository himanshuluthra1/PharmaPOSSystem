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
    private bool _isReturnLine;
    private string? _returnNumber;

    private string? _locationLabel;

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
        set
        {
            if (SetProperty(ref _batchNumber, value ?? string.Empty))
                Changed?.Invoke();
        }
    }

    public DateTime? ExpiryDate
    {
        get => _expiryDate;
        set
        {
            if (SetProperty(ref _expiryDate, value))
            {
                OnPropertyChanged(nameof(ExpiryDisplay));
                Changed?.Invoke();
            }
        }
    }

    public string ExpiryDisplay
    {
        get => ExpiryDate?.ToString("MM/yy") ?? "";
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim() == "-")
            {
                ExpiryDate = null;
                return;
            }

            var raw = value.Trim();
            if (DateTime.TryParseExact(raw, ["MM/yy", "M/yy", "MM/yyyy", "M/yyyy", "dd/MM/yyyy"],
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsed)
                || DateTime.TryParse(raw, out parsed))
            {
                // Store as last day of month for MM/yy style entries.
                ExpiryDate = new DateTime(parsed.Year, parsed.Month, DateTime.DaysInMonth(parsed.Year, parsed.Month));
            }
            else
            {
                OnPropertyChanged(nameof(ExpiryDisplay));
            }
        }
    }

    public string? LocationLabel
    {
        get => _locationLabel;
        private set => SetProperty(ref _locationLabel, value);
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
        set
        {
            var clamped = Math.Clamp(value, 0m, 100m);
            if (SetProperty(ref _gstPercent, clamped))
                Recalculate();
        }
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
    public bool IsReturnLine => _isReturnLine;
    public bool IsEditable => !IsEmpty && !IsReturnLine;
    public string? ReturnNumber => _returnNumber;

    public decimal Gross => SaleLinePricing.GrossAtMrp(Mrp, Quantity);
    public decimal DiscountAmount => SaleLinePricing.DiscountAmount(Mrp, UnitPrice, Quantity);
    public decimal NetInclusive => SaleLinePricing.LineTotal(UnitPrice, Quantity);
    public decimal Taxable => Math.Round(NetInclusive / (1 + GstPercent / 100m), 2);
    public decimal TaxAmount => NetInclusive - Taxable;
    public decimal LineTotal => NetInclusive;

    public decimal OriginalQuantity { get; private set; }

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
        LocationLabel = selection.LocationLabel;
        _mrp = selection.Mrp;
        _unitPrice = selection.UnitPrice;
        OnPropertyChanged(nameof(Mrp));
        OnPropertyChanged(nameof(UnitPrice));

        if (selection.DefaultDiscountPercent > 0 && selection.Mrp > 0)
            DiscountPercent = selection.DefaultDiscountPercent;
        else
            UpdateDiscountFromPrices();

        // Never invent stock qty: clamp to available (0 when out of stock).
        if (AvailableStock <= 0)
            Quantity = 0;
        else if (Quantity <= 0)
            Quantity = 1;
        else if (Quantity > AvailableStock)
            Quantity = AvailableStock;

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
        LocationLabel = line.LocationLabel;
        _isReturnLine = line.IsReturnLine;
        _returnNumber = line.ReturnNumber;
        OnPropertyChanged(nameof(IsReturnLine));
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(ReturnNumber));
        _mrp = line.Mrp > 0 ? line.Mrp : line.UnitPrice;
        _unitPrice = line.UnitPrice;
        OnPropertyChanged(nameof(Mrp));
        OnPropertyChanged(nameof(UnitPrice));
        UpdateDiscountFromPrices();
        Quantity = line.Quantity;
        OriginalQuantity = line.IsReturnLine ? 0 : line.Quantity;
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
        LocationLabel = null;
        UnitPrice = 0;
        DiscountPercent = 0;
        OriginalQuantity = 0;
        _isReturnLine = false;
        _returnNumber = null;
        OnPropertyChanged(nameof(IsReturnLine));
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(ReturnNumber));
        Quantity = 0;
        Recalculate();
    }

    internal ParkedCartLine ToParked() => new()
    {
        MedicineId = MedicineId,
        BatchId = BatchId,
        MedicineName = MedicineName,
        BatchNumber = BatchNumber,
        ExpiryDate = ExpiryDate,
        Mrp = Mrp,
        GstPercent = GstPercent,
        AvailableStock = AvailableStock,
        LocationLabel = LocationLabel,
        Quantity = Quantity,
        UnitPrice = UnitPrice,
        DiscountPercent = DiscountPercent,
        OriginalQuantity = OriginalQuantity,
        IsReturnLine = IsReturnLine,
        ReturnNumber = ReturnNumber
    };

    internal void LoadFromParked(ParkedCartLine line)
    {
        MedicineId = line.MedicineId;
        BatchId = line.BatchId;
        MedicineName = line.MedicineName;
        BatchNumber = line.BatchNumber;
        ExpiryDate = line.ExpiryDate;
        GstPercent = line.GstPercent;
        AvailableStock = line.AvailableStock;
        LocationLabel = line.LocationLabel;
        _isReturnLine = line.IsReturnLine;
        _returnNumber = line.ReturnNumber;
        OnPropertyChanged(nameof(IsReturnLine));
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(ReturnNumber));
        _mrp = line.Mrp;
        _unitPrice = line.UnitPrice;
        _discountPercent = line.DiscountPercent;
        OnPropertyChanged(nameof(Mrp));
        OnPropertyChanged(nameof(UnitPrice));
        OnPropertyChanged(nameof(DiscountPercent));
        Quantity = line.Quantity;
        OriginalQuantity = line.OriginalQuantity;
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
