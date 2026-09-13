using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using PharmaPOS.Application.Features.Purchases;
using PharmaPOS.WPF.Services;
using PharmaPOS.WPF.ViewModels.Purchases;

namespace PharmaPOS.WPF.Views;

public partial class PurchaseBillScanWindow : Window, INotifyPropertyChanged
{
    private readonly ObservableCollection<ScannedLineRow> _lines = new();
    private ScannedPurchaseDraftDto _draft = new();
    private readonly IMedicinePickerService _medicinePicker;
    private readonly IPurchaseService _purchases;
    private bool _isScanning;

    public ScannedPurchaseDraftDto? AcceptedDraft { get; private set; }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (_isScanning == value) return;
            _isScanning = value;
            OnPropertyChanged();
            ScanningOverlay.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public PurchaseBillScanWindow(
        IPurchaseService purchases,
        IMedicinePickerService medicinePicker)
    {
        InitializeComponent();
        _purchases = purchases;
        _medicinePicker = medicinePicker;

        LinesGrid.ItemsSource = _lines;
    }

    public PurchaseBillScanWindow(
        ScannedPurchaseDraftDto draft,
        string imagePath,
        IPurchaseService purchases,
        IMedicinePickerService medicinePicker)
        : this(purchases, medicinePicker)
    {
        ApplyDraft(draft, imagePath);
    }

    public void ShowScanning() => IsScanning = true;

    public void ApplyDraft(ScannedPurchaseDraftDto draft, string imagePath)
    {
        _draft = draft;

        SupplierBox.Text = draft.SupplierName ?? string.Empty;
        InvoiceNoBox.Text = draft.SupplierInvoiceNumber ?? string.Empty;
        InvoiceDatePicker.SelectedDate = draft.InvoiceDate ?? DateTime.Today;

        _lines.Clear();
        foreach (var line in draft.Lines)
            _lines.Add(new ScannedLineRow(line));

        WarningText.Text = BuildWarningText(draft);

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(imagePath);
            bmp.EndInit();
            BillImage.Source = bmp;
        }
        catch
        {
            BillImage.Source = null;
        }

        IsScanning = false;
    }

    public Task ApplyDraftAsync(ScannedPurchaseDraftDto draft, string imagePath)
    {
        ApplyDraft(draft, imagePath);
        return Task.CompletedTask;
    }

    private static string BuildWarningText(ScannedPurchaseDraftDto draft)
    {
        var matchedCount = draft.Lines.Count(l => l.IsMatched);
        var hints = new List<string>(draft.Warnings);
        if (draft.Lines.Count > 0 && matchedCount == 0)
        {
            hints.Add(
                "No lines matched your medicine master. Click Pick on each row to map the OCR name to a medicine in your catalog, then Apply.");
        }
        else if (matchedCount < draft.Lines.Count)
        {
            hints.Add(
                $"{matchedCount}/{draft.Lines.Count} lines matched. Use Pick on unmatched rows (OK column empty) before Apply.");
        }

        return string.Join(" ", hints);
    }

    private async void PickMedicine_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ScannedLineRow row }) return;

        var pick = await _medicinePicker.PickMedicineLookupAsync();
        if (pick is null) return;

        row.MatchedMedicineId = pick.Id;
        row.MatchedMedicineName = pick.Name;
        if (row.GstPercent <= 0) row.GstPercent = pick.GstPercent;
        // Keep bill rate/MRP from OCR/Gemini; catalog prices are only a last resort later on Apply.
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        var matched = _lines.Where(l => l.IsMatched).Select(l => l.ToDto()).ToList();
        if (matched.Count == 0)
        {
            MessageBox.Show(
                "No medicines are linked yet.\n\n" +
                "For each row, click Pick and choose the matching medicine from your master.\n" +
                "Scanned names (e.g. from the supplier bill) often differ from your catalog names.\n\n" +
                "If the medicine does not exist, add it under Masters first, then scan again.",
                "Scan purchase bill",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(SupplierBox.Text))
        {
            MessageBox.Show("Enter the supplier name before applying.", "Scan purchase bill",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var unmatched = _lines.Count(l => !l.IsMatched);
        if (unmatched > 0)
        {
            var go = MessageBox.Show(
                $"{matched.Count} line(s) will be applied. {unmatched} unmatched line(s) will be skipped.\n\nContinue?",
                "Scan purchase bill",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (go != MessageBoxResult.Yes) return;
        }

        var supplierName = SupplierBox.Text.Trim();
        var matchedSupplierId = _draft.MatchedSupplierId;
        var matchedSupplierPhone = _draft.MatchedSupplierPhone;
        try
        {
            var hits = await _purchases.SearchSuppliersAsync(supplierName);
            var best = hits.FirstOrDefault(s =>
                           string.Equals(s.Name, supplierName, StringComparison.OrdinalIgnoreCase))
                       ?? hits.FirstOrDefault();
            if (best is not null)
            {
                matchedSupplierId = best.Id;
                matchedSupplierPhone = best.Phone;
                supplierName = best.Name;
            }
        }
        catch
        {
            // Keep OCR match; Purchase tab will try again.
        }

        // Window is shown with Show() (not ShowDialog) so scanning can update the UI —
        // DialogResult cannot be set in that mode; AcceptedDraft is the accept signal.
        AcceptedDraft = new ScannedPurchaseDraftDto
        {
            RawText = _draft.RawText,
            SupplierName = supplierName,
            MatchedSupplierId = matchedSupplierId,
            MatchedSupplierPhone = matchedSupplierPhone,
            SupplierInvoiceNumber = string.IsNullOrWhiteSpace(InvoiceNoBox.Text) ? null : InvoiceNoBox.Text.Trim(),
            InvoiceDate = InvoiceDatePicker.SelectedDate ?? DateTime.Today,
            GrandTotalHint = _draft.GrandTotalHint,
            Lines = matched,
            Warnings = _draft.Warnings
        };
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        AcceptedDraft = null;
        Close();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Editable grid row wrapping a scanned line (for match / pick UI).</summary>
public sealed class ScannedLineRow : INotifyPropertyChanged
{
    public ScannedLineRow(ScannedPurchaseLineDto source)
    {
        OcrItemName = source.OcrItemName;
        MatchedMedicineId = source.MatchedMedicineId;
        MatchedMedicineName = source.MatchedMedicineName;
        BatchNumber = source.BatchNumber;
        SetExpiryDate(source.ExpiryDate);
        Quantity = source.Quantity;
        FreeQuantity = source.FreeQuantity;
        PurchasePrice = source.PurchasePrice;
        Mrp = source.Mrp;
        SellingPrice = source.SellingPrice;
        GstPercent = source.GstPercent;
        DiscountPercent = source.DiscountPercent;
        LineAmountHint = source.LineAmountHint;
    }

    public string OcrItemName { get; }
    public decimal FreeQuantity { get; }
    public decimal DiscountPercent { get; }
    public decimal? LineAmountHint { get; }

    private int? _matchedMedicineId;
    public int? MatchedMedicineId
    {
        get => _matchedMedicineId;
        set
        {
            if (_matchedMedicineId == value) return;
            _matchedMedicineId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMatched));
            OnPropertyChanged(nameof(MatchStatus));
        }
    }

    private string? _matchedMedicineName;
    public string? MatchedMedicineName
    {
        get => _matchedMedicineName;
        set { if (_matchedMedicineName != value) { _matchedMedicineName = value; OnPropertyChanged(); } }
    }

    private string? _batchNumber;
    public string? BatchNumber
    {
        get => _batchNumber;
        set { if (_batchNumber != value) { _batchNumber = value; OnPropertyChanged(); } }
    }

    private DateTime? _expiryDate;
    private string _expiryMonth = string.Empty;
    private string _expiryYear = string.Empty;
    private bool _syncingExpiry;

    public DateTime? ExpiryDate
    {
        get => _expiryDate;
        set => SetExpiryDate(value);
    }

    public string ExpiryMonth
    {
        get => _expiryMonth;
        set
        {
            var next = PurchaseLineViewModel.SanitizeMonth(value, _expiryMonth);
            if (_expiryMonth == next) return;
            _expiryMonth = next;
            OnPropertyChanged();
            if (!_syncingExpiry)
                RebuildExpiryDateFromParts();
        }
    }

    public string ExpiryYear
    {
        get => _expiryYear;
        set
        {
            var next = PurchaseLineViewModel.SanitizeYear(value, _expiryYear);
            if (_expiryYear == next) return;
            _expiryYear = next;
            OnPropertyChanged();
            if (!_syncingExpiry)
                RebuildExpiryDateFromParts();
        }
    }

    private void SetExpiryDate(DateTime? value)
    {
        if (_expiryDate == value) return;
        _expiryDate = value;
        OnPropertyChanged(nameof(ExpiryDate));
        if (_syncingExpiry) return;
        _syncingExpiry = true;
        try
        {
            if (value is DateTime d)
            {
                _expiryMonth = d.Month.ToString("00");
                _expiryYear = d.Year.ToString("0000");
            }
            else
            {
                _expiryMonth = string.Empty;
                _expiryYear = string.Empty;
            }
            OnPropertyChanged(nameof(ExpiryMonth));
            OnPropertyChanged(nameof(ExpiryYear));
        }
        finally
        {
            _syncingExpiry = false;
        }
    }

    private void RebuildExpiryDateFromParts()
    {
        _syncingExpiry = true;
        try
        {
            if (int.TryParse(_expiryMonth, out var m) && m is >= 1 and <= 12
                && _expiryYear.Length == 4
                && int.TryParse(_expiryYear, out var y) && y is >= 2000 and <= 2100)
            {
                // Keep typed text as-is (do not pad "1" → "01" while editing).
                _expiryDate = new DateTime(y, m, DateTime.DaysInMonth(y, m));
            }
            else if (string.IsNullOrEmpty(_expiryMonth) && string.IsNullOrEmpty(_expiryYear))
            {
                _expiryDate = null;
            }
            OnPropertyChanged(nameof(ExpiryDate));
        }
        finally
        {
            _syncingExpiry = false;
        }
    }

    private decimal _quantity;
    public decimal Quantity
    {
        get => _quantity;
        set { if (_quantity != value) { _quantity = value; OnPropertyChanged(); } }
    }

    private decimal _purchasePrice;
    public decimal PurchasePrice
    {
        get => _purchasePrice;
        set { if (_purchasePrice != value) { _purchasePrice = value; OnPropertyChanged(); } }
    }

    private decimal _mrp;
    public decimal Mrp
    {
        get => _mrp;
        set { if (_mrp != value) { _mrp = value; OnPropertyChanged(); } }
    }

    private decimal _sellingPrice;
    public decimal SellingPrice
    {
        get => _sellingPrice;
        set { if (_sellingPrice != value) { _sellingPrice = value; OnPropertyChanged(); } }
    }

    private decimal _gstPercent;
    public decimal GstPercent
    {
        get => _gstPercent;
        set { if (_gstPercent != value) { _gstPercent = value; OnPropertyChanged(); } }
    }

    public bool IsMatched => MatchedMedicineId is > 0;
    public string MatchStatus => IsMatched ? "OK" : "Pick →";

    public ScannedPurchaseLineDto ToDto() => new()
    {
        OcrItemName = OcrItemName,
        MatchedMedicineId = MatchedMedicineId,
        MatchedMedicineName = MatchedMedicineName,
        BatchNumber = BatchNumber,
        ExpiryDate = ExpiryDate,
        Quantity = Quantity,
        FreeQuantity = FreeQuantity,
        PurchasePrice = PurchasePrice,
        Mrp = Mrp,
        SellingPrice = SellingPrice,
        GstPercent = GstPercent,
        DiscountPercent = DiscountPercent,
        LineAmountHint = LineAmountHint
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
