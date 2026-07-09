using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Sales;
using PharmaPOS.Domain.Enums;
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
    private readonly IMedicinePickerService _picker;
    private readonly IBillSearchService _billSearch;
    private readonly ICurrentUserService _currentUser;
    private readonly IDialogService _dialog;
    private readonly IInvoicePrintService _printService;

    private string _customerName = string.Empty;
    private string? _customerMobile;
    private string? _customerAddress;
    private string? _doctorName;

    private int? _editingSaleId;
    private bool _suppressBillSelection;
    private SaleListItemDto? _selectedBill;
    private int? _lastDropdownSaleId;
    private CancellationTokenSource? _billLoadCts;
    private readonly SemaphoreSlim _salesGate = new(1, 1);

    public bool SuppressBillLoad
    {
        get => _suppressBillSelection;
        set => _suppressBillSelection = value;
    }

    private PaymentMethod _paymentMethod = PaymentMethod.Cash;
    private bool _isBusy;
    private string? _statusMessage;

    public SalesViewModel(
        ISalesService salesService,
        IMedicinePickerService picker,
        IBillSearchService billSearch,
        ICurrentUserService currentUser,
        IDialogService dialog,
        IInvoicePrintService printService)
    {
        _salesService = salesService;
        _picker = picker;
        _billSearch = billSearch;
        _currentUser = currentUser;
        _dialog = dialog;
        _printService = printService;

        Cart.CollectionChanged += (_, _) => RecalculateTotals();

        RemoveLineCommand = new RelayCommand(p => RemoveLine(p as CartLineViewModel));
        SaveCommand = new AsyncRelayCommand(_ => SaveAsync(), _ => Cart.Any(l => !l.IsEmpty) && !IsBusy);
        NewBillCommand = new RelayCommand(_ => NewBill());
        SearchBillsCommand = new AsyncRelayCommand(_ => OpenBillSearchAsync(), _ => !IsBusy);

        EnsureTrailingEmptyRow();
        _ = InitializeBillsAsync();
    }

    /// <summary>Asks the view to focus the Item column on the given (or last empty) line.</summary>
    public event Action<CartLineViewModel?>? RequestItemFocus;

    /// <summary>Asks the view to focus the customer name field.</summary>
    public event Action? RequestCustomerFocus;

    public ObservableCollection<CartLineViewModel> Cart { get; } = new();
    public ObservableCollection<SaleListItemDto> BillHistory { get; } = new();

    public Array PaymentMethods => Enum.GetValues(typeof(PaymentMethod));

    public ICommand RemoveLineCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand NewBillCommand { get; }
    public ICommand SearchBillsCommand { get; }

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
        if (line is null || line.IsEmpty) return;
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
        set => SetProperty(ref _customerName, value);
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
        set => SetProperty(ref _paymentMethod, value);
    }

    private void RecalculateTotals()
    {
        var lines = Cart.Where(l => !l.IsEmpty).ToList();
        SubTotal = lines.Sum(l => l.Gross);
        DiscountTotal = lines.Sum(l => l.DiscountAmount);
        TaxableTotal = lines.Sum(l => l.Taxable);
        var tax = lines.Sum(l => l.TaxAmount);
        Cgst = Math.Round(tax / 2m, 2);
        Sgst = tax - Cgst;

        var net = TaxableTotal + tax;
        var rounded = Math.Round(net, 0, MidpointRounding.AwayFromZero);
        RoundOff = rounded - net;
        GrandTotal = rounded;

        OnPropertyChanged(nameof(ItemCount));
        OnPropertyChanged(nameof(HasItems));
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
            var bills = await _salesService.ListBillsAsync(branchId);
            var preview = await _salesService.PreviewNextInvoiceNumberAsync(branchId);
            var newBill = new SaleListItemDto(0, preview, DateTime.Now);

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
        }
        catch (Exception ex)
        {
            _dialog.ShowError($"Could not load bill history: {ex.Message}");
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
            StatusMessage = $"Editing invoice {result.Value.InvoiceNumber}.";
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

    /// <summary>Loads a bill from the dropdown as soon as it is highlighted (no Enter needed).</summary>
    public async Task LoadBillFromDropdownAsync(SaleListItemDto bill, bool focusGridAfterLoad = false)
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

        if (_lastDropdownSaleId == bill.SaleId)
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
                StatusMessage = $"Editing invoice {result.Value.InvoiceNumber}.";
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
        OnPropertyChanged(nameof(IsEditing));

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
        if (focusGridAfterLoad)
            RequestItemFocus?.Invoke(Cart.FirstOrDefault());
    }

    private async Task SaveAsync()
    {
        var lines = Cart.Where(l => !l.IsEmpty).ToList();
        if (lines.Count == 0) return;

        foreach (var line in lines)
        {
            if (line.Quantity > line.AvailableStock)
            {
                _dialog.ShowError($"Insufficient stock for {line.MedicineName} (batch {line.BatchNumber}).");
                return;
            }
        }

        var lineRequests = lines.Select(l => new SaleLineRequest
        {
            MedicineId = l.MedicineId,
            MedicineBatchId = l.BatchId,
            Quantity = l.Quantity,
            UnitPrice = l.UnitPrice,
            DiscountPercent = l.DiscountPercent
        }).ToList();

        var payments = new List<SalePaymentRequest>
        {
            new() { Method = PaymentMethod, Amount = GrandTotal }
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
                        Lines = lineRequests
                    }, _currentUser.CurrentUser?.BranchId);
                }

                if (result.IsFailure || result.Value is null)
                {
                    _dialog.ShowError(result.Error ?? "Could not save the invoice.");
                    return;
                }

                var receipt = result.Value;
                StatusMessage = $"Saved invoice {receipt.InvoiceNumber}.";

                if (_dialog.Confirm($"Invoice {receipt.InvoiceNumber} saved.\n\nPrint / preview it now?", "Invoice saved"))
                    _printService.ShowPreview(receipt);

                await RefreshBillHistoryCoreAsync(selectNewBill: true);
                ResetBillForm(clearStatus: false);
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

    private void NewBill()
    {
        _suppressBillSelection = true;
        SelectedBill = BillHistory.FirstOrDefault(b => b.SaleId == 0);
        _suppressBillSelection = false;
        ResetBillForm(clearStatus: true);
    }

    private void ResetBillForm(bool clearStatus)
    {
        foreach (var line in Cart)
            line.Changed -= RecalculateTotals;
        Cart.Clear();
        _editingSaleId = null;
        _lastDropdownSaleId = null;
        OnPropertyChanged(nameof(IsEditing));
        EnsureTrailingEmptyRow();
        CustomerName = string.Empty;
        CustomerMobile = null;
        CustomerAddress = null;
        DoctorName = null;
        PaymentMethod = PaymentMethod.Cash;
        RecalculateTotals();
        if (clearStatus) StatusMessage = null;
    }
}
