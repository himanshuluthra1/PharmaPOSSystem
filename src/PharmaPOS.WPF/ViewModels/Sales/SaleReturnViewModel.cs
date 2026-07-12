using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.SaleReturns;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Shared.Constants;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Sales;

public sealed class SaleReturnSearchCriteriaOption(SaleReturnSearchType type, string label)
{
    public SaleReturnSearchType Type { get; } = type;
    public string Label { get; } = label;
}

/// <summary>Sale return workflow: search invoice, select lines, inspect, refund, post.</summary>
public class SaleReturnViewModel : ObservableObject
{
    private readonly ISaleReturnService _saleReturnService;
    private readonly ICurrentUserService _currentUser;
    private readonly IDialogService _dialog;
    private readonly IInvoicePrintService _printService;
    private readonly int? _branchId;

    private SaleReturnSearchCriteriaOption _selectedCriteria;
    private string _searchText = string.Empty;
    private int _selectedSearchIndex = -1;
    private SaleReturnSearchResultDto? _selectedSearchResult;
    private string? _invoiceNumber;
    private string? _customerName;
    private string? _customerPhone;
    private DateTime? _invoiceDate;
    private string? _invoiceStatus;
    private int? _loadedSaleId;
    private RefundMode _refundMode = RefundMode.Cash;
    private string? _remarks;
    private bool _managerOverride;
    private string? _managerOverrideReason;
    private bool _isBusy;
    private string? _statusMessage;
    private CancellationTokenSource? _searchCts;

    public SaleReturnViewModel(
        ISaleReturnService saleReturnService,
        ICurrentUserService currentUser,
        IDialogService dialog,
        IInvoicePrintService printService)
    {
        _saleReturnService = saleReturnService;
        _currentUser = currentUser;
        _dialog = dialog;
        _printService = printService;
        _branchId = currentUser.CurrentUser?.BranchId;

        CanProcess = currentUser.HasAnyPermission(
            AppConstants.Permissions.SalesReturn, AppConstants.Permissions.SalesReturnManage);
        CanOverride = currentUser.HasAnyPermission(
            AppConstants.Permissions.SalesReturnOverride, AppConstants.Permissions.SalesReturnManage);
        CanHighValue = currentUser.HasAnyPermission(
            AppConstants.Permissions.SalesReturnHighValue, AppConstants.Permissions.SalesReturnManage);

        CriteriaOptions =
        [
            new(SaleReturnSearchType.InvoiceNumber, "Invoice Number"),
            new(SaleReturnSearchType.CustomerMobile, "Customer Mobile"),
            new(SaleReturnSearchType.CustomerName, "Customer Name"),
            new(SaleReturnSearchType.Barcode, "Barcode"),
            new(SaleReturnSearchType.QrCode, "Receipt QR / Invoice")
        ];
        _selectedCriteria = CriteriaOptions[0];

        SearchCommand = new AsyncRelayCommand(_ => SearchAsync(), _ => CanProcess && !IsBusy);
        LoadInvoiceCommand = new AsyncRelayCommand(_ => LoadSelectedInvoiceAsync(),
            _ => CanProcess && SelectedSearchResult is not null && !IsBusy);
        ReturnEntireInvoiceCommand = new RelayCommand(_ => ReturnEntireInvoice(), _ => HasLoadedInvoice && CanProcess);
        ProcessReturnCommand = new AsyncRelayCommand(_ => ProcessReturnAsync(), _ => CanSubmit);
        ClearCommand = new RelayCommand(_ => ClearInvoice(), _ => HasLoadedInvoice);
        PrintLastCommand = new RelayCommand(_ => PrintLastReceipt(), _ => _lastReceipt is not null);

        _ = InitializeAsync();
    }

    private SaleReturnReceiptDto? _lastReceipt;
    private SaleReturnPolicyDto _policy = new();

    public IReadOnlyList<SaleReturnSearchCriteriaOption> CriteriaOptions { get; }
    public ObservableCollection<SaleReturnSearchResultDto> SearchResults { get; } = new();
    public bool HasSearchResults => SearchResults.Count > 0;
    public ObservableCollection<SaleReturnLineViewModel> Lines { get; } = new();
    public ObservableCollection<ReturnReasonDto> ReturnReasons { get; } = new();

    public Array RefundModes => Enum.GetValues(typeof(RefundMode));

    public bool CanProcess { get; }
    public bool CanOverride { get; }
    public bool CanHighValue { get; }

    public SaleReturnSearchCriteriaOption SelectedCriteria
    {
        get => _selectedCriteria;
        set
        {
            if (!SetProperty(ref _selectedCriteria, value)) return;
            OnPropertyChanged(nameof(SearchHint));
        }
    }

    public string SearchHint => SelectedCriteria.Type switch
    {
        SaleReturnSearchType.InvoiceNumber => "Enter invoice number (full or partial)",
        SaleReturnSearchType.CustomerMobile => "Enter customer mobile number",
        SaleReturnSearchType.CustomerName => "Enter customer / patient name",
        SaleReturnSearchType.Barcode => "Scan or type medicine barcode",
        SaleReturnSearchType.QrCode => "Scan receipt QR or paste invoice number",
        _ => string.Empty
    };

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;
            _ = SearchAsync();
        }
    }

    public int SelectedSearchIndex
    {
        get => _selectedSearchIndex;
        set
        {
            if (!SetProperty(ref _selectedSearchIndex, value)) return;
            SelectedSearchResult = value >= 0 && value < SearchResults.Count
                ? SearchResults[value]
                : null;
        }
    }

    public SaleReturnSearchResultDto? SelectedSearchResult
    {
        get => _selectedSearchResult;
        private set
        {
            if (SetProperty(ref _selectedSearchResult, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool HasLoadedInvoice => _loadedSaleId is not null;

    public string? InvoiceNumber
    {
        get => _invoiceNumber;
        private set => SetProperty(ref _invoiceNumber, value);
    }

    public string? CustomerName
    {
        get => _customerName;
        private set => SetProperty(ref _customerName, value);
    }

    public string? CustomerPhone
    {
        get => _customerPhone;
        private set => SetProperty(ref _customerPhone, value);
    }

    public DateTime? InvoiceDate
    {
        get => _invoiceDate;
        private set
        {
            if (SetProperty(ref _invoiceDate, value))
                OnPropertyChanged(nameof(InvoiceDateLabel));
        }
    }

    public string InvoiceDateLabel => InvoiceDate?.ToString("dd/MM/yyyy hh:mm tt") ?? "—";

    public string? InvoiceStatus
    {
        get => _invoiceStatus;
        private set => SetProperty(ref _invoiceStatus, value);
    }

    public RefundMode RefundMode
    {
        get => _refundMode;
        set => SetProperty(ref _refundMode, value);
    }

    public string? Remarks
    {
        get => _remarks;
        set => SetProperty(ref _remarks, value);
    }

    public bool ManagerOverride
    {
        get => _managerOverride;
        set => SetProperty(ref _managerOverride, value);
    }

    public string? ManagerOverrideReason
    {
        get => _managerOverrideReason;
        set => SetProperty(ref _managerOverrideReason, value);
    }

    public decimal RefundSubTotal => Lines.Where(l => l.IsSelected && l.ReturnQuantity > 0)
        .Sum(l => l.ProportionalLineTotal - l.ProportionalTax);

    public decimal RefundDiscount => Lines.Where(l => l.IsSelected && l.ReturnQuantity > 0)
        .Sum(l => l.ProportionalDiscount);

    public decimal RefundTax => Lines.Where(l => l.IsSelected && l.ReturnQuantity > 0)
        .Sum(l => l.ProportionalTax);

    public decimal RefundGrandTotal => Lines.Where(l => l.IsSelected && l.ReturnQuantity > 0)
        .Sum(l => l.ProportionalLineTotal);

    public bool RequiresManagerApproval =>
        (_policy.AllowedDaysExceeded(InvoiceDate) && !ManagerOverride)
        || (RefundGrandTotal > _policy.HighValueThreshold && !CanHighValue && !ManagerOverride);

    public bool CanSubmit => CanProcess && HasLoadedInvoice && !IsBusy
        && Lines.Any(l => l.IsSelected && l.ReturnQuantity > 0)
        && Lines.Where(l => l.IsSelected && l.ReturnQuantity > 0).All(l => !l.HasValidationError)
        && (!ManagerOverride || !string.IsNullOrWhiteSpace(ManagerOverrideReason));

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public ICommand SearchCommand { get; }
    public ICommand LoadInvoiceCommand { get; }
    public ICommand ReturnEntireInvoiceCommand { get; }
    public ICommand ProcessReturnCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand PrintLastCommand { get; }

    public void OnLineChanged()
    {
        OnPropertyChanged(nameof(RefundSubTotal));
        OnPropertyChanged(nameof(RefundDiscount));
        OnPropertyChanged(nameof(RefundTax));
        OnPropertyChanged(nameof(RefundGrandTotal));
        OnPropertyChanged(nameof(CanSubmit));
        OnPropertyChanged(nameof(RequiresManagerApproval));
        CommandManager.InvalidateRequerySuggested();
    }

    public async Task LoadInvoiceByIdAsync(int saleId)
    {
        SelectedSearchIndex = -1;
        await LoadInvoiceCoreAsync(saleId);
    }

    private async Task InitializeAsync()
    {
        try
        {
            _policy = await _saleReturnService.GetPolicyAsync();
            var reasons = await _saleReturnService.ListReturnReasonsAsync();
            ReturnReasons.Clear();
            foreach (var r in reasons) ReturnReasons.Add(r);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private async Task SearchAsync()
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        var term = SearchText.Trim();
        if (term.Length < 1) return;

        try
        {
            await Task.Delay(250, token);
            var rows = await _saleReturnService.SearchSalesAsync(
                SelectedCriteria.Type, term, _branchId, token);
            if (token.IsCancellationRequested) return;

            SearchResults.Clear();
            foreach (var r in rows) SearchResults.Add(r);
            OnPropertyChanged(nameof(HasSearchResults));
            SelectedSearchIndex = rows.Count == 1 ? 0 : -1;
            StatusMessage = $"{rows.Count} invoice(s) found.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private async Task LoadSelectedInvoiceAsync()
    {
        if (SelectedSearchResult is null) return;
        await LoadInvoiceCoreAsync(SelectedSearchResult.SaleId);
    }

    private async Task LoadInvoiceCoreAsync(int saleId)
    {
        IsBusy = true;
        StatusMessage = "Loading invoice...";
        try
        {
            var result = await _saleReturnService.GetSaleForReturnAsync(saleId, _branchId);
            if (!result.IsSuccess)
            {
                _dialog.ShowError(result.Error ?? "Could not load invoice.");
                return;
            }

            var sale = result.Value!;
            _loadedSaleId = sale.SaleId;
            InvoiceNumber = sale.InvoiceNumber;
            CustomerName = sale.CustomerName;
            CustomerPhone = sale.CustomerPhone;
            InvoiceDate = sale.InvoiceDate;
            InvoiceStatus = sale.Status switch
            {
                SaleStatus.Completed => "Completed",
                SaleStatus.PartiallyReturned => "Partially Returned",
                SaleStatus.Returned => "Fully Returned",
                _ => sale.Status.ToString()
            };

            Lines.Clear();
            foreach (var line in sale.Lines.Where(l => l.AvailableReturnQuantity > 0))
            {
                var vm = new SaleReturnLineViewModel(line, ReturnReasons);
                vm.PropertyChanged += (_, _) => OnLineChanged();
                Lines.Add(vm);
            }

            OnPropertyChanged(nameof(HasLoadedInvoice));
            OnLineChanged();
            StatusMessage = $"Loaded {sale.InvoiceNumber} — {Lines.Count} returnable line(s).";
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

    private void ReturnEntireInvoice()
    {
        foreach (var line in Lines)
        {
            line.IsSelected = line.AvailableReturnQuantity > 0;
            if (line.IsSelected)
                line.ReturnQuantity = line.AvailableReturnQuantity;
        }
        OnLineChanged();
    }

    private async Task ProcessReturnAsync()
    {
        if (_loadedSaleId is not int saleId) return;

        var selected = Lines.Where(l => l.IsSelected && l.ReturnQuantity > 0).ToList();
        if (selected.Count == 0)
        {
            _dialog.ShowInfo("Select at least one item to return.", "Validation");
            return;
        }

        if (selected.Any(l => l.HasValidationError))
        {
            _dialog.ShowInfo("Fix validation errors before processing the return.", "Validation");
            return;
        }

        foreach (var line in selected)
        {
            var reason = ReturnReasons.FirstOrDefault(r => r.Id == line.ReturnReasonId);
            if (reason?.RequiresRemarks == true && string.IsNullOrWhiteSpace(line.ReasonRemarks))
            {
                _dialog.ShowInfo($"Remarks required for: {reason.Name}", "Validation");
                return;
            }
        }

        if (RefundGrandTotal > _policy.HighValueThreshold && !CanHighValue && !ManagerOverride)
        {
            _dialog.ShowInfo(
                $"Return amount ({RefundGrandTotal:N2}) exceeds threshold. Manager approval required.",
                "Validation");
            return;
        }

        if (ManagerOverride && string.IsNullOrWhiteSpace(ManagerOverrideReason))
        {
            _dialog.ShowInfo("Enter manager override reason.", "Validation");
            return;
        }

        if (!_dialog.Confirm($"Process return for {RefundGrandTotal:N2}?")) return;

        IsBusy = true;
        StatusMessage = "Creating return...";
        try
        {
            var request = new CreateSaleReturnRequest
            {
                SaleId = saleId,
                ReturnEntireInvoice = selected.Count == Lines.Count
                    && selected.All(l => l.ReturnQuantity >= l.AvailableReturnQuantity),
                RefundMode = RefundMode,
                Remarks = Remarks,
                ManagerOverrideUsed = ManagerOverride,
                ManagerOverrideReason = ManagerOverrideReason,
                Lines = selected.Select(l =>
                {
                    var reason = ReturnReasons.FirstOrDefault(r => r.Id == l.ReturnReasonId);
                    return l.ToRequest(reason);
                }).ToList()
            };

            var result = await _saleReturnService.CreateReturnAsync(
                request, _branchId, _currentUser.CurrentUser?.FullName, CancellationToken.None);

            if (!result.IsSuccess)
            {
                _dialog.ShowError(result.Error ?? "Return failed.");
                return;
            }

            _lastReceipt = result.Value!;
            StatusMessage = $"Return {_lastReceipt.ReturnNumber} created.";
            _dialog.ShowInfo($"Return {_lastReceipt.ReturnNumber} processed successfully.");

            if (_dialog.Confirm("Print return receipt?"))
                _printService.ShowReturnPreview(_lastReceipt);

            ClearInvoice();
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

    private void PrintLastReceipt()
    {
        if (_lastReceipt is not null)
            _printService.ShowReturnPreview(_lastReceipt);
    }

    private void ClearInvoice()
    {
        _loadedSaleId = null;
        Lines.Clear();
        InvoiceNumber = null;
        CustomerName = null;
        CustomerPhone = null;
        InvoiceDate = null;
        InvoiceStatus = null;
        OnPropertyChanged(nameof(HasLoadedInvoice));
        OnLineChanged();
    }
}

internal static class SaleReturnPolicyExtensions
{
    public static bool AllowedDaysExceeded(this SaleReturnPolicyDto policy, DateTime? invoiceDate)
    {
        if (invoiceDate is null) return false;
        return (DateTime.Today - invoiceDate.Value.Date).TotalDays > policy.AllowedDays;
    }
}
