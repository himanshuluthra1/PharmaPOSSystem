using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Counters;
using PharmaPOS.Application.Features.Sales;
using PharmaPOS.Application.Features.Settings;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Shared.Constants;
using PharmaPOS.Shared.Results;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Sales;

/// <summary>
/// Drives the fast-billing screen: grid-based item entry via medicine/batch
/// picker popups, live GST-inclusive totals, payment and invoice finalization.
/// </summary>
public class SalesViewModel : ObservableObject
{
    private readonly ISalesService _salesService;
    private readonly ISettingsService _settings;
    private readonly IMedicinePickerService _picker;
    private readonly IBillSearchService _billSearch;
    private readonly ISaleReturnDialogService _saleReturnDialog;
    private readonly ICurrentUserService _currentUser;
    private readonly ICounterContextService _counterContext;
    private readonly IBillingCounterService _counters;
    private readonly ICounterPickerUiService _counterPicker;
    private readonly IDialogService _dialog;
    private readonly IInvoicePrintService _printService;
    private readonly IBarcodeCameraService _barcodeCamera;
    private readonly IBillShareService _billShare;

    private string _customerName = string.Empty;
    private string? _customerMobile;
    private string? _customerAddress;
    private string? _doctorName;

    private int? _editingSaleId;
    private bool _allowEditSalesBills;
    private bool _isInvoiceLocked;
    private string? _lockedBy;
    private bool _suppressBillSelection;
    private SaleListItemDto? _selectedBill;
    private int? _lastDropdownSaleId;
    private CancellationTokenSource? _billLoadCts;
    private readonly SemaphoreSlim _salesGate = new(1, 1);
    private DateOnly? _nextOlderBillDate;
    private bool _isLoadingOlderBills;
    private string? _counterCashSummary;

    public bool SuppressBillLoad
    {
        get => _suppressBillSelection;
        set => _suppressBillSelection = value;
    }

    private PaymentMethod _paymentMethod = PaymentMethod.Cash;
    private bool _isBusy;
    private string? _statusMessage;
    private int _activeBillIndex;
    private bool _suppressSlotSummary;

    public const int MaxOpenCustomerBills = 4;

    public SalesViewModel(
        ISalesService salesService,
        ISettingsService settings,
        IMedicinePickerService picker,
        IBillSearchService billSearch,
        ISaleReturnDialogService saleReturnDialog,
        ICurrentUserService currentUser,
        ICounterContextService counterContext,
        IBillingCounterService counters,
        ICounterPickerUiService counterPicker,
        IDialogService dialog,
        IInvoicePrintService printService,
        IBarcodeCameraService barcodeCamera,
        IBillShareService billShare)
    {
        _salesService = salesService;
        _settings = settings;
        _picker = picker;
        _billSearch = billSearch;
        _saleReturnDialog = saleReturnDialog;
        _currentUser = currentUser;
        _counterContext = counterContext;
        _counters = counters;
        _counterPicker = counterPicker;
        _dialog = dialog;
        _printService = printService;
        _barcodeCamera = barcodeCamera;
        _billShare = billShare;

        CanCreate = currentUser.HasAnyPermission(
            AppConstants.Permissions.SalesCreate, AppConstants.Permissions.SalesManage);
        CanSearchBills = currentUser.HasAnyPermission(
            AppConstants.Permissions.SalesView, AppConstants.Permissions.SalesManage);
        CanApplyDiscount = currentUser.HasAnyPermission(
            AppConstants.Permissions.SalesDiscount, AppConstants.Permissions.SalesManage);
        CanPrint = currentUser.HasAnyPermission(
            AppConstants.Permissions.SalesPrint, AppConstants.Permissions.SalesManage);
        CanReturn = currentUser.HasAnyPermission(
            AppConstants.Permissions.SalesReturn, AppConstants.Permissions.SalesReturnManage);
        CanEditInvoices = currentUser.HasAnyPermission(
            AppConstants.Permissions.SalesEdit, AppConstants.Permissions.SalesManage);
        CanUnlockInvoices = currentUser.HasAnyPermission(
            AppConstants.Permissions.SalesUnlock, AppConstants.Permissions.SalesManage);

        Cart.CollectionChanged += (_, _) => RecalculateTotals();

        RemoveLineCommand = new RelayCommand(p => RemoveLine(p as CartLineViewModel), _ => CanModifyBill);
        SaveCommand = new AsyncRelayCommand(_ => SaveAsync(), _ => CanSaveBill);
        UnlockBillCommand = new AsyncRelayCommand(_ => UnlockBillAsync(), _ => CanUnlockBill);
        PrintCommand = new AsyncRelayCommand(_ => PrintAsync(), _ => CanPrint && IsEditing && !IsBusy);
        NewBillCommand = new RelayCommand(_ => NewBill(), _ => CanCreate);
        SearchBillsCommand = new AsyncRelayCommand(_ => OpenBillSearchAsync(), _ => CanSearchBills && !IsBusy);
        OpenSaleReturnCommand = new AsyncRelayCommand(_ => OpenInlineReturnAsync(),
            _ => CanReturn && IsEditing && !IsBusy);
        LoadOlderBillsCommand = new AsyncRelayCommand(_ => LoadOlderBillsAsync(), _ => CanLoadOlderBills);
        ScanBarcodeCameraCommand = new AsyncRelayCommand(_ => ScanBarcodeCameraAsync(), _ => CanModifyBill && !IsBusy);
        SwitchOpenBillCommand = new RelayCommand(p => SwitchOpenBill(p), _ => CanCreate && !IsBusy);
        NewCustomerBillCommand = new RelayCommand(_ => OpenNewCustomerBill(), _ => CanCreate && !IsBusy && OpenBills.Count < MaxOpenCustomerBills);
        CloseCustomerBillCommand = new RelayCommand(_ => CloseActiveCustomerBill(), _ => CanCreate && !IsBusy);
        RefreshCounterCashCommand = new AsyncRelayCommand(RefreshCounterCashAsync, () => !IsBusy);
        ShowCounterCashSummaryCommand = new AsyncRelayCommand(ShowCounterCashSummaryAsync, () => !IsBusy);
        ChangeCounterCommand = new AsyncRelayCommand(ChangeCounterAsync, () => !IsBusy);

        OpenBills.Add(CreateSlot(1));
        OpenBills[0].IsActive = true;

        EnsureTrailingEmptyRow();
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await RefreshEditPolicyAsync();
        await InitializeBillsAsync();
        await RefreshCounterCashAsync();
    }

    private async Task RefreshEditPolicyAsync()
    {
        try
        {
            var prefs = await _settings.GetPreferencesAsync();
            AllowEditSalesBills = prefs.AllowEditSalesBills;
        }
        catch
        {
            AllowEditSalesBills = false;
        }
    }

    /// <summary>Asks the view to focus the Item column on the given (or last empty) line.</summary>
    public event Action<CartLineViewModel?>? RequestItemFocus;

    /// <summary>Asks the view to focus the customer name field.</summary>
    public event Action? RequestCustomerFocus;

    public ObservableCollection<CartLineViewModel> Cart { get; } = new();
    public ObservableCollection<SaleListItemDto> BillHistory { get; } = new();
    public ObservableCollection<OpenSaleBillSlot> OpenBills { get; } = new();

    public Array PaymentMethods => Enum.GetValues(typeof(PaymentMethod));

    public ICommand RemoveLineCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand UnlockBillCommand { get; }
    public ICommand PrintCommand { get; }
    public ICommand NewBillCommand { get; }
    public ICommand SearchBillsCommand { get; }
    public ICommand OpenSaleReturnCommand { get; }
    public ICommand LoadOlderBillsCommand { get; }
    public ICommand ScanBarcodeCameraCommand { get; }
    public ICommand SwitchOpenBillCommand { get; }
    public ICommand NewCustomerBillCommand { get; }
    public ICommand CloseCustomerBillCommand { get; }
    public ICommand RefreshCounterCashCommand { get; }
    public ICommand ShowCounterCashSummaryCommand { get; }
    public ICommand ChangeCounterCommand { get; }

    public string CounterDisplay =>
        _counterContext.ActiveCounterDisplay ?? "No counter selected";

    public bool HasCounterSession => _counterContext.HasActiveCounter;

    public string? CounterCashSummary
    {
        get => _counterCashSummary;
        private set => SetProperty(ref _counterCashSummary, value);
    }

    public bool CanCreate { get; }
    public bool CanSearchBills { get; }
    public bool CanApplyDiscount { get; }
    public bool CanPrint { get; }
    public bool CanReturn { get; }
    public bool CanEditInvoices { get; }
    public bool CanUnlockInvoices { get; }

    public bool AllowEditSalesBills
    {
        get => _allowEditSalesBills;
        private set
        {
            if (SetProperty(ref _allowEditSalesBills, value))
                NotifyBillEditStateChanged();
        }
    }

    public bool IsInvoiceLocked
    {
        get => _isInvoiceLocked;
        private set
        {
            if (SetProperty(ref _isInvoiceLocked, value))
                NotifyBillEditStateChanged();
        }
    }

    public string? LockedBy
    {
        get => _lockedBy;
        private set
        {
            if (SetProperty(ref _lockedBy, value))
                OnPropertyChanged(nameof(LockBannerText));
        }
    }

    public string LockBannerText =>
        string.IsNullOrWhiteSpace(LockedBy)
            ? "This invoice is locked. Unlock it to make changes."
            : $"This invoice is locked by {LockedBy}. Unlock it to make changes.";

    /// <summary>True when the loaded bill can be changed (new bill, or unlocked edit allowed).</summary>
    public bool CanModifyBill =>
        CanCreate
        && (!IsEditing || (AllowEditSalesBills && CanEditInvoices && !IsInvoiceLocked))
        && !IsBusy;

    public bool CanSaveBill => CanModifyBill && Cart.Any(l => !l.IsEmpty);

    public bool ShowSaveButton =>
        CanCreate && (!IsEditing || (AllowEditSalesBills && CanEditInvoices && !IsInvoiceLocked));

    public bool IsBillReadOnly =>
        IsEditing && !(AllowEditSalesBills && CanEditInvoices && !IsInvoiceLocked);

    public bool CanUnlockBill =>
        IsEditing && IsInvoiceLocked && AllowEditSalesBills && CanUnlockInvoices && !IsBusy;

    public bool ShowLockBanner => IsEditing && IsInvoiceLocked && AllowEditSalesBills;

    public bool CanLoadOlderBills => _nextOlderBillDate is not null && !IsLoadingOlderBills && !IsBusy;

    public bool IsLoadingOlderBills
    {
        get => _isLoadingOlderBills;
        private set
        {
            if (SetProperty(ref _isLoadingOlderBills, value))
            {
                OnPropertyChanged(nameof(CanLoadOlderBills));
                OnPropertyChanged(nameof(LoadOlderBillsLabel));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string? LoadOlderBillsLabel =>
        _nextOlderBillDate is DateOnly date
            ? $"Load bills from {date:dd/MM/yyyy}"
            : null;

    public bool IsEditing => _editingSaleId.HasValue;

    public SaleListItemDto? SelectedBill
    {
        get => _selectedBill;
        set => SetProperty(ref _selectedBill, value);
    }

    public bool HasItems => Cart.Any(l => !l.IsEmpty);

    #region Grid item picker

    public async Task BeginItemSelectionAsync(CartLineViewModel line)
    {
        if (!CanModifyBill || line.IsReturnLine) return;

        var selection = await _picker.PickMedicineAsync();
        if (selection is null) return;

        var duplicate = Cart.FirstOrDefault(l => l != line && l.BatchId == selection.BatchId && l.BatchId > 0);
        if (duplicate is not null)
        {
            var newQty = duplicate.Quantity + 1;
            if (newQty > selection.AvailableStock)
            {
                _dialog.ShowError($"Only {selection.AvailableStock} units available in batch {selection.BatchNumber}.");
                return;
            }
            duplicate.Quantity = newQty;
            if (line.IsEmpty) Cart.Remove(line);
        }
        else
        {
            line.ApplySelection(selection);
        }

        EnsureTrailingEmptyRow();
        RecalculateTotals();

        var focusLine = duplicate ?? line;
        if (!focusLine.IsEmpty)
            RequestItemFocus?.Invoke(focusLine);
    }

    public async Task TryAddByBarcodeAsync(string barcode)
    {
        if (!CanModifyBill || IsBusy) return;
        barcode = barcode.Trim();
        if (barcode.Length < 3) return;

        IsBusy = true;
        try
        {
            await _salesGate.WaitAsync();
            try
            {
                var medicine = await _salesService.FindMedicineByBarcodeAsync(
                    barcode, _currentUser.CurrentUser?.BranchId);
                if (medicine is null)
                {
                    _dialog.ShowError($"No medicine found for barcode \"{barcode}\".");
                    return;
                }

                var selection = await _picker.PickBatchForMedicineAsync(medicine);
                if (selection is null) return;

                var existing = Cart.FirstOrDefault(l => l.BatchId == selection.BatchId && l.BatchId > 0 && !l.IsReturnLine);
                if (existing is not null)
                {
                    var newQty = existing.Quantity + 1;
                    if (newQty > selection.AvailableStock)
                    {
                        _dialog.ShowError($"Only {selection.AvailableStock} units available in batch {selection.BatchNumber}.");
                        return;
                    }
                    existing.Quantity = newQty;
                    RecalculateTotals();
                    RequestItemFocus?.Invoke(existing);
                    StatusMessage = $"+1 {selection.MedicineName}";
                    return;
                }

                var target = Cart.FirstOrDefault(l => l.IsEmpty) ?? CartLineViewModel.CreateEmpty();
                if (!Cart.Contains(target))
                {
                    target.Changed += RecalculateTotals;
                    Cart.Add(target);
                }
                target.ApplySelection(selection);
                EnsureTrailingEmptyRow();
                RecalculateTotals();
                RequestItemFocus?.Invoke(target);
                StatusMessage = $"Added {selection.MedicineName}";
            }
            finally
            {
                _salesGate.Release();
            }
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

    public async Task ShowMedicineDetailAsync(CartLineViewModel line)
    {
        if (line.IsEmpty)
        {
            _dialog.ShowInfo("Select a medicine line first.");
            return;
        }

        var detail = await _salesService.GetMedicineLineDetailAsync(
            line.MedicineId,
            line.BatchId > 0 ? line.BatchId : null,
            _currentUser.CurrentUser?.BranchId);

        if (detail is null)
        {
            _dialog.ShowError("Medicine details could not be loaded.");
            return;
        }

        var window = new Views.MedicineDetailPopupWindow(detail)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    public async Task ReplaceWithSubstituteAsync(CartLineViewModel line)
    {
        if (line.IsEmpty || line.IsReturnLine)
        {
            _dialog.ShowInfo("Select a medicine line first.");
            return;
        }

        var substitutes = await _salesService.ListSameSaltMedicinesAsync(
            line.MedicineId, _currentUser.CurrentUser?.BranchId);
        if (substitutes.Count == 0)
        {
            _dialog.ShowInfo("No medicines found with the same salt and strength.", "Substitute");
            return;
        }

        var selection = await _picker.PickSubstituteAsync(substitutes, line.MedicineId);
        if (selection is null) return;

        line.ApplySelection(selection);
        EnsureTrailingEmptyRow();
        RecalculateTotals();
        RequestItemFocus?.Invoke(line);
    }

    private void EnsureTrailingEmptyRow()
    {
        if (Cart.Count == 0 || !Cart[^1].IsEmpty)
        {
            var empty = CartLineViewModel.CreateEmpty();
            empty.Changed += RecalculateTotals;
            Cart.Add(empty);
        }
    }

    private void RemoveLine(CartLineViewModel? line)
    {
        if (line is null || line.IsEmpty || line.IsReturnLine) return;
        line.Changed -= RecalculateTotals;
        Cart.Remove(line);
        EnsureTrailingEmptyRow();
        RecalculateTotals();
    }

    #endregion

    #region F3 navigation / save

    public void GoToCustomerOrWarn()
    {
        if (!HasItems)
        {
            _dialog.ShowInfo("Please add at least one item to the bill.");
            return;
        }
        RequestCustomerFocus?.Invoke();
    }

    public async Task TrySaveFromCustomerAsync()
    {
        if (string.IsNullOrWhiteSpace(CustomerName))
        {
            _dialog.ShowInfo("Please enter customer name before saving.");
            return;
        }
        if (SaveCommand.CanExecute(null))
            await SaveAsync();
    }

    #endregion

    #region Customer / doctor (free text)

    public string CustomerName
    {
        get => _customerName;
        set
        {
            if (SetProperty(ref _customerName, value))
                RefreshActiveSlotSummary();
        }
    }

    public string? CustomerMobile
    {
        get => _customerMobile;
        set => SetProperty(ref _customerMobile, value);
    }

    public string? CustomerAddress
    {
        get => _customerAddress;
        set => SetProperty(ref _customerAddress, value);
    }

    public string? DoctorName
    {
        get => _doctorName;
        set => SetProperty(ref _doctorName, value);
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

    public int ItemCount => Cart.Count(l => !l.IsEmpty);

    public PaymentMethod PaymentMethod
    {
        get => _paymentMethod;
        set
        {
            if (SetProperty(ref _paymentMethod, value))
            {
                OnPropertyChanged(nameof(BalanceDue));
                OnPropertyChanged(nameof(IsCreditSale));
            }
        }
    }

    public bool IsCreditSale => PaymentMethod == PaymentMethod.Credit;

    public decimal BalanceDue => IsCreditSale ? GrandTotal : 0m;

    private void RecalculateTotals()
    {
        var lines = Cart.Where(l => !l.IsEmpty).ToList();
        var summary = SaleLinePricing.ComputeBillSummary(
            lines.Select(l => (l.Mrp, l.Quantity, l.UnitPrice, l.GstPercent)));

        SubTotal = summary.SubTotalMrp;
        DiscountTotal = summary.Discount;
        TaxableTotal = summary.Taxable;
        Cgst = summary.Cgst;
        Sgst = summary.Sgst;
        RoundOff = summary.RoundOff;
        GrandTotal = summary.GrandTotal;

        OnPropertyChanged(nameof(ItemCount));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(BalanceDue));
        RefreshActiveSlotSummary();
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
                OnPropertyChanged(nameof(CanLoadOlderBills));
                OnPropertyChanged(nameof(CanModifyBill));
                OnPropertyChanged(nameof(CanSaveBill));
                OnPropertyChanged(nameof(CanUnlockBill));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    private async Task InitializeBillsAsync()
    {
        await RefreshBillHistoryAsync(selectNewBill: true);
    }

    private async Task RefreshBillHistoryAsync(bool selectNewBill = false, int? selectSaleId = null)
    {
        await _salesGate.WaitAsync();
        try
        {
            await RefreshBillHistoryCoreAsync(selectNewBill, selectSaleId);
        }
        finally
        {
            _salesGate.Release();
        }
    }

    private async Task RefreshBillHistoryCoreAsync(bool selectNewBill = false, int? selectSaleId = null)
    {
        try
        {
            var branchId = _currentUser.CurrentUser?.BranchId;
            var initialDate = await _salesService.GetInitialBillHistoryDateAsync(branchId);
            var bills = await _salesService.ListBillsForDateAsync(initialDate, branchId);
            var preview = await _salesService.PreviewNextInvoiceNumberAsync(branchId);
            var newBill = new SaleListItemDto(0, preview, DateTime.Now);

            _nextOlderBillDate = await _salesService.GetPreviousBillDateAsync(initialDate, branchId);

            _suppressBillSelection = true;
            BillHistory.Clear();
            BillHistory.Add(newBill);
            foreach (var bill in bills)
                BillHistory.Add(bill);

            if (selectSaleId is int saleId)
                SelectedBill = BillHistory.FirstOrDefault(b => b.SaleId == saleId) ?? newBill;
            else if (selectNewBill)
                SelectedBill = newBill;
            else if (_editingSaleId is int editingId)
                SelectedBill = BillHistory.FirstOrDefault(b => b.SaleId == editingId) ?? newBill;

            _suppressBillSelection = false;
            OnPropertyChanged(nameof(CanLoadOlderBills));
            OnPropertyChanged(nameof(LoadOlderBillsLabel));
        }
        catch (Exception ex)
        {
            _dialog.ShowError($"Could not load bill history: {ex.Message}");
        }
    }

    private async Task LoadOlderBillsAsync()
    {
        if (_nextOlderBillDate is not DateOnly dateToLoad)
            return;

        IsLoadingOlderBills = true;
        try
        {
            var branchId = _currentUser.CurrentUser?.BranchId;
            var bills = await _salesService.ListBillsForDateAsync(dateToLoad, branchId);

            foreach (var bill in bills)
                BillHistory.Add(bill);

            _nextOlderBillDate = await _salesService.GetPreviousBillDateAsync(dateToLoad, branchId);
            OnPropertyChanged(nameof(CanLoadOlderBills));
            OnPropertyChanged(nameof(LoadOlderBillsLabel));
        }
        catch (Exception ex)
        {
            _dialog.ShowError($"Could not load older bills: {ex.Message}");
        }
        finally
        {
            IsLoadingOlderBills = false;
        }
    }

    private async Task OnBillSelectedAsync(SaleListItemDto bill)
    {
        if (bill.SaleId == 0)
        {
            if (_editingSaleId.HasValue)
                ResetBillForm(clearStatus: true);
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _salesService.GetSaleForEditAsync(bill.SaleId, _currentUser.CurrentUser?.BranchId);
            if (result.IsFailure || result.Value is null)
            {
                _dialog.ShowError(result.Error ?? "Could not load the invoice.");
                return;
            }

            LoadSale(result.Value);
            var returnCount = result.Value.Lines.Count(l => l.IsReturnLine);
            StatusMessage = returnCount > 0
                ? $"Invoice {result.Value.InvoiceNumber} — {returnCount} return line(s) applied."
                : $"Editing invoice {result.Value.InvoiceNumber}.";
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

    private async Task OpenBillSearchAsync()
    {
        var bill = await _billSearch.PickBillAsync();
        if (bill is null) return;

        _selectedBill = BillHistory.FirstOrDefault(b => b.SaleId == bill.SaleId) ?? bill;
        OnPropertyChanged(nameof(SelectedBill));
        await LoadBillFromDropdownAsync(bill, focusGridAfterLoad: true);
    }

    private async Task OpenInlineReturnAsync()
    {
        if (_editingSaleId is not int saleId)
        {
            _dialog.ShowInfo("Open a saved invoice first, then click Return.", "Sale Return");
            return;
        }

        if (SelectedBill?.Status == SaleStatus.Returned)
        {
            _dialog.ShowInfo("This invoice is already fully returned.", "Sale Return");
            return;
        }

        IsBusy = true;
        try
        {
            var dialogResult = await _saleReturnDialog.ShowForSaleAsync(saleId);
            if (!dialogResult.DialogShown) return;

            await RefreshInvoiceAfterReturnDialogAsync(saleId, dialogResult);
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

    private async Task RefreshInvoiceAfterReturnDialogAsync(int saleId, SaleReturnDialogResult dialogResult)
    {
        if (dialogResult.ReturnPosted && dialogResult.Receipt is not null)
            StatusMessage = $"Return {dialogResult.Receipt.ReturnNumber} posted.";

        await RefreshBillHistoryAsync(selectNewBill: false, selectSaleId: saleId);
        var bill = BillHistory.FirstOrDefault(b => b.SaleId == saleId);
        if (bill is null) return;

        _lastDropdownSaleId = null;
        await LoadBillFromDropdownAsync(bill, focusGridAfterLoad: false, forceReload: true);
    }

    /// <summary>Loads a bill from the dropdown (invoice-number click or Enter).</summary>
    public async Task LoadBillFromDropdownAsync(SaleListItemDto bill, bool focusGridAfterLoad = false, bool forceReload = false)
    {
        if (_suppressBillSelection) return;

        if (bill.SaleId == 0)
        {
            _lastDropdownSaleId = 0;
            if (_editingSaleId.HasValue)
                ResetBillForm(clearStatus: true);
            if (focusGridAfterLoad)
                RequestItemFocus?.Invoke(Cart.FirstOrDefault());
            return;
        }

        if (!forceReload && _lastDropdownSaleId == bill.SaleId)
        {
            if (focusGridAfterLoad)
                RequestItemFocus?.Invoke(Cart.FirstOrDefault());
            return;
        }

        _billLoadCts?.Cancel();
        _billLoadCts?.Dispose();
        _billLoadCts = new CancellationTokenSource();
        var token = _billLoadCts.Token;

                IsBusy = true;
        try
        {
            await RefreshEditPolicyAsync();
            await _salesGate.WaitAsync(token);
            try
            {
                if (token.IsCancellationRequested) return;

                var result = await _salesService.GetSaleForEditAsync(bill.SaleId, _currentUser.CurrentUser?.BranchId);
                if (token.IsCancellationRequested) return;

                if (result.IsFailure || result.Value is null)
                {
                    _dialog.ShowError(result.Error ?? "Could not load the invoice.");
                    return;
                }

                _lastDropdownSaleId = bill.SaleId;
                _selectedBill = bill;
                OnPropertyChanged(nameof(SelectedBill));
                LoadSale(result.Value, focusGridAfterLoad);
                var returnCount = result.Value.Lines.Count(l => l.IsReturnLine);
                StatusMessage = returnCount > 0
                    ? $"Invoice {result.Value.InvoiceNumber} — {returnCount} return line(s) applied."
                    : $"Editing invoice {result.Value.InvoiceNumber}.";
            }
            finally
            {
                _salesGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer highlight while browsing the list.
        }
        catch (Exception ex)
        {
            _dialog.ShowError(ex.Message);
        }
        finally
        {
            if (!token.IsCancellationRequested)
                IsBusy = false;
            else if (_billLoadCts?.Token == token)
                IsBusy = false;
        }
    }

    private void LoadSale(SaleEditDto sale, bool focusGridAfterLoad = true)
    {
        foreach (var line in Cart)
            line.Changed -= RecalculateTotals;
        Cart.Clear();

        _editingSaleId = sale.SaleId;
        IsInvoiceLocked = sale.IsLocked;
        LockedBy = sale.LockedBy;
        NotifyBillEditStateChanged();

        CustomerName = sale.BillingCustomerName ?? string.Empty;
        CustomerMobile = sale.BillingCustomerPhone;
        CustomerAddress = sale.BillingCustomerAddress;
        DoctorName = sale.BillingDoctorName;
        PaymentMethod = sale.PaymentMethod;

        foreach (var line in sale.Lines)
        {
            var vm = CartLineViewModel.CreateEmpty();
            vm.LoadFromSaleLine(line);
            vm.Changed += RecalculateTotals;
            Cart.Add(vm);
        }

        EnsureTrailingEmptyRow();
        RecalculateTotals();
        StatusMessage = BuildEditStatusMessage(sale.InvoiceNumber);
        if (focusGridAfterLoad && !IsBillReadOnly)
            RequestItemFocus?.Invoke(Cart.FirstOrDefault());
    }

    private string BuildEditStatusMessage(string invoiceNumber)
    {
        if (!AllowEditSalesBills)
            return $"Viewing invoice {invoiceNumber} (edit is off in Settings → Preferences).";
        if (!CanEditInvoices)
            return $"Viewing invoice {invoiceNumber} (your role cannot edit sale invoices).";
        if (IsInvoiceLocked)
            return $"Invoice {invoiceNumber} is locked. Unlock to edit.";
        return $"Editing invoice {invoiceNumber}. Save to update (re-locks on save).";
    }

    private void NotifyBillEditStateChanged()
    {
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(CanModifyBill));
        OnPropertyChanged(nameof(CanSaveBill));
        OnPropertyChanged(nameof(ShowSaveButton));
        OnPropertyChanged(nameof(IsBillReadOnly));
        OnPropertyChanged(nameof(CanUnlockBill));
        OnPropertyChanged(nameof(ShowLockBanner));
        OnPropertyChanged(nameof(LockBannerText));
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task SaveAsync()
    {
        var lines = Cart.Where(l => !l.IsEmpty && !l.IsReturnLine).ToList();
        if (lines.Count == 0) return;

        foreach (var line in lines)
        {
            if (line.Quantity > line.AvailableStock)
            {
                _dialog.ShowError($"Insufficient stock for {line.MedicineName} (batch {line.BatchNumber}).");
                return;
            }
        }

        if (PaymentMethod == PaymentMethod.Credit && string.IsNullOrWhiteSpace(CustomerName))
        {
            _dialog.ShowError("Enter the customer name for credit sales.");
            RequestCustomerFocus?.Invoke();
            return;
        }

        if (!_editingSaleId.HasValue && !_counterContext.HasActiveCounter)
        {
            _dialog.ShowError("Select a billing counter before saving. Sign out and sign in to choose a counter.");
            return;
        }

        var lineRequests = lines.Select(l => new SaleLineRequest
        {
            MedicineId = l.MedicineId,
            MedicineBatchId = l.BatchId,
            BatchNumber = l.BatchNumber,
            Quantity = l.Quantity,
            Mrp = l.Mrp,
            UnitPrice = l.UnitPrice,
            DiscountPercent = l.DiscountPercent
        }).ToList();

        // Credit tenders are recorded for audit but do not count as cash paid.
        var payments = new List<SalePaymentRequest>
        {
            new()
            {
                Method = PaymentMethod,
                Amount = GrandTotal
            }
        };

        IsBusy = true;
        try
        {
            await _salesGate.WaitAsync();
            try
            {
                Result<SaleReceiptDto> result;
                if (_editingSaleId is int saleId)
                {
                    result = await _salesService.UpdateSaleAsync(new UpdateSaleRequest
                    {
                        SaleId = saleId,
                        BillingCustomerName = string.IsNullOrWhiteSpace(CustomerName) ? null : CustomerName.Trim(),
                        BillingCustomerPhone = string.IsNullOrWhiteSpace(CustomerMobile) ? null : CustomerMobile.Trim(),
                        BillingCustomerAddress = string.IsNullOrWhiteSpace(CustomerAddress) ? null : CustomerAddress.Trim(),
                        BillingDoctorName = string.IsNullOrWhiteSpace(DoctorName) ? null : DoctorName.Trim(),
                        Payments = payments,
                        Lines = lineRequests
                    }, _currentUser.CurrentUser?.BranchId);
                }
                else
                {
                    result = await _salesService.CreateSaleAsync(new CreateSaleRequest
                    {
                        BillingCustomerName = string.IsNullOrWhiteSpace(CustomerName) ? null : CustomerName.Trim(),
                        BillingCustomerPhone = string.IsNullOrWhiteSpace(CustomerMobile) ? null : CustomerMobile.Trim(),
                        BillingCustomerAddress = string.IsNullOrWhiteSpace(CustomerAddress) ? null : CustomerAddress.Trim(),
                        BillingDoctorName = string.IsNullOrWhiteSpace(DoctorName) ? null : DoctorName.Trim(),
                        Payments = payments,
                        Lines = lineRequests,
                        CounterId = _counterContext.ActiveCounterId,
                        CounterSessionId = _counterContext.ActiveSessionId
                    }, _currentUser.CurrentUser?.BranchId);
                }

                if (result.IsFailure || result.Value is null)
                {
                    _dialog.ShowError(result.Error ?? "Could not save the invoice.");
                    return;
                }

                var receipt = result.Value;
                StatusMessage = $"Saved invoice {receipt.InvoiceNumber}.";

                if (_billShare.ShouldOfferAfterSave(receipt))
                    _billShare.OfferShareAfterSave(receipt);

                if (CanPrint && _dialog.Confirm($"Invoice {receipt.InvoiceNumber} saved.\n\nPrint / preview it now?", "Invoice saved"))
                    _printService.ShowPreview(receipt);

                await RefreshBillHistoryCoreAsync(selectNewBill: true);
                ResetBillForm(clearStatus: false);
                await RefreshCounterCashAsync();
            }
            finally
            {
                _salesGate.Release();
            }

            RequestItemFocus?.Invoke(Cart.FirstOrDefault());
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

    private async Task UnlockBillAsync()
    {
        if (_editingSaleId is not int saleId || !CanUnlockBill) return;

        if (!_dialog.Confirm(
                "Unlock this invoice for editing?\n\nIt will lock again after you save changes.",
                "Unlock invoice"))
            return;

        IsBusy = true;
        try
        {
            var result = await _salesService.UnlockSaleAsync(saleId, _currentUser.CurrentUser?.BranchId);
            if (result.IsFailure)
            {
                _dialog.ShowError(result.Error ?? "Could not unlock the invoice.");
                return;
            }

            IsInvoiceLocked = false;
            LockedBy = null;
            StatusMessage = $"Invoice unlocked. Edit and save to update.";
            NotifyBillEditStateChanged();
            RequestItemFocus?.Invoke(Cart.FirstOrDefault());
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

    private async Task PrintAsync()
    {
        if (_editingSaleId is not int saleId) return;

        IsBusy = true;
        try
        {
            await _salesGate.WaitAsync();
            try
            {
                var result = await _salesService.GetSaleReceiptAsync(saleId, _currentUser.CurrentUser?.BranchId);
                if (result.IsFailure || result.Value is null)
                {
                    _dialog.ShowError(result.Error ?? "Could not load the invoice for printing.");
                    return;
                }

                _printService.ShowPreview(result.Value);
                StatusMessage = $"Printing invoice {result.Value.InvoiceNumber}.";
            }
            finally
            {
                _salesGate.Release();
            }
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

    private void NewBill()
    {
        _suppressBillSelection = true;
        SelectedBill = BillHistory.FirstOrDefault(b => b.SaleId == 0);
        _suppressBillSelection = false;
        ResetBillForm(clearStatus: true);
        StatusMessage = $"Cleared customer bill {OpenBills[_activeBillIndex].DisplayNumber}. Other open bills are unchanged.";
        RefreshActiveSlotSummary();
    }

    private void ResetBillForm(bool clearStatus)
    {
        foreach (var line in Cart)
            line.Changed -= RecalculateTotals;
        Cart.Clear();
        _editingSaleId = null;
        IsInvoiceLocked = false;
        LockedBy = null;
        _lastDropdownSaleId = null;
        NotifyBillEditStateChanged();
        EnsureTrailingEmptyRow();
        _suppressSlotSummary = true;
        CustomerName = string.Empty;
        CustomerMobile = null;
        CustomerAddress = null;
        DoctorName = null;
        _suppressSlotSummary = false;
        PaymentMethod = PaymentMethod.Cash;
        RecalculateTotals();
        if (clearStatus) StatusMessage = null;
    }

    #region Multi-customer open bills

    private static OpenSaleBillSlot CreateSlot(int displayNumber) => new(displayNumber);

    public bool TrySwitchToBillNumber(int displayNumber)
    {
        var index = OpenBills.ToList().FindIndex(b => b.DisplayNumber == displayNumber);
        if (index < 0) return false;
        SwitchToBillIndex(index);
        return true;
    }

    private void SwitchOpenBill(object? parameter)
    {
        switch (parameter)
        {
            case OpenSaleBillSlot slot:
                var idx = OpenBills.IndexOf(slot);
                if (idx >= 0) SwitchToBillIndex(idx);
                break;
            case int number:
                TrySwitchToBillNumber(number);
                break;
            case string text when int.TryParse(text, out var parsed):
                TrySwitchToBillNumber(parsed);
                break;
        }
    }

    private void OpenNewCustomerBill()
    {
        if (OpenBills.Count >= MaxOpenCustomerBills)
        {
            _dialog.ShowInfo(
                $"You can keep up to {MaxOpenCustomerBills} customer bills open. Close one (Ctrl+W) before starting another.",
                "Multi-customer");
            return;
        }

        ParkActiveBill();
        var slot = CreateSlot(OpenBills.Count + 1);
        OpenBills.Add(slot);
        RenumberSlots();
        _activeBillIndex = OpenBills.Count - 1;
        ApplyActiveFlags();
        ResetBillForm(clearStatus: true);
        StatusMessage = $"Opened customer bill {slot.DisplayNumber}. Press Ctrl+1–{OpenBills.Count} to switch.";
        RefreshActiveSlotSummary();
        RequestItemFocus?.Invoke(Cart.FirstOrDefault(l => l.IsEmpty));
        CommandManager.InvalidateRequerySuggested();
    }

    private void CloseActiveCustomerBill()
    {
        var active = OpenBills[_activeBillIndex];
        var hasContent = Cart.Any(l => !l.IsEmpty) || _editingSaleId.HasValue
                         || !string.IsNullOrWhiteSpace(CustomerName);
        if (hasContent && !_dialog.Confirm(
                $"Close customer bill {active.DisplayNumber}" +
                (string.IsNullOrWhiteSpace(CustomerName) ? "" : $" ({CustomerName.Trim()})") +
                "? Unsaved items on this bill will be discarded.",
                "Close customer bill"))
            return;

        if (OpenBills.Count == 1)
        {
            NewBill();
            StatusMessage = "Customer bill cleared.";
            return;
        }

        OpenBills.RemoveAt(_activeBillIndex);
        if (_activeBillIndex >= OpenBills.Count)
            _activeBillIndex = OpenBills.Count - 1;
        RenumberSlots();
        RestoreBill(OpenBills[_activeBillIndex]);
        ApplyActiveFlags();
        StatusMessage = $"Closed bill. Now on customer bill {OpenBills[_activeBillIndex].DisplayNumber}.";
        RefreshActiveSlotSummary();
        RequestItemFocus?.Invoke(Cart.FirstOrDefault(l => !l.IsEmpty) ?? Cart.FirstOrDefault());
        CommandManager.InvalidateRequerySuggested();
    }

    private void SwitchToBillIndex(int index)
    {
        if (index < 0 || index >= OpenBills.Count || index == _activeBillIndex)
            return;

        ParkActiveBill();
        _activeBillIndex = index;
        RestoreBill(OpenBills[_activeBillIndex]);
        ApplyActiveFlags();
        StatusMessage = $"Switched to customer bill {OpenBills[_activeBillIndex].DisplayNumber}" +
                        (string.IsNullOrWhiteSpace(CustomerName) ? "." : $": {CustomerName.Trim()}.");
        RequestItemFocus?.Invoke(Cart.FirstOrDefault(l => !l.IsEmpty) ?? Cart.FirstOrDefault());
        CommandManager.InvalidateRequerySuggested();
    }

    private void ParkActiveBill()
    {
        var slot = OpenBills[_activeBillIndex];
        slot.Parked = CaptureCurrentBill();
        slot.UpdateSummary(CustomerName, ItemCount, GrandTotal);
    }

    private ParkedSaleBill CaptureCurrentBill() => new()
    {
        Lines = Cart.Where(l => !l.IsEmpty).Select(l => l.ToParked()).ToList(),
        CustomerName = CustomerName,
        CustomerMobile = CustomerMobile,
        CustomerAddress = CustomerAddress,
        DoctorName = DoctorName,
        PaymentMethod = PaymentMethod,
        EditingSaleId = _editingSaleId,
        IsInvoiceLocked = IsInvoiceLocked,
        LockedBy = LockedBy,
        StatusMessage = StatusMessage
    };

    private void RestoreBill(OpenSaleBillSlot slot)
    {
        var parked = slot.Parked ?? new ParkedSaleBill();
        foreach (var line in Cart)
            line.Changed -= RecalculateTotals;
        Cart.Clear();

        _editingSaleId = parked.EditingSaleId;
        IsInvoiceLocked = parked.IsInvoiceLocked;
        LockedBy = parked.LockedBy;
        NotifyBillEditStateChanged();

        _suppressSlotSummary = true;
        CustomerName = parked.CustomerName;
        CustomerMobile = parked.CustomerMobile;
        CustomerAddress = parked.CustomerAddress;
        DoctorName = parked.DoctorName;
        _suppressSlotSummary = false;
        PaymentMethod = parked.PaymentMethod;
        StatusMessage = parked.StatusMessage;

        foreach (var parkedLine in parked.Lines)
        {
            var vm = CartLineViewModel.CreateEmpty();
            vm.LoadFromParked(parkedLine);
            vm.Changed += RecalculateTotals;
            Cart.Add(vm);
        }

        EnsureTrailingEmptyRow();
        RecalculateTotals();

        _suppressBillSelection = true;
        SelectedBill = parked.EditingSaleId is int saleId
            ? BillHistory.FirstOrDefault(b => b.SaleId == saleId) ?? BillHistory.FirstOrDefault(b => b.SaleId == 0)
            : BillHistory.FirstOrDefault(b => b.SaleId == 0);
        _suppressBillSelection = false;
        _lastDropdownSaleId = SelectedBill?.SaleId;
        slot.Parked = null;
    }

    private void ApplyActiveFlags()
    {
        for (var i = 0; i < OpenBills.Count; i++)
            OpenBills[i].IsActive = i == _activeBillIndex;
    }

    private void RenumberSlots()
    {
        for (var i = 0; i < OpenBills.Count; i++)
            OpenBills[i].SetDisplayNumber(i + 1);
    }

    private void RefreshActiveSlotSummary()
    {
        if (_suppressSlotSummary || OpenBills.Count == 0) return;
        if (_activeBillIndex < 0 || _activeBillIndex >= OpenBills.Count) return;
        OpenBills[_activeBillIndex].UpdateSummary(CustomerName, ItemCount, GrandTotal);
    }

    private async Task RefreshCounterCashAsync()
    {
        OnPropertyChanged(nameof(CounterDisplay));
        OnPropertyChanged(nameof(HasCounterSession));

        if (_counterContext.ActiveCounterId is not int counterId)
        {
            CounterCashSummary = "Select a billing counter to track cash.";
            return;
        }

        try
        {
            var row = await _counters.GetActiveCounterCashAsync(
                counterId, _counterContext.ActiveSessionId, DateTime.Today);
            if (row is null)
            {
                CounterCashSummary = null;
                return;
            }

            CounterCashSummary =
                $"Today · Bills {row.BillCount} · Cash ₹{row.CashCollected:N0} · Drawer ₹{row.ExpectedCashInDrawer:N0}" +
                (row.OpeningFloat > 0 ? $" (float ₹{row.OpeningFloat:N0})" : string.Empty);
        }
        catch
        {
            CounterCashSummary = null;
        }
    }

    private async Task ShowCounterCashSummaryAsync()
    {
        try
        {
            var rows = await _counters.GetCashSummaryAsync(_currentUser.CurrentUser?.BranchId, DateTime.Today);
            if (rows.Count == 0)
            {
                _dialog.ShowInfo("No billing counters found.", "Counter cash");
                return;
            }

            var lines = rows.Select(r =>
                $"{r.CounterCode} ({r.CounterName}): bills {r.BillCount}, cash ₹{r.CashCollected:N0}" +
                (r.OperatorName is null ? "" : $", op {r.OperatorName}") +
                $", drawer ₹{r.ExpectedCashInDrawer:N0}");
            _dialog.ShowInfo(string.Join("\n", lines), "Today's cash by counter");
            await RefreshCounterCashAsync();
        }
        catch (Exception ex)
        {
            _dialog.ShowError(ex.Message);
        }
    }

    private async Task ChangeCounterAsync()
    {
        var current = _counterContext.ActiveCounterDisplay ?? "none";
        if (!_dialog.Confirm(
                $"You are on {current}.\n\nSwitch to a different billing counter?\n\n" +
                "New sales will go to the counter you pick next. Bills already saved stay on the old counter.",
                "Change counter"))
            return;

        if (!_counterPicker.ShowPicker(switchMode: true))
            return;

        // Cached Sales VM keeps old header until we refresh.
        await RefreshCounterCashAsync();
        StatusMessage = $"Now billing on {_counterContext.ActiveCounterDisplay}.";
    }

    #endregion
}
