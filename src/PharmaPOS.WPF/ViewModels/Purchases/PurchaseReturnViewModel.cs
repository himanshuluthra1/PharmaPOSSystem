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

    private string _searchText = string.Empty;
    private PurchaseReturnSearchResultDto? _selectedSearch;
    private PurchaseForReturnDto? _loaded;
    private string? _remarks;
    private PurchaseReturnSettlementMode _settlementMode = PurchaseReturnSettlementMode.SupplierCredit;
    private bool _isBusy;
    private string? _statusMessage;
    private bool _pendingReceiptOnly = true;
    private PurchaseReturnListRowDto? _selectedReturn;
    private string _receiptNumber = string.Empty;
    private DateTime? _receiptDate = DateTime.Today;

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
        IDialogService dialog)
    {
        _service = service;
        _purchaseService = purchaseService;
        _medicinePicker = medicinePicker;
        _currentUser = currentUser;
        _dialog = dialog;

        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsBusy);
        LoadPurchaseCommand = new AsyncRelayCommand(LoadSelectedPurchaseAsync, () => !IsBusy && SelectedSearch is not null);
        ProcessReturnCommand = new AsyncRelayCommand(ProcessReturnAsync, () => !IsBusy && LoadedPurchase is not null);
        RefreshReturnsCommand = new AsyncRelayCommand(RefreshReturnsAsync, () => !IsBusy);
        AttachReceiptCommand = new AsyncRelayCommand(AttachReceiptAsync, () => !IsBusy);
        ClearCommand = new RelayCommand(ClearLoaded);

        AddDirectMedicineCommand = new AsyncRelayCommand(AddDirectMedicineAsync, () => !IsBusy);
        RemoveDirectLineCommand = new RelayCommand(p =>
        {
            if (p is DirectReturnLineRow row) DirectLines.Remove(row);
        });
        ProcessDirectReturnCommand = new AsyncRelayCommand(ProcessDirectReturnAsync, () => !IsBusy && DirectSupplier is not null && DirectLines.Count > 0);
        ClearDirectCommand = new RelayCommand(ClearDirect);

        _ = InitializeAsync();
    }

    public ObservableCollection<PurchaseReturnSearchResultDto> SearchResults { get; } = new();
    public ObservableCollection<PurchaseReturnLineRow> Lines { get; } = new();
    public ObservableCollection<ReturnReasonOptionDto> Reasons { get; } = new();
    public ObservableCollection<PurchaseReturnListRowDto> ReturnRecords { get; } = new();
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
                ReceiptNumber = value.SupplierReturnReceiptNumber ?? string.Empty;
                ReceiptDate = value.SupplierReturnReceiptDate ?? DateTime.Today;
                _ = LoadSelectedReturnDetailsAsync(value.Id);
            }
            else
            {
                SelectedReturnLines.Clear();
                OnPropertyChanged(nameof(HasSelectedReturnLines));
            }
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool HasSelectedReturn => SelectedReturn is not null;

    public bool HasSelectedReturnLines => SelectedReturnLines.Count > 0;

    public string SelectedReturnHint => SelectedReturn is null
        ? "Select a return from the list below to see medicines and enter the supplier receipt number."
        : $"Selected {SelectedReturn.ReturnNumber} ({SelectedReturn.SupplierName}) — medicines shown below; enter receipt # here.";

    public ObservableCollection<PurchaseReturnDetailLineDto> SelectedReturnLines { get; } = new();

    public string ReceiptNumber
    {
        get => _receiptNumber;
        set => SetProperty(ref _receiptNumber, value);
    }

    public DateTime? ReceiptDate
    {
        get => _receiptDate;
        set => SetProperty(ref _receiptDate, value);
    }

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
                $"Return {result.Value!.ReturnNumber} saved for ₹{result.Value.GrandTotal:N2}.\n\nWhen the supplier sends the return receipt, enter its number under Return Records.",
                "Purchase return");
            ClearLoaded();
            await RefreshReturnsAsync();
            StatusMessage = $"Created {result.Value.ReturnNumber}.";
        }
        finally { IsBusy = false; }
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
            ReturnRecords.Clear();
            SelectedReturnLines.Clear();
            OnPropertyChanged(nameof(HasSelectedReturnLines));
            var rows = await _service.ListReturnsAsync(
                PendingReceiptOnly, _currentUser.CurrentUser?.BranchId);
            foreach (var r in rows) ReturnRecords.Add(r);
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
            if (result.IsSuccess && result.Value is not null)
            {
                foreach (var line in result.Value.Lines)
                    SelectedReturnLines.Add(line);
            }
            OnPropertyChanged(nameof(HasSelectedReturnLines));
        }
        catch
        {
            SelectedReturnLines.Clear();
            OnPropertyChanged(nameof(HasSelectedReturnLines));
        }
    }

    private async Task AttachReceiptAsync()
    {
        if (SelectedReturn is null)
        {
            _dialog.ShowInfo("Select a return from the list first.", "Return receipt");
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _service.AttachSupplierReceiptAsync(
                SelectedReturn.Id, ReceiptNumber, ReceiptDate, _currentUser.CurrentUser?.FullName);
            if (result.IsFailure)
            {
                _dialog.ShowError(result.Error ?? "Could not save receipt number.");
                return;
            }

            StatusMessage = $"Receipt {ReceiptNumber.Trim()} saved on {SelectedReturn.ReturnNumber}.";
            _dialog.ShowInfo($"Receipt number saved on {SelectedReturn.ReturnNumber}.", "Return receipt");
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
