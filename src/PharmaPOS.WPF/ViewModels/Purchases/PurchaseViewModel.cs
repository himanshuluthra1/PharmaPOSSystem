using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Purchases;
using PharmaPOS.Domain.Enums;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Purchases;

/// <summary>
/// Drives the purchase / goods-receipt screen: supplier selection, medicine
/// search, batch &amp; expiry entry, live tax-exclusive totals and receiving the
/// stock (which creates/updates batches and posts to the supplier ledger).
/// </summary>
public class PurchaseViewModel : ObservableObject
{
    private readonly IPurchaseService _purchaseService;
    private readonly ICurrentUserService _currentUser;
    private readonly IDialogService _dialog;

    private string _supplierSearchText = string.Empty;
    private SupplierLookupDto? _selectedSupplier;
    private string? _supplierInvoiceNumber;
    private DateTime _invoiceDate = DateTime.Today;

    private string _medicineSearchText = string.Empty;
    private PurchaseMedicineDto? _selectedMedicine;
    private string? _searchHint;
    private bool _hasMedicineResults;
    private CancellationTokenSource? _searchCts;

    private PaymentMethod _paymentMethod = PaymentMethod.Cash;
    private decimal _paidAmount;
    private bool _isBusy;
    private string? _statusMessage;

    public PurchaseViewModel(
        IPurchaseService purchaseService,
        ICurrentUserService currentUser,
        IDialogService dialog)
    {
        _purchaseService = purchaseService;
        _currentUser = currentUser;
        _dialog = dialog;

        Lines.CollectionChanged += (_, _) => RecalculateTotals();

        AddLineCommand = new RelayCommand(_ => AddLine(), _ => SelectedMedicine is not null);
        RemoveLineCommand = new RelayCommand(p => RemoveLine(p as PurchaseLineViewModel));
        SaveCommand = new AsyncRelayCommand(_ => SaveAsync(), _ => CanSave());
        NewPurchaseCommand = new RelayCommand(_ => NewPurchase());
        ClearSupplierCommand = new RelayCommand(_ => SelectedSupplier = null);
    }

    public ObservableCollection<PurchaseMedicineDto> MedicineResults { get; } = new();
    public ObservableCollection<SupplierLookupDto> SupplierResults { get; } = new();
    public ObservableCollection<PurchaseLineViewModel> Lines { get; } = new();

    public Array PaymentMethods => Enum.GetValues(typeof(PaymentMethod));

    public ICommand AddLineCommand { get; }
    public ICommand RemoveLineCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand NewPurchaseCommand { get; }
    public ICommand ClearSupplierCommand { get; }

    #region Supplier

    public string SupplierSearchText
    {
        get => _supplierSearchText;
        set
        {
            if (SetProperty(ref _supplierSearchText, value))
                _ = SearchSuppliersAsync(value);
        }
    }

    public SupplierLookupDto? SelectedSupplier
    {
        get => _selectedSupplier;
        set
        {
            if (SetProperty(ref _selectedSupplier, value))
            {
                OnPropertyChanged(nameof(SupplierDisplay));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string SupplierDisplay => SelectedSupplier?.Name ?? "No supplier selected";

    public string? SupplierInvoiceNumber
    {
        get => _supplierInvoiceNumber;
        set => SetProperty(ref _supplierInvoiceNumber, value);
    }

    public DateTime InvoiceDate
    {
        get => _invoiceDate;
        set => SetProperty(ref _invoiceDate, value);
    }

    private async Task SearchSuppliersAsync(string term)
    {
        SupplierResults.Clear();
        if (string.IsNullOrWhiteSpace(term)) return;
        try
        {
            var results = await _purchaseService.SearchSuppliersAsync(term);
            foreach (var r in results) SupplierResults.Add(r);
        }
        catch { /* best-effort */ }
    }

    #endregion

    #region Medicine search / line entry

    public string MedicineSearchText
    {
        get => _medicineSearchText;
        set
        {
            if (SetProperty(ref _medicineSearchText, value))
                _ = SearchMedicinesAsync(value);
        }
    }

    public bool HasMedicineResults
    {
        get => _hasMedicineResults;
        private set => SetProperty(ref _hasMedicineResults, value);
    }

    public string? SearchHint
    {
        get => _searchHint;
        private set => SetProperty(ref _searchHint, value);
    }

    public PurchaseMedicineDto? SelectedMedicine
    {
        get => _selectedMedicine;
        set
        {
            if (SetProperty(ref _selectedMedicine, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task SearchMedicinesAsync(string term)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        MedicineResults.Clear();
        HasMedicineResults = false;
        SelectedMedicine = null;

        term = term.Trim();
        if (term.Length < 2)
        {
            SearchHint = term.Length == 1 ? "Type at least 2 characters to search..." : null;
            return;
        }

        SearchHint = "Searching...";
        try
        {
            await Task.Delay(300, token);
            var results = await _purchaseService.SearchMedicinesAsync(term, token);
            if (token.IsCancellationRequested) return;

            foreach (var r in results) MedicineResults.Add(r);
            HasMedicineResults = results.Count > 0;
            SearchHint = results.Count == 0 ? $"No medicines found for \"{term}\"." : null;
        }
        catch (OperationCanceledException) { /* superseded */ }
        catch (Exception ex)
        {
            SearchHint = $"Search failed: {ex.Message}";
        }
    }

    private void AddLine()
    {
        if (SelectedMedicine is null) return;

        var line = new PurchaseLineViewModel
        {
            MedicineId = SelectedMedicine.Id,
            MedicineName = SelectedMedicine.Name,
            GenericName = SelectedMedicine.GenericName,
            PurchasePrice = SelectedMedicine.PurchasePrice,
            Mrp = SelectedMedicine.Mrp,
            SellingPrice = SelectedMedicine.SellingPrice > 0 ? SelectedMedicine.SellingPrice : SelectedMedicine.Mrp,
            GstPercent = SelectedMedicine.GstPercent,
            ExpiryDate = DateTime.Today.AddYears(2),
            Quantity = 1
        };
        line.Changed += RecalculateTotals;
        Lines.Add(line);

        RecalculateTotals();
        ResetEntry();
    }

    private void ResetEntry()
    {
        _searchCts?.Cancel();
        _medicineSearchText = string.Empty;
        OnPropertyChanged(nameof(MedicineSearchText));
        MedicineResults.Clear();
        HasMedicineResults = false;
        SearchHint = null;
        SelectedMedicine = null;
    }

    private void RemoveLine(PurchaseLineViewModel? line)
    {
        if (line is not null)
        {
            line.Changed -= RecalculateTotals;
            Lines.Remove(line);
        }
        RecalculateTotals();
    }

    #endregion

    #region Totals & payment

    private decimal _subTotal, _discountTotal, _taxableTotal, _cgst, _sgst, _roundOff, _grandTotal;

    public decimal SubTotal { get => _subTotal; private set => SetProperty(ref _subTotal, value); }
    public decimal DiscountTotal { get => _discountTotal; private set => SetProperty(ref _discountTotal, value); }
    public decimal TaxableTotal { get => _taxableTotal; private set => SetProperty(ref _taxableTotal, value); }
    public decimal Cgst { get => _cgst; private set => SetProperty(ref _cgst, value); }
    public decimal Sgst { get => _sgst; private set => SetProperty(ref _sgst, value); }
    public decimal RoundOff { get => _roundOff; private set => SetProperty(ref _roundOff, value); }
    public decimal GrandTotal { get => _grandTotal; private set => SetProperty(ref _grandTotal, value); }

    public decimal BalanceDue => GrandTotal > PaidAmount ? GrandTotal - PaidAmount : 0m;
    public int ItemCount => Lines.Count;

    public PaymentMethod PaymentMethod
    {
        get => _paymentMethod;
        set => SetProperty(ref _paymentMethod, value);
    }

    public decimal PaidAmount
    {
        get => _paidAmount;
        set
        {
            if (SetProperty(ref _paidAmount, value))
                OnPropertyChanged(nameof(BalanceDue));
        }
    }

    private void RecalculateTotals()
    {
        SubTotal = Lines.Sum(l => l.Gross);
        DiscountTotal = Lines.Sum(l => l.DiscountAmount);
        TaxableTotal = Lines.Sum(l => l.Taxable);
        var tax = Lines.Sum(l => l.TaxAmount);
        Cgst = Math.Round(tax / 2m, 2);
        Sgst = tax - Cgst;

        var net = TaxableTotal + tax;
        var rounded = Math.Round(net, 0, MidpointRounding.AwayFromZero);
        RoundOff = rounded - net;
        GrandTotal = rounded;

        OnPropertyChanged(nameof(BalanceDue));
        OnPropertyChanged(nameof(ItemCount));
        CommandManager.InvalidateRequerySuggested();
    }

    #endregion

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    private bool CanSave() => SelectedSupplier is not null && Lines.Count > 0 && !IsBusy;

    private async Task SaveAsync()
    {
        if (SelectedSupplier is null)
        {
            _dialog.ShowError("Select a supplier before saving.");
            return;
        }
        if (Lines.Count == 0) return;

        var invalid = Lines.FirstOrDefault(l => string.IsNullOrWhiteSpace(l.BatchNumber) || l.Quantity <= 0);
        if (invalid is not null)
        {
            _dialog.ShowError($"'{invalid.MedicineName}' needs a batch number and a quantity greater than zero.");
            return;
        }

        var request = new CreatePurchaseRequest
        {
            SupplierId = SelectedSupplier.Id,
            SupplierInvoiceNumber = SupplierInvoiceNumber,
            InvoiceDate = InvoiceDate,
            PaymentMethod = PaymentMethod,
            PaidAmount = PaidAmount,
            Lines = Lines.Select(l => new PurchaseLineRequest
            {
                MedicineId = l.MedicineId,
                BatchNumber = l.BatchNumber.Trim(),
                ManufacturingDate = l.ManufacturingDate,
                ExpiryDate = l.ExpiryDate,
                Quantity = l.Quantity,
                FreeQuantity = l.FreeQuantity,
                PurchasePrice = l.PurchasePrice,
                Mrp = l.Mrp,
                SellingPrice = l.SellingPrice,
                DiscountPercent = l.DiscountPercent,
                GstPercent = l.GstPercent
            }).ToList()
        };

        IsBusy = true;
        try
        {
            var result = await _purchaseService.CreatePurchaseAsync(request, _currentUser.CurrentUser?.BranchId);
            if (result.IsFailure || result.Value is null)
            {
                _dialog.ShowError(result.Error ?? "Could not save the purchase.");
                return;
            }

            var r = result.Value;
            StatusMessage = $"Saved purchase {r.InvoiceNumber}. {r.ItemCount} item(s), stock received. Balance due: ₹{r.BalanceDue:N2}";
            _dialog.ShowInfo(
                $"Purchase {r.InvoiceNumber} saved and stock received.\n\n" +
                $"Items: {r.ItemCount}\nGrand total: ₹{r.GrandTotal:N2}\nBalance due: ₹{r.BalanceDue:N2}",
                "Purchase saved");

            NewPurchase();
        }
        catch (Exception ex)
        {
            _dialog.ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void NewPurchase()
    {
        foreach (var line in Lines) line.Changed -= RecalculateTotals;
        Lines.Clear();
        ResetEntry();
        SelectedSupplier = null;
        SupplierSearchText = string.Empty;
        SupplierResults.Clear();
        SupplierInvoiceNumber = null;
        InvoiceDate = DateTime.Today;
        PaymentMethod = PaymentMethod.Cash;
        PaidAmount = 0;
        RecalculateTotals();
    }
}
