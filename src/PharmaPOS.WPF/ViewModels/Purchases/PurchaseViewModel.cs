using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Purchases;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Shared.Constants;
using PharmaPOS.Shared.Results;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Purchases;

/// <summary>
/// Drives the purchase / goods-receipt screen: supplier selection, medicine
/// popup entry, batch &amp; expiry entry, live tax-exclusive totals and receiving stock.
/// </summary>
public class PurchaseViewModel : ObservableObject
{
    private readonly IPurchaseService _purchaseService;
    private readonly IMedicinePickerService _medicinePicker;
    private readonly IPurchaseSearchService _purchaseSearch;
    private readonly ICurrentUserService _currentUser;
    private readonly IDialogService _dialog;

    private string _supplierSearchText = string.Empty;
    private SupplierLookupDto? _selectedSupplier;
    private int _supplierSuggestionIndex = -1;
    private bool _suppressSupplierSearch;
    private string? _supplierInvoiceNumber;
    private DateTime _invoiceDate = DateTime.Today;

    private PaymentMethod _paymentMethod = PaymentMethod.Cash;
    private decimal _paidAmount;
    private bool _isBusy;
    private string? _statusMessage;

    private PurchaseListItemDto? _selectedPurchase;
    private bool _suppressPurchaseSelection;
    private int? _lastDropdownPurchaseId;
    private int? _editingPurchaseId;
    private CancellationTokenSource? _purchaseLoadCts;
    private readonly SemaphoreSlim _purchaseGate = new(1, 1);

    public PurchaseViewModel(
        IPurchaseService purchaseService,
        IMedicinePickerService medicinePicker,
        IPurchaseSearchService purchaseSearch,
        ICurrentUserService currentUser,
        IDialogService dialog)
    {
        _purchaseService = purchaseService;
        _medicinePicker = medicinePicker;
        _purchaseSearch = purchaseSearch;
        _currentUser = currentUser;
        _dialog = dialog;

        CanCreate = currentUser.HasAnyPermission(
            AppConstants.Permissions.PurchaseCreate, AppConstants.Permissions.PurchaseManage);
        CanSearch = currentUser.HasAnyPermission(
            AppConstants.Permissions.PurchaseSearch, AppConstants.Permissions.PurchaseView,
            AppConstants.Permissions.PurchaseManage);

        Lines.CollectionChanged += (_, _) => RecalculateTotals();

        RemoveLineCommand = new RelayCommand(p => RemoveLine(p as PurchaseLineViewModel), _ => CanCreate);
        SaveCommand = new AsyncRelayCommand(_ => SaveAsync(), _ => CanCreate && CanSave());
        NewPurchaseCommand = new RelayCommand(_ => NewPurchase(), _ => CanCreate);
        ClearSupplierCommand = new RelayCommand(_ => ClearSupplier());
        SearchPurchasesCommand = new AsyncRelayCommand(_ => OpenPurchaseSearchAsync(), _ => CanSearch && !IsBusy);

        EnsureTrailingEmptyRow();
        _ = InitializePurchasesAsync();
    }

    public bool SuppressPurchaseLoad
    {
        get => _suppressPurchaseSelection;
        set => _suppressPurchaseSelection = value;
    }

    public event Action<PurchaseLineViewModel?>? RequestItemFocus;

    public ObservableCollection<SupplierLookupDto> SupplierResults { get; } = new();
    public ObservableCollection<PurchaseLineViewModel> Lines { get; } = new();
    public ObservableCollection<PurchaseListItemDto> PurchaseHistory { get; } = new();

    public Array PaymentMethods => Enum.GetValues(typeof(PaymentMethod));

    public ICommand RemoveLineCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand NewPurchaseCommand { get; }
    public ICommand ClearSupplierCommand { get; }
    public ICommand SearchPurchasesCommand { get; }

    public bool CanCreate { get; }
    public bool CanSearch { get; }

    public bool IsEditing => _editingPurchaseId.HasValue;

    #region Supplier

    public string SupplierSearchText
    {
        get => _supplierSearchText;
        set
        {
            if (SetProperty(ref _supplierSearchText, value) && !_suppressSupplierSearch)
                _ = SearchSuppliersAsync(value);
        }
    }

    public int SupplierSuggestionIndex
    {
        get => _supplierSuggestionIndex;
        set => SetProperty(ref _supplierSuggestionIndex, value);
    }

    public bool ShowSupplierResults => SupplierResults.Count > 0;

    public SupplierLookupDto? SelectedSupplier
    {
        get => _selectedSupplier;
        set
        {
            if (!SetProperty(ref _selectedSupplier, value)) return;
            OnPropertyChanged(nameof(SupplierDisplay));
            if (value is not null)
            {
                _suppressSupplierSearch = true;
                SupplierSearchText = value.Name;
                _suppressSupplierSearch = false;
                SupplierResults.Clear();
                SupplierSuggestionIndex = -1;
                OnPropertyChanged(nameof(ShowSupplierResults));
            }
            CommandManager.InvalidateRequerySuggested();
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

    public void MoveSupplierSelection(int delta)
    {
        if (SupplierResults.Count == 0)
        {
            SupplierSuggestionIndex = -1;
            return;
        }

        if (SupplierSuggestionIndex < 0)
            SupplierSuggestionIndex = 0;
        else
            SupplierSuggestionIndex = Math.Clamp(SupplierSuggestionIndex + delta, 0, SupplierResults.Count - 1);
    }

    public void ConfirmSupplierSelection()
    {
        if (SupplierSuggestionIndex >= 0 && SupplierSuggestionIndex < SupplierResults.Count)
            SelectedSupplier = SupplierResults[SupplierSuggestionIndex];
    }

    public void DismissSupplierSuggestions()
    {
        SupplierResults.Clear();
        SupplierSuggestionIndex = -1;
        OnPropertyChanged(nameof(ShowSupplierResults));
    }

    private async Task SearchSuppliersAsync(string term)
    {
        SupplierResults.Clear();
        SupplierSuggestionIndex = -1;
        OnPropertyChanged(nameof(ShowSupplierResults));

        if (string.IsNullOrWhiteSpace(term)) return;
        try
        {
            var results = await _purchaseService.SearchSuppliersAsync(term);
            foreach (var r in results) SupplierResults.Add(r);
            SupplierSuggestionIndex = results.Count > 0 ? 0 : -1;
            OnPropertyChanged(nameof(ShowSupplierResults));
        }
        catch { /* best-effort */ }
    }

    private void ClearSupplier()
    {
        SelectedSupplier = null;
        SupplierSearchText = string.Empty;
        SupplierResults.Clear();
        SupplierSuggestionIndex = -1;
        OnPropertyChanged(nameof(ShowSupplierResults));
    }

    #endregion

    #region Purchase history

    public PurchaseListItemDto? SelectedPurchase
    {
        get => _selectedPurchase;
        set => SetProperty(ref _selectedPurchase, value);
    }

    private async Task InitializePurchasesAsync()
    {
        await RefreshPurchaseHistoryAsync(selectNewPurchase: true);
    }

    private async Task RefreshPurchaseHistoryAsync(bool selectNewPurchase = false, int? selectPurchaseId = null)
    {
        await _purchaseGate.WaitAsync();
        try
        {
            await RefreshPurchaseHistoryCoreAsync(selectNewPurchase, selectPurchaseId);
        }
        finally
        {
            _purchaseGate.Release();
        }
    }

    private async Task RefreshPurchaseHistoryCoreAsync(bool selectNewPurchase = false, int? selectPurchaseId = null)
    {
        try
        {
            var branchId = _currentUser.CurrentUser?.BranchId;
            var purchases = await _purchaseService.ListPurchasesAsync(branchId);
            var preview = await _purchaseService.PreviewNextPurchaseNumberAsync(branchId);
            var newPurchase = new PurchaseListItemDto(0, preview, DateTime.Now, "New purchase");

            _suppressPurchaseSelection = true;
            PurchaseHistory.Clear();
            PurchaseHistory.Add(newPurchase);
            foreach (var purchase in purchases)
                PurchaseHistory.Add(purchase);

            if (selectPurchaseId is int purchaseId)
                SelectedPurchase = PurchaseHistory.FirstOrDefault(p => p.PurchaseId == purchaseId) ?? newPurchase;
            else if (selectNewPurchase)
                SelectedPurchase = newPurchase;
            else if (_editingPurchaseId is int viewingId)
                SelectedPurchase = PurchaseHistory.FirstOrDefault(p => p.PurchaseId == viewingId) ?? newPurchase;

            _suppressPurchaseSelection = false;
        }
        catch (Exception ex)
        {
            _dialog.ShowError($"Could not load purchase history: {ex.Message}");
        }
    }

    private async Task OpenPurchaseSearchAsync()
    {
        var purchase = await _purchaseSearch.PickPurchaseAsync();
        if (purchase is null) return;

        SelectedPurchase = PurchaseHistory.FirstOrDefault(p => p.PurchaseId == purchase.PurchaseId) ?? purchase;
        await LoadPurchaseFromDropdownAsync(purchase, focusGridAfterLoad: true);
    }

    public async Task LoadPurchaseFromDropdownAsync(PurchaseListItemDto purchase, bool focusGridAfterLoad = false)
    {
        if (_suppressPurchaseSelection) return;

        if (purchase.PurchaseId == 0)
        {
            _lastDropdownPurchaseId = 0;
            if (_editingPurchaseId.HasValue)
                ResetPurchaseForm(clearStatus: true);
            if (focusGridAfterLoad)
                RequestItemFocus?.Invoke(Lines.FirstOrDefault(l => l.IsEmpty));
            return;
        }

        if (_lastDropdownPurchaseId == purchase.PurchaseId)
        {
            if (focusGridAfterLoad)
                RequestItemFocus?.Invoke(Lines.FirstOrDefault(l => !l.IsEmpty) ?? Lines.FirstOrDefault(l => l.IsEmpty));
            return;
        }

        _purchaseLoadCts?.Cancel();
        _purchaseLoadCts?.Dispose();
        _purchaseLoadCts = new CancellationTokenSource();
        var token = _purchaseLoadCts.Token;

        IsBusy = true;
        try
        {
            await _purchaseGate.WaitAsync(token);
            try
            {
                if (token.IsCancellationRequested) return;

                var result = await _purchaseService.GetPurchaseForLoadAsync(
                    purchase.PurchaseId, _currentUser.CurrentUser?.BranchId);
                if (token.IsCancellationRequested) return;

                if (result.IsFailure || result.Value is null)
                {
                    _dialog.ShowError(result.Error ?? "Could not load the purchase invoice.");
                    return;
                }

                _lastDropdownPurchaseId = purchase.PurchaseId;
                _selectedPurchase = purchase;
                OnPropertyChanged(nameof(SelectedPurchase));
                LoadPurchase(result.Value);
                StatusMessage = $"Editing purchase {result.Value.InvoiceNumber}.";
                if (focusGridAfterLoad)
                    RequestItemFocus?.Invoke(Lines.FirstOrDefault(l => !l.IsEmpty) ?? Lines.FirstOrDefault(l => l.IsEmpty));
            }
            finally
            {
                _purchaseGate.Release();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _dialog.ShowError(ex.Message);
        }
        finally
        {
            if (!token.IsCancellationRequested)
                IsBusy = false;
            else if (_purchaseLoadCts?.Token == token)
                IsBusy = false;
        }
    }

    private void LoadPurchase(PurchaseLoadDto purchase)
    {
        foreach (var line in Lines)
            line.Changed -= RecalculateTotals;
        Lines.Clear();

        _editingPurchaseId = purchase.PurchaseId;
        OnPropertyChanged(nameof(IsEditing));

        SelectedSupplier = new SupplierLookupDto(
            purchase.SupplierId,
            purchase.SupplierName,
            purchase.SupplierPhone,
            null,
            0m);
        SupplierInvoiceNumber = purchase.SupplierInvoiceNumber;
        InvoiceDate = purchase.InvoiceDate;
        PaymentMethod = purchase.PaymentMethod;
        PaidAmount = purchase.PaidAmount;

        foreach (var line in purchase.Lines)
        {
            var vm = PurchaseLineViewModel.CreateEmpty();
            vm.LoadFrom(line);
            vm.Changed += RecalculateTotals;
            Lines.Add(vm);
        }

        EnsureTrailingEmptyRow();
        RecalculateTotals();
        CommandManager.InvalidateRequerySuggested();
    }

    #endregion

    #region Grid item picker

    public async Task BeginItemSelectionAsync(PurchaseLineViewModel line)
    {
        var lookup = await _medicinePicker.PickMedicineLookupAsync();
        if (lookup is null) return;

        var medicine = await _purchaseService.GetMedicineAsync(lookup.Id);
        if (medicine is null)
        {
            _dialog.ShowError("Could not load medicine details.");
            return;
        }

        line.ApplyMedicine(medicine);
        EnsureTrailingEmptyRow();
        RecalculateTotals();
        RequestItemFocus?.Invoke(line);
    }

    private void EnsureTrailingEmptyRow()
    {
        if (Lines.Count == 0 || !Lines[^1].IsEmpty)
        {
            var empty = PurchaseLineViewModel.CreateEmpty();
            empty.Changed += RecalculateTotals;
            Lines.Add(empty);
        }
    }

    private void RemoveLine(PurchaseLineViewModel? line)
    {
        if (line is null || line.IsEmpty) return;

        line.Changed -= RecalculateTotals;
        Lines.Remove(line);
        EnsureTrailingEmptyRow();
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
    public int ItemCount => Lines.Count(l => !l.IsEmpty);

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
        var active = Lines.Where(l => !l.IsEmpty).ToList();
        SubTotal = active.Sum(l => l.Gross);
        DiscountTotal = active.Sum(l => l.DiscountAmount);
        TaxableTotal = active.Sum(l => l.Taxable);
        var tax = active.Sum(l => l.TaxAmount);
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

    private bool CanSave() =>
        SelectedSupplier is not null &&
        Lines.Any(l => !l.IsEmpty) &&
        !IsBusy;

    private async Task SaveAsync()
    {
        if (SelectedSupplier is null)
        {
            _dialog.ShowError("Select a supplier before saving.");
            return;
        }

        var activeLines = Lines.Where(l => !l.IsEmpty).ToList();
        if (activeLines.Count == 0) return;

        var invalid = activeLines.FirstOrDefault(l => string.IsNullOrWhiteSpace(l.BatchNumber) || l.Quantity <= 0);
        if (invalid is not null)
        {
            _dialog.ShowError($"'{invalid.MedicineName}' needs a batch number and a quantity greater than zero.");
            return;
        }

        var lineRequests = activeLines.Select(l => new PurchaseLineRequest
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
        }).ToList();

        IsBusy = true;
        try
        {
            await _purchaseGate.WaitAsync();
            try
            {
                Result<PurchaseReceiptDto> result;
                if (_editingPurchaseId is int purchaseId)
                {
                    result = await _purchaseService.UpdatePurchaseAsync(new UpdatePurchaseRequest
                    {
                        PurchaseId = purchaseId,
                        SupplierId = SelectedSupplier.Id,
                        SupplierInvoiceNumber = SupplierInvoiceNumber,
                        InvoiceDate = InvoiceDate,
                        PaymentMethod = PaymentMethod,
                        PaidAmount = PaidAmount,
                        Lines = lineRequests
                    }, _currentUser.CurrentUser?.BranchId);
                }
                else
                {
                    result = await _purchaseService.CreatePurchaseAsync(new CreatePurchaseRequest
                    {
                        SupplierId = SelectedSupplier.Id,
                        SupplierInvoiceNumber = SupplierInvoiceNumber,
                        InvoiceDate = InvoiceDate,
                        PaymentMethod = PaymentMethod,
                        PaidAmount = PaidAmount,
                        Lines = lineRequests
                    }, _currentUser.CurrentUser?.BranchId);
                }

                if (result.IsFailure || result.Value is null)
                {
                    _dialog.ShowError(result.Error ?? "Could not save the purchase.");
                    return;
                }

                var r = result.Value;
                var savedId = r.PurchaseId;
                var wasEditing = _editingPurchaseId.HasValue;

                StatusMessage = wasEditing
                    ? $"Updated purchase {r.InvoiceNumber}. {r.ItemCount} item(s)."
                    : $"Saved purchase {r.InvoiceNumber}. {r.ItemCount} item(s), stock received.";

                _dialog.ShowInfo(
                    $"Purchase {r.InvoiceNumber} saved.\n\n" +
                    $"Items: {r.ItemCount}\nGrand total: ₹{r.GrandTotal:N2}\nBalance due: ₹{r.BalanceDue:N2}",
                    "Purchase saved");

                await RefreshPurchaseHistoryCoreAsync(
                    selectNewPurchase: !wasEditing,
                    selectPurchaseId: wasEditing ? savedId : null);
            }
            finally
            {
                _purchaseGate.Release();
            }

            if (_editingPurchaseId is int editingId)
            {
                _lastDropdownPurchaseId = null;
                var bill = PurchaseHistory.FirstOrDefault(p => p.PurchaseId == editingId);
                if (bill is not null)
                    await LoadPurchaseFromDropdownAsync(bill, focusGridAfterLoad: false);
            }
            else
            {
                ResetPurchaseForm(clearStatus: false);
            }

            RequestItemFocus?.Invoke(Lines.FirstOrDefault(l => l.IsEmpty));
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
        _suppressPurchaseSelection = true;
        SelectedPurchase = PurchaseHistory.FirstOrDefault(p => p.PurchaseId == 0);
        _suppressPurchaseSelection = false;
        ResetPurchaseForm(clearStatus: true);
    }

    private void ResetPurchaseForm(bool clearStatus)
    {
        foreach (var line in Lines)
            line.Changed -= RecalculateTotals;
        Lines.Clear();
        EnsureTrailingEmptyRow();

        ClearSupplier();
        SupplierInvoiceNumber = null;
        InvoiceDate = DateTime.Today;
        PaymentMethod = PaymentMethod.Cash;
        PaidAmount = 0;
        _editingPurchaseId = null;
        _lastDropdownPurchaseId = 0;
        OnPropertyChanged(nameof(IsEditing));

        RecalculateTotals();
        if (clearStatus)
            StatusMessage = null;

        CommandManager.InvalidateRequerySuggested();
        RequestItemFocus?.Invoke(Lines.FirstOrDefault(l => l.IsEmpty));
    }
}
