using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using PharmaPOS.Application.Features.Purchases;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.Views;

public partial class PurchaseBillScanWindow : Window
{
    private readonly ObservableCollection<ScannedLineRow> _lines = new();
    private readonly ScannedPurchaseDraftDto _draft;
    private readonly IMedicinePickerService _medicinePicker;

    public ScannedPurchaseDraftDto? AcceptedDraft { get; private set; }

    public PurchaseBillScanWindow(
        ScannedPurchaseDraftDto draft,
        string imagePath,
        IPurchaseService purchases,
        IMedicinePickerService medicinePicker)
    {
        InitializeComponent();
        _draft = draft;
        _medicinePicker = medicinePicker;
        _ = purchases;

        SupplierBox.Text = draft.SupplierName ?? string.Empty;
        InvoiceNoBox.Text = draft.SupplierInvoiceNumber ?? string.Empty;
        InvoiceDatePicker.SelectedDate = draft.InvoiceDate ?? DateTime.Today;

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
        WarningText.Text = string.Join(" ", hints);

        foreach (var line in draft.Lines)
            _lines.Add(new ScannedLineRow(line));
        LinesGrid.ItemsSource = _lines;

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
            // Image preview is optional.
        }
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

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
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

        AcceptedDraft = new ScannedPurchaseDraftDto
        {
            RawText = _draft.RawText,
            SupplierName = SupplierBox.Text.Trim(),
            MatchedSupplierId = _draft.MatchedSupplierId,
            MatchedSupplierPhone = _draft.MatchedSupplierPhone,
            SupplierInvoiceNumber = string.IsNullOrWhiteSpace(InvoiceNoBox.Text) ? null : InvoiceNoBox.Text.Trim(),
            InvoiceDate = InvoiceDatePicker.SelectedDate ?? DateTime.Today,
            GrandTotalHint = _draft.GrandTotalHint,
            Lines = matched,
            Warnings = _draft.Warnings
        };
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
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
        ExpiryDate = source.ExpiryDate;
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
    public DateTime? ExpiryDate
    {
        get => _expiryDate;
        set { if (_expiryDate != value) { _expiryDate = value; OnPropertyChanged(); } }
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
