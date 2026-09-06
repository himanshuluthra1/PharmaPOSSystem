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
    private string _expiryMonth = string.Empty;
    private string _expiryYear = string.Empty;
    private bool _syncingExpiry;
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

    /// <summary>Expiry month text for the grid: 01–12 only.</summary>
    public string ExpiryMonth
    {
        get => _expiryMonth;
        set
        {
            var next = SanitizeMonth(value, _expiryMonth);
            if (!SetProperty(ref _expiryMonth, next)) return;
            if (!_syncingExpiry)
                RebuildExpiryDateFromParts();
        }
    }

    /// <summary>Expiry year text for the grid: exactly 4 digits (e.g. 2027).</summary>
    public string ExpiryYear
    {
        get => _expiryYear;
        set
        {
            var next = SanitizeYear(value, _expiryYear);
            if (!SetProperty(ref _expiryYear, next)) return;
            if (!_syncingExpiry)
                RebuildExpiryDateFromParts();
        }
    }

    public DateTime? ExpiryDate
    {
        get => _expiryDate;
        set
        {
            if (!SetProperty(ref _expiryDate, value)) return;
            if (_syncingExpiry) return;
            _syncingExpiry = true;
            try
            {
                if (value is DateTime d)
                {
                    SetProperty(ref _expiryMonth, d.Month.ToString("00"));
                    SetProperty(ref _expiryYear, d.Year.ToString("0000"));
                }
                else
                {
                    SetProperty(ref _expiryMonth, string.Empty);
                    SetProperty(ref _expiryYear, string.Empty);
                }
            }
            finally
            {
                _syncingExpiry = false;
            }
        }
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

    private void RebuildExpiryDateFromParts()
    {
        _syncingExpiry = true;
        try
        {
            if (TryParseMonth(_expiryMonth, out var m) && TryParseYear(_expiryYear, out var y))
            {
                // Do not rewrite Month/Year text here — padding "1" → "01" while typing
                // prevents entering "12".
                SetProperty(ref _expiryDate, new DateTime(y, m, DateTime.DaysInMonth(y, m)));
            }
            else if (string.IsNullOrEmpty(_expiryMonth) && string.IsNullOrEmpty(_expiryYear))
            {
                SetProperty(ref _expiryDate, null);
            }
        }
        finally
        {
            _syncingExpiry = false;
        }
    }

    /// <summary>Digits only, max 2 chars; value must be 1–12 (leading 0 allowed while typing 01–09).</summary>
    internal static string SanitizeMonth(string? raw, string previous)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length > 2) digits = digits[..2];
        if (digits.Length == 0) return string.Empty;

        // Allow "0" / "01"–"09" while typing; reject "00".
        if (digits == "0") return "0";
        if (digits == "00") return previous;

        if (!int.TryParse(digits, out var m)) return previous;
        if (m is >= 1 and <= 12) return digits; // keep "1" or "12" as typed — do not pad
        return previous;
    }

    /// <summary>Digits only, max 4 chars; only 2000–2100 accepted when complete.</summary>
    internal static string SanitizeYear(string? raw, string previous)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length > 4) digits = digits[..4];
        if (digits.Length == 0) return string.Empty;

        // While typing (1–3 digits), keep as typed — do not auto-expand to 20xx.
        if (digits.Length < 4) return digits;

        if (!int.TryParse(digits, out var y)) return previous;
        if (y is >= 2000 and <= 2100) return digits;
        return previous;
    }

    private static bool TryParseMonth(string text, out int month)
    {
        month = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (!int.TryParse(text, out month)) return false;
        return month is >= 1 and <= 12;
    }

    private static bool TryParseYear(string text, out int year)
    {
        year = 0;
        if (string.IsNullOrWhiteSpace(text) || text.Length != 4) return false;
        if (!int.TryParse(text, out year)) return false;
        return year is >= 2000 and <= 2100;
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
