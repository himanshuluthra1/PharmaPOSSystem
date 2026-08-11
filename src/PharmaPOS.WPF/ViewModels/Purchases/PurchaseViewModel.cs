using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Purchases;
using PharmaPOS.Application.Features.Settings;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Shared.Constants;
using PharmaPOS.Shared.Results;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;
using PharmaPOS.WPF.Views;
using WpfApp = System.Windows.Application;

namespace PharmaPOS.WPF.ViewModels.Purchases;

/// <summary>
/// Drives the purchase / goods-receipt screen: supplier selection, medicine
/// popup entry, batch &amp; expiry entry, live tax-exclusive totals and receiving stock.
/// </summary>
public class PurchaseViewModel : ObservableObject
{
    private readonly IPurchaseService _purchaseService;
    private readonly ISettingsService _settings;
    private readonly IMedicinePickerService _medicinePicker;
    private readonly IPurchaseSearchService _purchaseSearch;
    private readonly ICurrentUserService _currentUser;
    private readonly IDialogService _dialog;
    private readonly IBarcodeCameraService _barcodeCamera;
    private readonly IPurchaseBillScanService _billScan;
    private readonly IPurchaseOrderReceiveBridge _poReceiveBridge;

    private string _supplierSearchText = string.Empty;
    private SupplierLookupDto? _selectedSupplier;
    private int _supplierSuggestionIndex = -1;
    private bool _suppressSupplierSearch;
    private string? _supplierInvoiceNumber;
    private DateTime _invoiceDate = DateTime.Today;

    private PaymentMethod _paymentMethod = PaymentMethod.Cash;
    private decimal _paidAmount;
    private decimal _headerGrandTotal;
    private bool _isBusy;
    private string? _statusMessage;
    private bool _allowEditPurchaseBills;

    private PurchaseListItemDto? _selectedPurchase;
    private bool _suppressPurchaseSelection;
    private int? _lastDropdownPurchaseId;
    private int? _editingPurchaseId;
    private int? _linkedPurchaseOrderId;
    private string? _linkedPurchaseOrderNumber;
    private CancellationTokenSource? _purchaseLoadCts;
    private readonly SemaphoreSlim _purchaseGate = new(1, 1);

    public PurchaseViewModel(
        IPurchaseService purchaseService,
        ISettingsService settings,
        IMedicinePickerService medicinePicker,
        IPurchaseSearchService purchaseSearch,
        ICurrentUserService currentUser,
        IDialogService dialog,
        IBarcodeCameraService barcodeCamera,
        IPurchaseBillScanService billScan,
        IPurchaseOrderReceiveBridge poReceiveBridge)
    {
        _purchaseService = purchaseService;
        _settings = settings;
        _medicinePicker = medicinePicker;
        _purchaseSearch = purchaseSearch;
        _currentUser = currentUser;
        _dialog = dialog;
        _barcodeCamera = barcodeCamera;
        _billScan = billScan;
        _poReceiveBridge = poReceiveBridge;

        CanCreate = currentUser.HasAnyPermission(
            AppConstants.Permissions.PurchaseCreate, AppConstants.Permissions.PurchaseManage);
        CanSearch = currentUser.HasAnyPermission(
            AppConstants.Permissions.PurchaseSearch, AppConstants.Permissions.PurchaseView,
            AppConstants.Permissions.PurchaseManage);

        Lines.CollectionChanged += (_, _) => RecalculateTotals();

        RemoveLineCommand = new RelayCommand(p => RemoveLine(p as PurchaseLineViewModel), _ => CanModifyBill);
        SaveCommand = new AsyncRelayCommand(_ => SaveAsync(), _ => CanCreate && CanSave());
        NewPurchaseCommand = new RelayCommand(_ => NewPurchase(), _ => CanCreate);
        ClearSupplierCommand = new RelayCommand(_ => ClearSupplier(), _ => CanModifyBill);
        SearchPurchasesCommand = new AsyncRelayCommand(_ => OpenPurchaseSearchAsync(), _ => CanSearch && !IsBusy);
        ScanBarcodeCameraCommand = new AsyncRelayCommand(_ => ScanBarcodeCameraAsync(), _ => CanModifyBill && !IsBusy);
        ScanPurchaseBillCommand = new AsyncRelayCommand(_ => ScanPurchaseBillAsync(), _ => CanModifyBill && !IsBusy);

        EnsureTrailingEmptyRow();
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await RefreshEditPolicyAsync();
        await InitializePurchasesAsync();
        await TryApplyPendingPurchaseOrderReceiveAsync();
    }

    private async Task RefreshEditPolicyAsync()
    {
        try
        {
            var prefs = await _settings.GetPreferencesAsync();
            AllowEditPurchaseBills = prefs.AllowEditPurchaseBills;
        }
        catch
        {
            AllowEditPurchaseBills = false;
        }
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
    public ICommand ScanBarcodeCameraCommand { get; }
    public ICommand ScanPurchaseBillCommand { get; }

    public bool CanCreate { get; }
    public bool CanSearch { get; }

    public bool AllowEditPurchaseBills
    {
        get => _allowEditPurchaseBills;
        private set
        {
            if (SetProperty(ref _allowEditPurchaseBills, value))
            {
                OnPropertyChanged(nameof(CanModifyBill));
                OnPropertyChanged(nameof(IsBillReadOnly));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool CanModifyBill => CanCreate && (!IsEditing || AllowEditPurchaseBills) && !IsBusy;

    public bool IsBillReadOnly => IsEditing && !AllowEditPurchaseBills;

    public bool IsEditing => _editingPurchaseId.HasValue;

    public int? LinkedPurchaseOrderId
    {
        get => _linkedPurchaseOrderId;
        private set
        {
            if (SetProperty(ref _linkedPurchaseOrderId, value))
                OnPropertyChanged(nameof(HasLinkedPurchaseOrder));
        }
    }

    public string? LinkedPurchaseOrderNumber
    {
        get => _linkedPurchaseOrderNumber;
        private set => SetProperty(ref _linkedPurchaseOrderNumber, value);
    }

    public bool HasLinkedPurchaseOrder => LinkedPurchaseOrderId is > 0;

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
            await RefreshEditPolicyAsync();
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
                StatusMessage = AllowEditPurchaseBills
                    ? $"Editing purchase {result.Value.InvoiceNumber}. Save to update."
                    : $"Viewing purchase {result.Value.InvoiceNumber} (edit is off in Settings → Preferences).";
                if (focusGridAfterLoad && !IsBillReadOnly)
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
        OnPropertyChanged(nameof(CanModifyBill));
        OnPropertyChanged(nameof(IsBillReadOnly));
        CommandManager.InvalidateRequerySuggested();

        _headerGrandTotal = purchase.GrandTotal;
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
        if (!CanModifyBill) return;

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

    public async Task TryAddByBarcodeAsync(string barcode)
    {
        if (!CanModifyBill || IsBusy) return;
        barcode = barcode.Trim();
        if (barcode.Length < 3) return;

        IsBusy = true;
        try
        {
            var medicine = await _purchaseService.FindMedicineByBarcodeAsync(barcode);
            if (medicine is null)
            {
                _dialog.ShowError($"No medicine found for barcode \"{barcode}\".");
                return;
            }

            var target = Lines.FirstOrDefault(l => l.IsEmpty) ?? PurchaseLineViewModel.CreateEmpty();
            if (!Lines.Contains(target))
            {
                target.Changed += RecalculateTotals;
                Lines.Add(target);
            }

            target.ApplyMedicine(medicine);
            EnsureTrailingEmptyRow();
            RecalculateTotals();
            RequestItemFocus?.Invoke(target);
            StatusMessage = $"Added {medicine.Name}";
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

    private Task ScanBarcodeCameraAsync()
    {
        var value = _barcodeCamera.ScanWithCamera("Scan medicine barcode");
        if (!string.IsNullOrWhiteSpace(value))
            return TryAddByBarcodeAsync(value);
        return Task.CompletedTask;
    }

    private async Task ScanPurchaseBillAsync()
    {
        if (!CanModifyBill) return;

        IsBusy = true;
        try
        {
            var draft = await _billScan.ScanAndReviewAsync(_currentUser.CurrentUser?.BranchId);
            if (draft is null) return;
            await ApplyScannedDraftAsync(draft);
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

    private async Task ApplyScannedDraftAsync(ScannedPurchaseDraftDto draft)
    {
        NewPurchase();

        SupplierLookupDto? supplier = null;
        if (draft.MatchedSupplierId is int sid)
        {
            var found = await _purchaseService.SearchSuppliersAsync(draft.SupplierName ?? string.Empty);
            supplier = found.FirstOrDefault(s => s.Id == sid) ?? found.FirstOrDefault();
        }
        if (supplier is null && !string.IsNullOrWhiteSpace(draft.SupplierName))
        {
            var found = await _purchaseService.SearchSuppliersAsync(draft.SupplierName);
            supplier = found.FirstOrDefault();
        }

        if (supplier is not null)
            SelectedSupplier = supplier;
        else if (!string.IsNullOrWhiteSpace(draft.SupplierName))
        {
            _suppressSupplierSearch = true;
            SupplierSearchText = draft.SupplierName;
            _suppressSupplierSearch = false;
            StatusMessage = "Supplier was not matched — select the supplier before saving.";
        }

        SupplierInvoiceNumber = draft.SupplierInvoiceNumber;
        InvoiceDate = draft.InvoiceDate ?? DateTime.Today;

        foreach (var line in Lines.ToList())
        {
            line.Changed -= RecalculateTotals;
            Lines.Remove(line);
        }

        foreach (var scanned in draft.Lines.Where(l => l.MatchedMedicineId is > 0))
        {
            var medicine = await _purchaseService.GetMedicineAsync(scanned.MatchedMedicineId!.Value);
            if (medicine is null) continue;

            var row = PurchaseLineViewModel.CreateEmpty();
            row.Changed += RecalculateTotals;
            row.ApplyMedicine(medicine);
            row.BatchNumber = scanned.BatchNumber ?? string.Empty;
            row.ExpiryDate = scanned.ExpiryDate ?? row.ExpiryDate;
            row.Quantity = scanned.Quantity > 0 ? scanned.Quantity : 1;
            row.FreeQuantity = scanned.FreeQuantity;
            if (scanned.PurchasePrice > 0) row.PurchasePrice = scanned.PurchasePrice;
            if (scanned.Mrp > 0) row.Mrp = scanned.Mrp;
            if (scanned.SellingPrice > 0) row.SellingPrice = scanned.SellingPrice;
            if (scanned.GstPercent > 0) row.GstPercent = scanned.GstPercent;
            if (scanned.DiscountPercent > 0) row.DiscountPercent = scanned.DiscountPercent;
            Lines.Add(row);
        }

        EnsureTrailingEmptyRow();
        RecalculateTotals();
        StatusMessage = $"Loaded {Lines.Count(l => !l.IsEmpty)} item(s) from scanned bill. Review and press Save (F9).";
        RequestItemFocus?.Invoke(Lines.FirstOrDefault(l => !l.IsEmpty) ?? Lines.FirstOrDefault());
    }

    private async Task TryApplyPendingPurchaseOrderReceiveAsync()
    {
        var draft = _poReceiveBridge.TakePending();
        if (draft is null) return;

        try
        {
            NewPurchase();
            SelectedSupplier = new SupplierLookupDto(
                draft.SupplierId, draft.SupplierName, null, null, 0);
            LinkedPurchaseOrderId = draft.PurchaseOrderId;
            LinkedPurchaseOrderNumber = draft.OrderNumber;

            foreach (var line in Lines.ToList())
            {
                line.Changed -= RecalculateTotals;
                Lines.Remove(line);
            }

            foreach (var poLine in draft.Lines)
            {
                var medicine = await _purchaseService.GetMedicineAsync(poLine.MedicineId)
                    ?? new PurchaseMedicineDto(
                        poLine.MedicineId,
                        poLine.MedicineName,
                        poLine.GenericName,
                        null,
                        poLine.GstPercent,
                        poLine.EstimatedPrice,
                        poLine.Mrp,
                        poLine.SellingPrice);

                var row = PurchaseLineViewModel.CreateEmpty();
                row.Changed += RecalculateTotals;
                row.ApplyMedicine(medicine);
                row.Quantity = poLine.RemainingQuantity;
                if (poLine.EstimatedPrice > 0) row.PurchasePrice = poLine.EstimatedPrice;
                if (poLine.Mrp > 0) row.Mrp = poLine.Mrp;
                if (poLine.SellingPrice > 0) row.SellingPrice = poLine.SellingPrice;
                if (poLine.GstPercent > 0) row.GstPercent = poLine.GstPercent;
                Lines.Add(row);
            }

            EnsureTrailingEmptyRow();
            RecalculateTotals();
            StatusMessage =
                $"Receiving PO {draft.OrderNumber}: {Lines.Count(l => !l.IsEmpty)} line(s). Enter batch/expiry, then Save (F9).";
            RequestItemFocus?.Invoke(Lines.FirstOrDefault(l => !l.IsEmpty) ?? Lines.FirstOrDefault());
        }
        catch (Exception ex)
        {
            _dialog.ShowError(ex.Message);
        }
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

    public decimal BalanceDue
    {
        get
        {
            var total = _editingPurchaseId.HasValue && _headerGrandTotal > 0
                ? _headerGrandTotal
                : GrandTotal;
            return total > PaidAmount ? total - PaidAmount : 0m;
        }
    }
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
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanModifyBill));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    private bool CanSave() =>
        CanModifyBill &&
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

        PurchasePartialPaymentReason? partialReason = null;
        string? partialNotes = null;
        int? linkedReturnId = null;

        if (BalanceDue > 0)
        {
            var openReturns = await _purchaseService.ListOpenPurchaseReturnCreditsAsync(
                SelectedSupplier.Id, _currentUser.CurrentUser?.BranchId);
            var reasonVm = new PartialPaymentReasonDialogViewModel(BalanceDue, openReturns);
            var dlg = new PartialPaymentReasonDialogWindow(reasonVm)
            {
                Owner = WpfApp.Current?.MainWindow
            };
            if (dlg.ShowDialog() != true)
                return;

            var reasonResult = reasonVm.BuildResult();
            if (reasonResult is null) return;
            partialReason = reasonResult.Reason;
            partialNotes = reasonResult.Notes;
            linkedReturnId = reasonResult.LinkedPurchaseReturnId;
        }

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
                        PartialPaymentReason = partialReason,
                        PartialPaymentNotes = partialNotes,
                        LinkedPurchaseReturnId = linkedReturnId,
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
                        PartialPaymentReason = partialReason,
                        PartialPaymentNotes = partialNotes,
                        LinkedPurchaseReturnId = linkedReturnId,
                        PurchaseOrderId = LinkedPurchaseOrderId,
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

                var creditLine = r.ReturnCreditApplied > 0
                    ? $"\nReturn credit applied: ₹{r.ReturnCreditApplied:N2}"
                    : string.Empty;
                _dialog.ShowInfo(
                    $"Purchase {r.InvoiceNumber} saved.\n\n" +
                    $"Items: {r.ItemCount}\nGrand total: ₹{r.GrandTotal:N2}\n" +
                    $"Paid (incl. credit): ₹{r.PaidAmount:N2}{creditLine}\nBalance due: ₹{r.BalanceDue:N2}",
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
        _headerGrandTotal = 0;
        _editingPurchaseId = null;
        LinkedPurchaseOrderId = null;
        LinkedPurchaseOrderNumber = null;
        _lastDropdownPurchaseId = 0;
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(CanModifyBill));
        OnPropertyChanged(nameof(IsBillReadOnly));

        RecalculateTotals();
        if (clearStatus)
            StatusMessage = null;

        CommandManager.InvalidateRequerySuggested();
        RequestItemFocus?.Invoke(Lines.FirstOrDefault(l => l.IsEmpty));
    }
}
