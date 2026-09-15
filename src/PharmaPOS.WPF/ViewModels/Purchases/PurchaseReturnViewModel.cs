using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.PurchaseReturns;
using PharmaPOS.Application.Features.Purchases;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Shared.Constants;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Purchases;

public class PurchaseReturnViewModel : ObservableObject
{
    private readonly IPurchaseReturnService _service;
    private readonly IPurchaseService _purchaseService;
    private readonly IMedicinePickerService _medicinePicker;
    private readonly ICurrentUserService _currentUser;
    private readonly IDialogService _dialog;
    private readonly IFinancialYearContext _financialYear;

    private string _searchText = string.Empty;
    private PurchaseReturnSearchResultDto? _selectedSearch;
    private PurchaseForReturnDto? _loaded;
    private string? _remarks;
    private PurchaseReturnSettlementMode _settlementMode = PurchaseReturnSettlementMode.SupplierCredit;
    private bool _isBusy;
    private string? _statusMessage;
    private bool _pendingReceiptOnly = true;
    private string _returnRecordsFilter = string.Empty;
    private PurchaseReturnListRowDto? _selectedReturn;
    private PurchaseReturnReceiptSettlementKind _receiptSettlementKind = PurchaseReturnReceiptSettlementKind.SupplierReceipt;
    private string _receiptNumber = string.Empty;
    private DateTime? _receiptDate = DateTime.Today;
    private PurchaseReturnSupplierBillOptionDto? _selectedSupplierBill;

    private string _directSupplierSearch = string.Empty;
    private SupplierLookupDto? _directSupplier;
    private bool _suppressSupplierSearch;
    private int _supplierSuggestionIndex = -1;
    private bool _showDirectSupplierResults;
    private string? _directRemarks;
    private PurchaseReturnSettlementMode _directSettlementMode = PurchaseReturnSettlementMode.SupplierCredit;

    public PurchaseReturnViewModel(
        IPurchaseReturnService service,
        IPurchaseService purchaseService,
        IMedicinePickerService medicinePicker,
        ICurrentUserService currentUser,
        IDialogService dialog,
        IFinancialYearContext financialYear)
    {
        _service = service;
        _purchaseService = purchaseService;
        _medicinePicker = medicinePicker;
        _currentUser = currentUser;
        _dialog = dialog;
        _financialYear = financialYear;

        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsBusy);
        LoadPurchaseCommand = new AsyncRelayCommand(LoadSelectedPurchaseAsync, () => !IsBusy && SelectedSearch is not null);
        ProcessReturnCommand = new AsyncRelayCommand(ProcessReturnAsync, () => !IsBusy && LoadedPurchase is not null && _financialYear.CanEditTransactions);
        RefreshReturnsCommand = new AsyncRelayCommand(RefreshReturnsAsync, () => !IsBusy);
        AttachReceiptCommand = new AsyncRelayCommand(
            AttachReceiptAsync,
            () => !IsBusy && _financialYear.CanEditTransactions && SelectedReturn is not null && CanSaveSettlement);
        SaveReturnLinesCommand = new AsyncRelayCommand(
            SaveReturnLinesAsync,
            () => !IsBusy && _financialYear.CanEditTransactions && CanEditReturnLines && ReturnLinesDirty);
        ClearCommand = new RelayCommand(ClearLoaded);

        AddDirectMedicineCommand = new AsyncRelayCommand(AddDirectMedicineAsync, () => !IsBusy && _financialYear.CanEditTransactions);
        RemoveDirectLineCommand = new RelayCommand(p =>
        {
            if (p is DirectReturnLineRow row) DirectLines.Remove(row);
        }, _ => _financialYear.CanEditTransactions);
        ProcessDirectReturnCommand = new AsyncRelayCommand(ProcessDirectReturnAsync, () => !IsBusy && _financialYear.CanEditTransactions && DirectSupplier is not null && DirectLines.Count > 0);
        ClearDirectCommand = new RelayCommand(ClearDirect);

        _ = InitializeAsync();
    }

    public ObservableCollection<PurchaseReturnSearchResultDto> SearchResults { get; } = new();
    public ObservableCollection<PurchaseReturnLineRow> Lines { get; } = new();
    public ObservableCollection<ReturnReasonOptionDto> Reasons { get; } = new();
    public ObservableCollection<PurchaseReturnListRowDto> ReturnRecords { get; } = new();

    public IEnumerable<PurchaseReturnListRowDto> FilteredReturnRecords
    {
        get
        {
            var term = ReturnRecordsFilter.Trim();
            if (string.IsNullOrWhiteSpace(term))
                return ReturnRecords;
            return ReturnRecords.Where(r =>
                r.ReturnNumber.Contains(term, StringComparison.OrdinalIgnoreCase)
                || r.SupplierName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || r.PurchaseInvoiceNumber.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (r.SupplierInvoiceNumber?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (r.SettlementReference?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (r.SupplierReturnReceiptNumber?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
        }
    }
    public ObservableCollection<SupplierLookupDto> DirectSupplierResults { get; } = new();
    public ObservableCollection<DirectReturnLineRow> DirectLines { get; } = new();

    public IReadOnlyList<PurchaseReturnSettlementMode> SettlementModes { get; } =
        Enum.GetValues<PurchaseReturnSettlementMode>();

    public bool HasSearchResults => SearchResults.Count > 0;

    public bool ShowDirectSupplierResults
    {
        get => _showDirectSupplierResults;
        set => SetProperty(ref _showDirectSupplierResults, value);
    }

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public PurchaseReturnSearchResultDto? SelectedSearch
    {
        get => _selectedSearch;
        set => SetProperty(ref _selectedSearch, value);
    }

    public PurchaseForReturnDto? LoadedPurchase
    {
        get => _loaded;
        private set
        {
            if (SetProperty(ref _loaded, value))
                OnPropertyChanged(nameof(HasLoadedPurchase));
        }
    }

    public bool HasLoadedPurchase => LoadedPurchase is not null;

    public string? Remarks
    {
        get => _remarks;
        set => SetProperty(ref _remarks, value);
    }

    public PurchaseReturnSettlementMode SettlementMode
    {
        get => _settlementMode;
        set => SetProperty(ref _settlementMode, value);
    }

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

    public bool PendingReceiptOnly
    {
        get => _pendingReceiptOnly;
        set
        {
            if (SetProperty(ref _pendingReceiptOnly, value))
                _ = RefreshReturnsAsync();
        }
    }

    public string ReturnRecordsFilter
    {
        get => _returnRecordsFilter;
        set
        {
            if (SetProperty(ref _returnRecordsFilter, value ?? string.Empty))
                OnPropertyChanged(nameof(FilteredReturnRecords));
        }
    }

    public PurchaseReturnListRowDto? SelectedReturn
    {
        get => _selectedReturn;
        set
        {
            if (!SetProperty(ref _selectedReturn, value)) return;
            OnPropertyChanged(nameof(HasSelectedReturn));
            OnPropertyChanged(nameof(SelectedReturnHint));
            if (value is not null)
            {
                _ = LoadSelectedReturnDetailsAsync(value.Id);
            }
            else
            {
                SelectedReturnLines.Clear();
                EditableReturnLines.Clear();
                CanEditReturnLines = false;
                OnPropertyChanged(nameof(CanEditReturnLines));
                OnPropertyChanged(nameof(IsReturnLinesReadOnly));
                OnPropertyChanged(nameof(HasEditableReturnLines));
                OnPropertyChanged(nameof(HasSelectedReturnLines));
                ReturnLinesDirty = false;
                SupplierBills.Clear();
                SelectedSupplierBill = null;
                ReceiptNumber = string.Empty;
            }
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool HasSelectedReturn => SelectedReturn is not null;

    public bool HasSelectedReturnLines => EditableReturnLines.Count > 0 || SelectedReturnLines.Count > 0;

    public string SelectedReturnHint => SelectedReturn is null
        ? "Select a return from the list below to see medicines and record the supplier receipt or purchase bill credit."
        : $"Selected {SelectedReturn.ReturnNumber} ({SelectedReturn.SupplierName}) — medicines shown below; record how credit was received.";

    public ObservableCollection<PurchaseReturnDetailLineDto> SelectedReturnLines { get; } = new();
    public ObservableCollection<PurchaseReturnEditableLineViewModel> EditableReturnLines { get; } = new();
    public ObservableCollection<PurchaseReturnSupplierBillOptionDto> SupplierBills { get; } = new();

    public bool CanEditReturnLines { get; private set; }
    public bool IsReturnLinesReadOnly => !CanEditReturnLines;
    public bool HasEditableReturnLines => EditableReturnLines.Count > 0;

    private bool _returnLinesDirty;
    public bool ReturnLinesDirty
    {
        get => _returnLinesDirty;
        private set
        {
            if (!SetProperty(ref _returnLinesDirty, value)) return;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public PurchaseReturnReceiptSettlementKind ReceiptSettlementKind
    {
        get => _receiptSettlementKind;
        set
        {
            if (!SetProperty(ref _receiptSettlementKind, value)) return;
            OnPropertyChanged(nameof(IsSupplierReceiptSettlement));
            OnPropertyChanged(nameof(IsPurchaseBillSettlement));
            OnPropertyChanged(nameof(ShowReceiptNumberFields));
            OnPropertyChanged(nameof(ShowPurchaseBillFields));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsSupplierReceiptSettlement
    {
        get => ReceiptSettlementKind == PurchaseReturnReceiptSettlementKind.SupplierReceipt;
        set
        {
            if (value) ReceiptSettlementKind = PurchaseReturnReceiptSettlementKind.SupplierReceipt;
        }
    }

    public bool IsPurchaseBillSettlement
    {
        get => ReceiptSettlementKind == PurchaseReturnReceiptSettlementKind.PurchaseBill;
        set
        {
            if (value) ReceiptSettlementKind = PurchaseReturnReceiptSettlementKind.PurchaseBill;
        }
    }

    public bool ShowReceiptNumberFields => IsSupplierReceiptSettlement;
    public bool ShowPurchaseBillFields => IsPurchaseBillSettlement;

    public string ReceiptNumber
    {
        get => _receiptNumber;
        set
        {
            if (!SetProperty(ref _receiptNumber, value)) return;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public DateTime? ReceiptDate
    {
        get => _receiptDate;
        set => SetProperty(ref _receiptDate, value);
    }

    public PurchaseReturnSupplierBillOptionDto? SelectedSupplierBill
    {
        get => _selectedSupplierBill;
        set
        {
            if (!SetProperty(ref _selectedSupplierBill, value)) return;
            if (value is not null && ReceiptSettlementKind == PurchaseReturnReceiptSettlementKind.PurchaseBill)
                ReceiptDate = value.InvoiceDate.Date;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private bool CanSaveSettlement =>
        ReceiptSettlementKind == PurchaseReturnReceiptSettlementKind.PurchaseBill
            ? SelectedSupplierBill is not null
            : !string.IsNullOrWhiteSpace(ReceiptNumber);

    public string DirectSupplierSearch
    {
        get => _directSupplierSearch;
        set
        {
            if (SetProperty(ref _directSupplierSearch, value) && !_suppressSupplierSearch)
                _ = SearchDirectSuppliersAsync(value);
        }
    }

    public int SupplierSuggestionIndex
    {
        get => _supplierSuggestionIndex;
        set => SetProperty(ref _supplierSuggestionIndex, value);
    }

    public SupplierLookupDto? DirectSupplier
    {
        get => _directSupplier;
        set
        {
            if (!SetProperty(ref _directSupplier, value)) return;
            OnPropertyChanged(nameof(DirectSupplierDisplay));
            if (value is not null)
            {
                _suppressSupplierSearch = true;
                DirectSupplierSearch = value.Name;
                _suppressSupplierSearch = false;
                DirectSupplierResults.Clear();
                SupplierSuggestionIndex = -1;
                ShowDirectSupplierResults = false;
            }
        }
    }

    public string DirectSupplierDisplay => DirectSupplier?.Name ?? "No supplier selected";

    public string? DirectRemarks
    {
        get => _directRemarks;
        set => SetProperty(ref _directRemarks, value);
    }

    public PurchaseReturnSettlementMode DirectSettlementMode
    {
        get => _directSettlementMode;
        set => SetProperty(ref _directSettlementMode, value);
    }

    public ICommand SearchCommand { get; }
    public ICommand LoadPurchaseCommand { get; }
    public ICommand ProcessReturnCommand { get; }
    public ICommand RefreshReturnsCommand { get; }
    public ICommand AttachReceiptCommand { get; }
    public ICommand SaveReturnLinesCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand AddDirectMedicineCommand { get; }
    public ICommand RemoveDirectLineCommand { get; }
    public ICommand ProcessDirectReturnCommand { get; }
    public ICommand ClearDirectCommand { get; }

    public void MoveSupplierSelection(int delta)
    {
        if (DirectSupplierResults.Count == 0)
        {
            SupplierSuggestionIndex = -1;
            return;
        }

        if (SupplierSuggestionIndex < 0)
            SupplierSuggestionIndex = 0;
        else
            SupplierSuggestionIndex = Math.Clamp(SupplierSuggestionIndex + delta, 0, DirectSupplierResults.Count - 1);
    }

    public void ConfirmSupplierSelection()
    {
        if (SupplierSuggestionIndex >= 0 && SupplierSuggestionIndex < DirectSupplierResults.Count)
            DirectSupplier = DirectSupplierResults[SupplierSuggestionIndex];
    }

    public void DismissSupplierSuggestions()
    {
        DirectSupplierResults.Clear();
        SupplierSuggestionIndex = -1;
        ShowDirectSupplierResults = false;
    }

    private async Task InitializeAsync()
    {
        var reasons = await _service.ListReturnReasonsAsync();
        Reasons.Clear();
        foreach (var r in reasons) Reasons.Add(r);
        await RefreshReturnsAsync();
    }

    private async Task SearchAsync()
    {
        IsBusy = true;
        try
        {
            SearchResults.Clear();
            var branchId = _currentUser.CurrentUser?.BranchId;
            var rows = await _service.SearchPurchasesAsync(SearchText, branchId);
            foreach (var r in rows) SearchResults.Add(r);
            OnPropertyChanged(nameof(HasSearchResults));
            StatusMessage = rows.Count == 0 ? "No matching purchase bills." : $"Found {rows.Count} bill(s).";
        }
        finally { IsBusy = false; }
    }

    private async Task LoadSelectedPurchaseAsync()
    {
        if (SelectedSearch is null) return;
        IsBusy = true;
        try
        {
            var branchId = _currentUser.CurrentUser?.BranchId;
            var result = await _service.GetPurchaseForReturnAsync(SelectedSearch.PurchaseId, branchId);
            if (result.IsFailure)
            {
                _dialog.ShowError(result.Error ?? "Could not load purchase.");
                return;
            }

            LoadedPurchase = result.Value;
            Lines.Clear();
            var defaultReason = Reasons.FirstOrDefault()?.Id;
            foreach (var line in result.Value!.Lines)
                Lines.Add(new PurchaseReturnLineRow(line, defaultReason));
            StatusMessage = $"Loaded {result.Value.InvoiceNumber} — select quantities to return.";
        }
        finally { IsBusy = false; }
    }

    private async Task ProcessReturnAsync()
    {
        if (LoadedPurchase is null) return;
        if (!EnsureReturnPermission()) return;

        var selected = Lines.Where(l => l.IsSelected && (l.ReturnQuantity > 0 || l.ReturnFreeQuantity > 0)).ToList();
        if (selected.Count == 0)
        {
            _dialog.ShowInfo("Select at least one line and enter return quantity.", "Purchase return");
            return;
        }

        foreach (var line in selected)
        {
            if (line.ReturnQuantity > line.AvailableQty || line.ReturnFreeQuantity > line.AvailableFreeQty)
            {
                _dialog.ShowError($"Return qty too high for {line.MedicineName} / {line.BatchNumber}.");
                return;
            }
        }

        if (!_dialog.Confirm(
                $"Return {selected.Count} line(s) to supplier?\n\nStock will be reduced now. You can enter the supplier return receipt number later when it arrives.",
                "Confirm purchase return"))
            return;

        IsBusy = true;
        try
        {
            var request = new CreatePurchaseReturnRequest
            {
                PurchaseId = LoadedPurchase.PurchaseId,
                SettlementMode = SettlementMode,
                Remarks = Remarks,
                Lines = selected.Select(l => new CreatePurchaseReturnLineRequest
                {
                    PurchaseItemId = l.PurchaseItemId,
                    ReturnQuantity = l.ReturnQuantity,
                    ReturnFreeQuantity = l.ReturnFreeQuantity,
                    ReturnReasonId = l.ReturnReasonId,
                    ReasonRemarks = l.ReasonRemarks
                }).ToList()
            };

            var result = await _service.CreateReturnAsync(
                request, _currentUser.CurrentUser?.BranchId, _currentUser.CurrentUser?.FullName);
            if (result.IsFailure)
            {
                _dialog.ShowError(result.Error ?? "Could not create return.");
                return;
            }

            _dialog.ShowInfo(
                BuildReturnSavedMessage(result.Value!),
                "Purchase return");
            ClearLoaded();
            await RefreshReturnsAsync();
            StatusMessage = $"Created {result.Value.ReturnNumber}.";
        }
        finally { IsBusy = false; }
    }

    private static string BuildReturnSavedMessage(PurchaseReturnReceiptDto r)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"Return {r.ReturnNumber} saved for ₹{r.GrandTotal:N2}.");
        if (!r.IsDirectReturn && !string.IsNullOrWhiteSpace(r.PurchaseInvoiceNumber))
        {
            sb.Append($"\n\nBill {r.PurchaseInvoiceNumber} amount reduced by ₹{r.GrandTotal:N2}.");
            sb.Append($"\nBill balance due now: ₹{r.PurchaseBalanceDueAfter:N2}.");
        }
        if (r.RemainingSupplierCredit > 0)
            sb.Append($"\nRemaining supplier credit: ₹{r.RemainingSupplierCredit:N2} (can settle another purchase).");
        sb.Append("\n\nWhen the supplier sends the return receipt, enter its number under Return Records.");
        return sb.ToString();
    }

    private async Task SearchDirectSuppliersAsync(string term)
    {
        DirectSupplierResults.Clear();
        SupplierSuggestionIndex = -1;
        ShowDirectSupplierResults = false;
        if (string.IsNullOrWhiteSpace(term)) return;

        try
        {
            var results = await _purchaseService.SearchSuppliersAsync(term);
            foreach (var r in results) DirectSupplierResults.Add(r);
            SupplierSuggestionIndex = results.Count > 0 ? 0 : -1;
            ShowDirectSupplierResults = DirectSupplierResults.Count > 0;
        }
        catch { /* best-effort */ }
    }

    private async Task AddDirectMedicineAsync()
    {
        var pick = await _medicinePicker.PickMedicineAsync();
        if (pick is null) return;

        var batchResult = await _service.GetBatchForDirectReturnAsync(
            pick.BatchId, _currentUser.CurrentUser?.BranchId);
        if (batchResult.IsFailure)
        {
            _dialog.ShowError(batchResult.Error ?? "Could not load batch.");
            return;
        }

        var batch = batchResult.Value!;
        var existing = DirectLines.FirstOrDefault(l => l.MedicineBatchId == batch.MedicineBatchId);
        if (existing is not null)
        {
            existing.ReturnQuantity = Math.Min(existing.ReturnQuantity + 1, existing.AvailableQty);
            StatusMessage = $"Increased qty for {batch.MedicineName} / {batch.BatchNumber}.";
            return;
        }

        DirectLines.Add(new DirectReturnLineRow(batch, Reasons.FirstOrDefault()?.Id));
        StatusMessage = $"Added {batch.MedicineName} ({batch.BatchNumber}).";
    }

    private async Task ProcessDirectReturnAsync()
    {
        if (DirectSupplier is null)
        {
            _dialog.ShowInfo("Select a supplier first.", "Direct return");
            return;
        }
        if (!EnsureReturnPermission()) return;

        var lines = DirectLines.Where(l => l.ReturnQuantity > 0 || l.ReturnFreeQuantity > 0).ToList();
        if (lines.Count == 0)
        {
            _dialog.ShowInfo("Add medicines and enter return quantity.", "Direct return");
            return;
        }

        foreach (var line in lines)
        {
            if (line.ReturnQuantity + line.ReturnFreeQuantity > line.AvailableQty)
            {
                _dialog.ShowError($"Return qty too high for {line.MedicineName} / {line.BatchNumber}.");
                return;
            }
        }

        if (!_dialog.Confirm(
                $"Return {lines.Count} medicine line(s) to {DirectSupplier.Name} without a purchase bill?\n\nStock will be reduced now. Enter the supplier receipt number later under Return Records.",
                "Confirm direct return"))
            return;

        IsBusy = true;
        try
        {
            var request = new CreateDirectPurchaseReturnRequest
            {
                SupplierId = DirectSupplier.Id,
                SettlementMode = DirectSettlementMode,
                Remarks = DirectRemarks,
                Lines = lines.Select(l => new CreateDirectPurchaseReturnLineRequest
                {
                    MedicineBatchId = l.MedicineBatchId,
                    ReturnQuantity = l.ReturnQuantity,
                    ReturnFreeQuantity = l.ReturnFreeQuantity,
                    PurchasePrice = l.PurchasePrice,
                    DiscountPercent = l.DiscountPercent,
                    GstPercent = l.GstPercent,
                    ReturnReasonId = l.ReturnReasonId,
                    ReasonRemarks = l.ReasonRemarks
                }).ToList()
            };

            var result = await _service.CreateDirectReturnAsync(
                request, _currentUser.CurrentUser?.BranchId, _currentUser.CurrentUser?.FullName);
            if (result.IsFailure)
            {
                _dialog.ShowError(result.Error ?? "Could not create return.");
                return;
            }

            _dialog.ShowInfo(
                $"Direct return {result.Value!.ReturnNumber} saved for ₹{result.Value.GrandTotal:N2}.\n\nWhen the supplier sends the return receipt, enter its number under Return Records.",
                "Direct return");
            ClearDirect();
            await RefreshReturnsAsync();
            StatusMessage = $"Created {result.Value.ReturnNumber}.";
        }
        finally { IsBusy = false; }
    }

    private bool EnsureReturnPermission()
    {
        if (_currentUser.HasAnyPermission(
                AppConstants.Permissions.PurchaseReturn,
                AppConstants.Permissions.PurchaseReturnManage,
                AppConstants.Permissions.PurchaseManage))
            return true;

        _dialog.ShowError("You do not have permission to process purchase returns.");
        return false;
    }

    private async Task RefreshReturnsAsync()
    {
        IsBusy = true;
        try
        {
            var keepId = SelectedReturn?.Id;
            ReturnRecords.Clear();
            var rows = await _service.ListReturnsAsync(
                PendingReceiptOnly, _currentUser.CurrentUser?.BranchId);
            foreach (var r in rows) ReturnRecords.Add(r);
            OnPropertyChanged(nameof(FilteredReturnRecords));

            if (keepId is int id)
            {
                var match = ReturnRecords.FirstOrDefault(r => r.Id == id);
                if (!ReferenceEquals(SelectedReturn, match))
                    SelectedReturn = match;
            }
        }
        finally { IsBusy = false; }
    }

    private async Task LoadSelectedReturnDetailsAsync(int purchaseReturnId)
    {
        try
        {
            var result = await _service.GetReturnDetailsAsync(
                purchaseReturnId, _currentUser.CurrentUser?.BranchId);
            SelectedReturnLines.Clear();
            EditableReturnLines.Clear();
            SupplierBills.Clear();
            SelectedSupplierBill = null;
            ReturnLinesDirty = false;

            if (result.IsSuccess && result.Value is not null)
            {
                CanEditReturnLines = result.Value.CanEditLines && _financialYear.CanEditTransactions;
                OnPropertyChanged(nameof(CanEditReturnLines));
                OnPropertyChanged(nameof(IsReturnLinesReadOnly));

                foreach (var line in result.Value.Lines)
                {
                    SelectedReturnLines.Add(line);
                    var edit = new PurchaseReturnEditableLineViewModel(line, CanEditReturnLines);
                    edit.Changed += () => ReturnLinesDirty = true;
                    EditableReturnLines.Add(edit);
                }

                ReceiptSettlementKind = result.Value.ReceiptSettlementKind;
                ReceiptDate = result.Value.SupplierReturnReceiptDate ?? DateTime.Today;

                var bills = await _service.ListSupplierBillsAsync(
                    result.Value.SupplierId, _currentUser.CurrentUser?.BranchId);
                foreach (var b in bills)
                    SupplierBills.Add(b);

                if (result.Value.ReceiptSettlementKind == PurchaseReturnReceiptSettlementKind.PurchaseBill
                    && result.Value.SettledAgainstPurchaseId is int billId)
                {
                    SelectedSupplierBill = SupplierBills.FirstOrDefault(b => b.PurchaseId == billId);
                    ReceiptNumber = string.Empty;
                }
                else
                {
                    ReceiptNumber = result.Value.SupplierReturnReceiptNumber ?? string.Empty;
                }
            }
            else
            {
                CanEditReturnLines = false;
                OnPropertyChanged(nameof(CanEditReturnLines));
                OnPropertyChanged(nameof(IsReturnLinesReadOnly));
            }

            OnPropertyChanged(nameof(HasSelectedReturnLines));
            OnPropertyChanged(nameof(HasEditableReturnLines));
            CommandManager.InvalidateRequerySuggested();
        }
        catch
        {
            SelectedReturnLines.Clear();
            EditableReturnLines.Clear();
            OnPropertyChanged(nameof(HasSelectedReturnLines));
            OnPropertyChanged(nameof(HasEditableReturnLines));
        }
    }

    private async Task SaveReturnLinesAsync()
    {
        if (SelectedReturn is null || !CanEditReturnLines) return;

        IsBusy = true;
        try
        {
            var result = await _service.UpdateReturnLinesAsync(
                new UpdatePurchaseReturnLinesRequest
                {
                    PurchaseReturnId = SelectedReturn.Id,
                    Lines = EditableReturnLines.Select(l => new UpdatePurchaseReturnLineRequest
                    {
                        Id = l.Id,
                        BatchNumber = l.BatchNumber,
                        ExpiryDate = l.ExpiryDate,
                        ReturnedQuantity = l.ReturnedQuantity,
                        ReturnedFreeQuantity = l.ReturnedFreeQuantity,
                        PurchasePrice = l.PurchasePrice,
                        GstPercent = l.GstPercent,
                        RefundPercent = l.RefundPercent,
                        ReasonRemarks = l.ReasonRemarks
                    }).ToList()
                },
                _currentUser.CurrentUser?.FullName);

            if (result.IsFailure)
            {
                _dialog.ShowError(result.Error ?? "Could not save return lines.");
                return;
            }

            ReturnLinesDirty = false;
            StatusMessage = $"Return lines saved. Amount {result.Value?.GrandTotal:N2}.";
            await RefreshReturnsAsync();
            if (SelectedReturn is not null)
                await LoadSelectedReturnDetailsAsync(SelectedReturn.Id);
            else if (result.Value is not null)
            {
                var row = ReturnRecords.FirstOrDefault(r => r.Id == result.Value.Id);
                if (row is not null) SelectedReturn = row;
            }
        }
        finally { IsBusy = false; }
    }

    private async Task AttachReceiptAsync()
    {
        if (SelectedReturn is null)
        {
            _dialog.ShowInfo("Select a return from the list first.", "Return receipt");
            return;
        }

        if (ReceiptSettlementKind == PurchaseReturnReceiptSettlementKind.PurchaseBill
            && SelectedSupplierBill is { IsPaid: true }
            && !_dialog.Confirm(
                "This bill is already Paid. Do you want to continue?",
                "Paid purchase bill"))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _service.AttachSupplierReceiptAsync(
                new AttachPurchaseReturnReceiptRequest
                {
                    PurchaseReturnId = SelectedReturn.Id,
                    SettlementKind = ReceiptSettlementKind,
                    ReceiptNumber = ReceiptNumber,
                    SettledAgainstPurchaseId = SelectedSupplierBill?.PurchaseId,
                    ReceiptDate = ReceiptDate
                },
                _currentUser.CurrentUser?.FullName);
            if (result.IsFailure)
            {
                _dialog.ShowError(result.Error ?? "Could not save credit settlement.");
                return;
            }

            var msg = ReceiptSettlementKind == PurchaseReturnReceiptSettlementKind.PurchaseBill
                ? $"Credit recorded against purchase bill on {SelectedReturn.ReturnNumber}."
                : $"Receipt number saved on {SelectedReturn.ReturnNumber}.";
            StatusMessage = msg;
            _dialog.ShowInfo(msg, "Return credit");
            await RefreshReturnsAsync();
        }
        finally { IsBusy = false; }
    }

    private void ClearLoaded()
    {
        LoadedPurchase = null;
        Lines.Clear();
        Remarks = null;
        StatusMessage = null;
    }

    private void ClearDirect()
    {
        DirectLines.Clear();
        DirectRemarks = null;
        DirectSupplier = null;
        _suppressSupplierSearch = true;
        DirectSupplierSearch = string.Empty;
        _suppressSupplierSearch = false;
        DismissSupplierSuggestions();
        StatusMessage = null;
    }
}

public sealed class PurchaseReturnEditableLineViewModel : ObservableObject
{
    private string? _batchNumber;
    private DateTime? _expiryDate;
    private decimal _returnedQuantity;
    private decimal _returnedFreeQuantity;
    private decimal _purchasePrice;
    private decimal _gstPercent;
    private decimal _refundPercent;
    private decimal _lineTotal;
    private string? _reasonRemarks;
    private bool _suppress;

    public PurchaseReturnEditableLineViewModel(PurchaseReturnDetailLineDto source, bool canEdit)
    {
        Id = source.Id;
        MedicineName = source.MedicineName;
        CanEdit = canEdit;
        _batchNumber = source.BatchNumber;
        _expiryDate = source.ExpiryDate;
        _returnedQuantity = source.ReturnedQuantity;
        _returnedFreeQuantity = source.ReturnedFreeQuantity;
        _purchasePrice = source.PurchasePrice;
        DiscountPercent = source.DiscountPercent;
        _gstPercent = source.GstPercent;
        _refundPercent = source.RefundPercent <= 0 ? 100m : source.RefundPercent;
        _lineTotal = source.LineTotal;
        ReasonName = source.ReasonName;
        _reasonRemarks = source.ReasonRemarks ?? source.ReasonName;
    }

    public event Action? Changed;

    public int Id { get; }
    public string MedicineName { get; }
    public bool CanEdit { get; }
    public decimal DiscountPercent { get; }
    public string? ReasonName { get; }

    public string? BatchNumber
    {
        get => _batchNumber;
        set { if (SetProperty(ref _batchNumber, value)) NotifyChanged(); }
    }

    public DateTime? ExpiryDate
    {
        get => _expiryDate;
        set { if (SetProperty(ref _expiryDate, value)) NotifyChanged(); }
    }

    public decimal ReturnedQuantity
    {
        get => _returnedQuantity;
        set { if (SetProperty(ref _returnedQuantity, Math.Max(0, value))) Recalc(); }
    }

    public decimal ReturnedFreeQuantity
    {
        get => _returnedFreeQuantity;
        set { if (SetProperty(ref _returnedFreeQuantity, Math.Max(0, value))) NotifyChanged(); }
    }

    public decimal PurchasePrice
    {
        get => _purchasePrice;
        set { if (SetProperty(ref _purchasePrice, Math.Max(0, value))) Recalc(); }
    }

    public decimal GstPercent
    {
        get => _gstPercent;
        set { if (SetProperty(ref _gstPercent, Math.Max(0, value))) Recalc(); }
    }

    public decimal RefundPercent
    {
        get => _refundPercent;
        set { if (SetProperty(ref _refundPercent, Math.Clamp(value, 0m, 999m))) Recalc(); }
    }

    public decimal LineTotal
    {
        get => _lineTotal;
        private set => SetProperty(ref _lineTotal, value);
    }

    public string? ReasonRemarks
    {
        get => _reasonRemarks;
        set { if (SetProperty(ref _reasonRemarks, value)) NotifyChanged(); }
    }

    private void Recalc()
    {
        if (_suppress) return;
        var taxable = Math.Round(PurchasePrice * ReturnedQuantity * (1m - Math.Clamp(DiscountPercent, 0m, 100m) / 100m), 2);
        var tax = Math.Round(taxable * GstPercent / 100m, 2);
        LineTotal = Math.Round((taxable + tax) * RefundPercent / 100m, 2);
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        if (!_suppress) Changed?.Invoke();
    }
}

public class PurchaseReturnLineRow : ObservableObject
{
    private bool _isSelected;
    private decimal _returnQuantity;
    private decimal _returnFreeQuantity;
    private int? _returnReasonId;
    private string? _reasonRemarks;

    public PurchaseReturnLineRow(PurchaseReturnLineDto source, int? defaultReasonId)
    {
        PurchaseItemId = source.PurchaseItemId;
        MedicineId = source.MedicineId;
        MedicineName = source.MedicineName;
        BatchNumber = source.BatchNumber;
        ExpiryDate = source.ExpiryDate;
        Quantity = source.Quantity;
        FreeQuantity = source.FreeQuantity;
        AvailableQty = source.AvailableQty;
        AvailableFreeQty = source.AvailableFreeQty;
        StockOnHand = source.StockOnHand;
        PurchasePrice = source.PurchasePrice;
        GstPercent = source.GstPercent;
        LineTotal = source.LineTotal;
        _returnReasonId = defaultReasonId;
        _returnQuantity = source.AvailableQty;
        _isSelected = false;
    }

    public int PurchaseItemId { get; }
    public int MedicineId { get; }
    public string MedicineName { get; }
    public string BatchNumber { get; }
    public DateTime? ExpiryDate { get; }
    public decimal Quantity { get; }
    public decimal FreeQuantity { get; }
    public decimal AvailableQty { get; }
    public decimal AvailableFreeQty { get; }
    public decimal StockOnHand { get; }
    public decimal PurchasePrice { get; }
    public decimal GstPercent { get; }
    public decimal LineTotal { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public decimal ReturnQuantity
    {
        get => _returnQuantity;
        set => SetProperty(ref _returnQuantity, Math.Clamp(value, 0, AvailableQty));
    }

    public decimal ReturnFreeQuantity
    {
        get => _returnFreeQuantity;
        set => SetProperty(ref _returnFreeQuantity, Math.Clamp(value, 0, AvailableFreeQty));
    }

    public int? ReturnReasonId
    {
        get => _returnReasonId;
        set => SetProperty(ref _returnReasonId, value);
    }

    public string? ReasonRemarks
    {
        get => _reasonRemarks;
        set => SetProperty(ref _reasonRemarks, value);
    }
}

public class DirectReturnLineRow : ObservableObject
{
    private decimal _returnQuantity = 1;
    private decimal _returnFreeQuantity;
    private decimal _purchasePrice;
    private decimal _discountPercent;
    private decimal _gstPercent;
    private int? _returnReasonId;
    private string? _reasonRemarks;

    public DirectReturnLineRow(DirectReturnBatchDto source, int? defaultReasonId)
    {
        MedicineBatchId = source.MedicineBatchId;
        MedicineId = source.MedicineId;
        MedicineName = source.MedicineName;
        BatchNumber = source.BatchNumber;
        ExpiryDate = source.ExpiryDate;
        AvailableQty = source.QuantityAvailable;
        _purchasePrice = source.PurchasePrice;
        _gstPercent = source.GstPercent;
        _returnReasonId = defaultReasonId;
        _returnQuantity = Math.Min(1, source.QuantityAvailable);
    }

    public int MedicineBatchId { get; }
    public int MedicineId { get; }
    public string MedicineName { get; }
    public string BatchNumber { get; }
    public DateTime? ExpiryDate { get; }
    public decimal AvailableQty { get; }

    public decimal ReturnQuantity
    {
        get => _returnQuantity;
        set => SetProperty(ref _returnQuantity, Math.Clamp(value, 0, AvailableQty));
    }

    public decimal ReturnFreeQuantity
    {
        get => _returnFreeQuantity;
        set => SetProperty(ref _returnFreeQuantity, Math.Clamp(value, 0, Math.Max(0, AvailableQty - ReturnQuantity)));
    }

    public decimal PurchasePrice
    {
        get => _purchasePrice;
        set => SetProperty(ref _purchasePrice, Math.Max(0, value));
    }

    public decimal DiscountPercent
    {
        get => _discountPercent;
        set => SetProperty(ref _discountPercent, Math.Clamp(value, 0, 100));
    }

    public decimal GstPercent
    {
        get => _gstPercent;
        set => SetProperty(ref _gstPercent, Math.Clamp(value, 0, 100));
    }

    public int? ReturnReasonId
    {
        get => _returnReasonId;
        set => SetProperty(ref _returnReasonId, value);
    }

    public string? ReasonRemarks
    {
        get => _reasonRemarks;
        set => SetProperty(ref _reasonRemarks, value);
    }
}
