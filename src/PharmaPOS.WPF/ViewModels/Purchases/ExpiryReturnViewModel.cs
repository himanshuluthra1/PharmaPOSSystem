using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.ExpiryReturns;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Shared.Constants;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Purchases;

public sealed class ExpiryReturnViewModel : ObservableObject
{
    private readonly IExpiryReturnService _service;
    private readonly ICurrentUserService _currentUser;
    private readonly IDialogService _dialog;
    private readonly int? _branchId;
    private readonly IFinancialYearContext _financialYear;

    private HorizonOption _selectedHorizon;
    private int? _selectedSupplierId;
    private bool _suppressSupplierFilterRefresh;
    private bool _isBusy;
    private string? _statusMessage;
    private string? _remarks;
    private bool _awaitingCreditOnly = true;
    private ExpiryClaimListRowDto? _selectedClaim;
    private ExpiryCreditSettlementKind _settlementKind = ExpiryCreditSettlementKind.CreditNote;
    private string _creditNoteNumber = string.Empty;
    private DateTime? _creditNoteDate = DateTime.Today;
    private decimal? _creditNoteAmount;
    private ExpiryClaimDetailDto? _claimDetail;
    private ExpirySupplierBillOptionDto? _selectedSupplierBill;

    public ExpiryReturnViewModel(
        IExpiryReturnService service,
        ICurrentUserService currentUser,
        IDialogService dialog,
        IFinancialYearContext financialYear)
    {
        _service = service;
        _currentUser = currentUser;
        _dialog = dialog;
        _financialYear = financialYear;
        _branchId = currentUser.CurrentUser?.BranchId;
        var hasReturnPermission = currentUser.HasAnyPermission(
            AppConstants.Permissions.PurchaseReturn, AppConstants.Permissions.PurchaseReturnManage);
        CanManage = hasReturnPermission && financialYear.CanEditTransactions;
        ManageBlockedReason = !hasReturnPermission
            ? "You do not have purchase return permission to save credit notes."
            : !financialYear.CanEditTransactions
                ? "Active financial year is read-only. Switch to the current year to save credit notes."
                : null;

        HorizonOptions =
        [
            new(0, "Expired only"),
            new(30, "Next 30 days"),
            new(90, "Next 90 days"),
            new(180, "Next 6 months")
        ];
        _selectedHorizon = HorizonOptions[2];

        RefreshEligibleCommand = new AsyncRelayCommand(_ => RefreshEligibleAsync(), _ => !IsBusy);
        SubmitClaimCommand = new AsyncRelayCommand(_ => SubmitAsync(), _ => CanManage && !IsBusy && Batches.Any(b => b.IsSelected && b.ClaimQuantity > 0));
        RefreshClaimsCommand = new AsyncRelayCommand(_ => RefreshClaimsAsync(), _ => !IsBusy);
        AttachCreditNoteCommand = new AsyncRelayCommand(
            _ => AttachCreditNoteAsync(),
            _ => CanManage && !IsBusy && SelectedClaim is not null && CanSaveSettlement);
        SaveClaimLinesCommand = new AsyncRelayCommand(
            _ => SaveClaimLinesAsync(),
            _ => CanManage && !IsBusy && CanEditClaimLines && ClaimLinesDirty);
        SelectAllCommand = new RelayCommand(_ => SetAllClaims(true), _ => Batches.Count > 0 && CanManage);
        ClearQtyCommand = new RelayCommand(_ => SetAllClaims(false), _ => Batches.Count > 0 && CanManage);

        _ = RefreshEligibleAsync();
        _ = RefreshClaimsAsync();
    }

    public bool CanManage { get; }

    public string? ManageBlockedReason { get; }

    public bool HasManageBlockedReason => !string.IsNullOrWhiteSpace(ManageBlockedReason);

    public IReadOnlyList<HorizonOption> HorizonOptions { get; }

    public HorizonOption SelectedHorizon
    {
        get => _selectedHorizon;
        set
        {
            if (!SetProperty(ref _selectedHorizon, value)) return;
            _ = RefreshEligibleAsync();
        }
    }

    public List<ExpirySupplierOptionDto> AllSuppliers { get; private set; } = [];

    public List<SupplierFilterOption> SupplierOptions { get; private set; } =
        [SupplierFilterOption.All, SupplierFilterOption.Unmapped];

    /// <summary>null = all suppliers, -1 = unmapped only.</summary>
    public int? SelectedSupplierId
    {
        get => _selectedSupplierId;
        set
        {
            if (!SetProperty(ref _selectedSupplierId, value)) return;
            if (!_suppressSupplierFilterRefresh)
                _ = RefreshEligibleAsync();
        }
    }

    public ObservableCollection<ExpiryClaimLineViewModel> Batches { get; } = new();
    public ObservableCollection<ExpiryClaimListRowDto> Claims { get; } = new();
    public ObservableCollection<ExpirySupplierBillOptionDto> SupplierBills { get; } = new();
    public ObservableCollection<ExpiryClaimEditableLineViewModel> EditableClaimLines { get; } = new();

    private bool _canEditClaimLines;
    public bool CanEditClaimLines
    {
        get => _canEditClaimLines;
        private set
        {
            if (!SetProperty(ref _canEditClaimLines, value)) return;
            OnPropertyChanged(nameof(IsClaimLinesReadOnly));
        }
    }

    public bool IsClaimLinesReadOnly => !CanEditClaimLines;
    public bool HasEditableClaimLines => EditableClaimLines.Count > 0;

    private bool _claimLinesDirty;
    public bool ClaimLinesDirty
    {
        get => _claimLinesDirty;
        private set
        {
            if (!SetProperty(ref _claimLinesDirty, value)) return;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string? Remarks
    {
        get => _remarks;
        set => SetProperty(ref _remarks, value);
    }

    public bool AwaitingCreditOnly
    {
        get => _awaitingCreditOnly;
        set
        {
            if (!SetProperty(ref _awaitingCreditOnly, value)) return;
            _ = RefreshClaimsAsync();
        }
    }

    public ExpiryClaimListRowDto? SelectedClaim
    {
        get => _selectedClaim;
        set
        {
            if (!SetProperty(ref _selectedClaim, value)) return;
            OnPropertyChanged(nameof(HasSelectedClaim));
            CommandManager.InvalidateRequerySuggested();
            _ = LoadClaimDetailAsync();
        }
    }

    public bool HasSelectedClaim => SelectedClaim is not null;

    public ExpiryClaimDetailDto? ClaimDetail
    {
        get => _claimDetail;
        private set => SetProperty(ref _claimDetail, value);
    }

    public ExpiryCreditSettlementKind SettlementKind
    {
        get => _settlementKind;
        set
        {
            if (!SetProperty(ref _settlementKind, value)) return;
            OnPropertyChanged(nameof(IsCreditNoteSettlement));
            OnPropertyChanged(nameof(IsPurchaseBillSettlement));
            OnPropertyChanged(nameof(ShowCreditNoteFields));
            OnPropertyChanged(nameof(ShowPurchaseBillFields));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsCreditNoteSettlement
    {
        get => SettlementKind == ExpiryCreditSettlementKind.CreditNote;
        set
        {
            if (value) SettlementKind = ExpiryCreditSettlementKind.CreditNote;
        }
    }

    public bool IsPurchaseBillSettlement
    {
        get => SettlementKind == ExpiryCreditSettlementKind.PurchaseBill;
        set
        {
            if (value) SettlementKind = ExpiryCreditSettlementKind.PurchaseBill;
        }
    }

    public bool ShowCreditNoteFields => IsCreditNoteSettlement;
    public bool ShowPurchaseBillFields => IsPurchaseBillSettlement;

    public string CreditNoteNumber
    {
        get => _creditNoteNumber;
        set
        {
            if (!SetProperty(ref _creditNoteNumber, value)) return;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public DateTime? CreditNoteDate
    {
        get => _creditNoteDate;
        set => SetProperty(ref _creditNoteDate, value);
    }

    public decimal? CreditNoteAmount
    {
        get => _creditNoteAmount;
        set => SetProperty(ref _creditNoteAmount, value);
    }

    public ExpirySupplierBillOptionDto? SelectedSupplierBill
    {
        get => _selectedSupplierBill;
        set
        {
            if (!SetProperty(ref _selectedSupplierBill, value)) return;
            if (value is not null && SettlementKind == ExpiryCreditSettlementKind.PurchaseBill)
                CreditNoteDate = value.InvoiceDate.Date;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private bool CanSaveSettlement =>
        SettlementKind == ExpiryCreditSettlementKind.PurchaseBill
            ? SelectedSupplierBill is not null
            : !string.IsNullOrWhiteSpace(CreditNoteNumber);

    public decimal ClaimTotal => Batches.Where(b => b.IsSelected && b.ClaimQuantity > 0).Sum(b => b.ClaimValue);
    public int ClaimLineCount => Batches.Count(b => b.IsSelected && b.ClaimQuantity > 0);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public ICommand RefreshEligibleCommand { get; }
    public ICommand SubmitClaimCommand { get; }
    public ICommand RefreshClaimsCommand { get; }
    public ICommand AttachCreditNoteCommand { get; }
    public ICommand SaveClaimLinesCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand ClearQtyCommand { get; }

    private async Task RefreshEligibleAsync()
    {
        IsBusy = true;
        try
        {
            if (AllSuppliers.Count == 0)
                AllSuppliers = await _service.ListSuppliersAsync();

            var supplierId = SelectedSupplierId;
            var rows = await _service.ListEligibleBatchesAsync(SelectedHorizon.Days, supplierId, _branchId);
            var keepSupplier = SelectedSupplierId;

            var named = AllSuppliers
                .OrderBy(s => s.Name)
                .Select(s => new SupplierFilterOption(s.Id, s.Name))
                .ToList();

            // Replacing ItemsSource clears WPF ComboBox SelectedValue; restore without re-query.
            _suppressSupplierFilterRefresh = true;
            try
            {
                SupplierOptions = [SupplierFilterOption.All, SupplierFilterOption.Unmapped, .. named];
                OnPropertyChanged(nameof(SupplierOptions));

                _selectedSupplierId = keepSupplier is not null
                    && SupplierOptions.All(s => s.SupplierId != keepSupplier)
                    ? null
                    : keepSupplier;
                OnPropertyChanged(nameof(SelectedSupplierId));
            }
            finally
            {
                _suppressSupplierFilterRefresh = false;
            }

            Batches.Clear();
            foreach (var r in rows)
            {
                var line = new ExpiryClaimLineViewModel(r, AllSuppliers);
                line.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(ExpiryClaimLineViewModel.ClaimQuantity)
                        or nameof(ExpiryClaimLineViewModel.IsSelected))
                    {
                        OnPropertyChanged(nameof(ClaimTotal));
                        OnPropertyChanged(nameof(ClaimLineCount));
                        CommandManager.InvalidateRequerySuggested();
                    }
                };
                Batches.Add(line);
            }

            OnPropertyChanged(nameof(ClaimTotal));
            OnPropertyChanged(nameof(ClaimLineCount));
            var missing = Batches.Count(b => b.SupplierId is null or <= 0);
            StatusMessage = missing > 0
                ? $"{Batches.Count} eligible batch(es) · {missing} without a supplier — pick one in the Supplier column"
                : $"{Batches.Count} eligible batch(es)";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            _dialog.ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SubmitAsync()
    {
        if (!CanManage)
        {
            _dialog.ShowError("You do not have permission to process expiry returns.");
            return;
        }

        var selected = Batches.Where(b => b.IsSelected && b.ClaimQuantity > 0).ToList();
        if (selected.Count == 0)
        {
            _dialog.ShowError("Enter claim quantity on at least one batch.");
            return;
        }

        var missing = selected.Where(b => b.SupplierId is null or <= 0).ToList();
        if (missing.Count > 0)
        {
            _dialog.ShowError("Some claimed batches have no supplier. Choose the company in the Supplier column, then submit.");
            return;
        }

        var supplierIds = selected.Select(b => b.SupplierId!.Value).Distinct().ToList();
        if (supplierIds.Count > 1)
        {
            _dialog.ShowError("Claim one supplier at a time. Filter by supplier, then submit.");
            return;
        }

        var supplierName = selected[0].SupplierDisplay;
        if (!_dialog.Confirm(
                $"Return {selected.Count} batch(es) to {supplierName} for credit of {ClaimTotal:N2}?",
                "Confirm expiry-to-company claim"))
            return;

        IsBusy = true;
        try
        {
            var result = await _service.SubmitClaimAsync(
                new SubmitExpiryClaimRequest
                {
                    SupplierId = supplierIds[0],
                    Remarks = Remarks,
                    Lines = selected.Select(b => new SubmitExpiryClaimLineRequest
                    {
                        MedicineBatchId = b.Source.MedicineBatchId,
                        ClaimQuantity = b.ClaimQuantity,
                        SupplierId = b.SupplierId
                    }).ToList()
                },
                _branchId,
                _currentUser.CurrentUser?.FullName);

            if (result.IsFailure || result.Value is null)
            {
                _dialog.ShowError(result.Error ?? "Claim failed.");
                return;
            }

            _dialog.ShowInfo(
                $"Claim {result.Value.ClaimNumber} posted. Stock return {result.Value.ReturnNumber}. Expected credit {result.Value.ExpectedCreditAmount:N2}.");
            Remarks = null;
            await RefreshEligibleAsync();
            await RefreshClaimsAsync();
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

    private async Task RefreshClaimsAsync()
    {
        try
        {
            var rows = await _service.ListClaimsAsync(AwaitingCreditOnly, _branchId);
            Claims.Clear();
            foreach (var r in rows)
                Claims.Add(r);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private async Task LoadClaimDetailAsync()
    {
        ClaimDetail = null;
        SupplierBills.Clear();
        SelectedSupplierBill = null;
        EditableClaimLines.Clear();
        ClaimLinesDirty = false;
        CanEditClaimLines = false;
        if (SelectedClaim is null) return;

        var result = await _service.GetClaimAsync(SelectedClaim.Id, _branchId);
        if (!result.IsSuccess || result.Value is null) return;

        ClaimDetail = result.Value;
        CanEditClaimLines = result.Value.CanEditLines && CanManage;
        SettlementKind = result.Value.CreditSettlementKind;
        CreditNoteDate = result.Value.CreditNoteDate ?? DateTime.Today;
        CreditNoteAmount = result.Value.CreditNoteAmount ?? result.Value.ExpectedCreditAmount;

        foreach (var line in result.Value.Lines)
        {
            var edit = new ExpiryClaimEditableLineViewModel(line, CanEditClaimLines);
            edit.Changed += () => ClaimLinesDirty = true;
            EditableClaimLines.Add(edit);
        }
        OnPropertyChanged(nameof(HasEditableClaimLines));

        var bills = await _service.ListSupplierBillsAsync(result.Value.SupplierId, _branchId);
        foreach (var b in bills)
            SupplierBills.Add(b);

        if (result.Value.CreditSettlementKind == ExpiryCreditSettlementKind.PurchaseBill
            && result.Value.SettledAgainstPurchaseId is int billId)
        {
            SelectedSupplierBill = SupplierBills.FirstOrDefault(b => b.PurchaseId == billId);
            CreditNoteNumber = "";
        }
        else
        {
            CreditNoteNumber = result.Value.CreditNoteNumber ?? "";
        }

        CommandManager.InvalidateRequerySuggested();
    }

    private async Task SaveClaimLinesAsync()
    {
        if (SelectedClaim is null || !CanEditClaimLines) return;
        IsBusy = true;
        try
        {
            var result = await _service.UpdateClaimLinesAsync(
                new UpdateExpiryClaimLinesRequest
                {
                    ClaimId = SelectedClaim.Id,
                    Lines = EditableClaimLines.Select(l => new UpdateExpiryClaimLineRequest
                    {
                        Id = l.Id,
                        BatchNumber = l.BatchNumber,
                        ExpiryDate = l.ExpiryDate,
                        ClaimQuantity = l.ClaimQuantity,
                        PurchasePrice = l.PurchasePrice,
                        GstPercent = l.GstPercent,
                        RefundPercent = l.RefundPercent
                    }).ToList()
                },
                _currentUser.CurrentUser?.FullName);

            if (result.IsFailure)
            {
                _dialog.ShowError(result.Error ?? "Could not save claim lines.");
                return;
            }

            ClaimLinesDirty = false;
            StatusMessage = $"Claim lines saved. Expected credit {result.Value?.ExpectedCreditAmount:N2}.";
            await RefreshClaimsAsync();
            await LoadClaimDetailAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AttachCreditNoteAsync()
    {
        if (SelectedClaim is null) return;
        var result = await _service.AttachCreditNoteAsync(
            new AttachExpiryCreditNoteRequest
            {
                ClaimId = SelectedClaim.Id,
                SettlementKind = SettlementKind,
                CreditNoteNumber = CreditNoteNumber,
                SettledAgainstPurchaseId = SelectedSupplierBill?.PurchaseId,
                CreditNoteDate = CreditNoteDate,
                CreditNoteAmount = CreditNoteAmount
            },
            _currentUser.CurrentUser?.FullName);

        if (result.IsFailure)
        {
            _dialog.ShowError(result.Error ?? "Could not save credit settlement.");
            return;
        }

        _dialog.ShowInfo(
            SettlementKind == ExpiryCreditSettlementKind.PurchaseBill
                ? "Credit recorded against the selected purchase bill."
                : "Supplier credit note recorded.");
        await RefreshClaimsAsync();
        await LoadClaimDetailAsync();
    }

    private void SetAllClaims(bool fill)
    {
        foreach (var b in Batches)
            b.ClaimQuantity = fill ? b.Source.StockQuantity : 0;
    }
}

public sealed class ExpiryClaimEditableLineViewModel : ObservableObject
{
    private string _batchNumber;
    private DateTime? _expiryDate;
    private decimal _claimQuantity;
    private decimal _purchasePrice;
    private decimal _gstPercent;
    private decimal _refundPercent;
    private decimal _lineTotal;

    public ExpiryClaimEditableLineViewModel(ExpiryClaimDetailLineDto source, bool canEdit)
    {
        Id = source.Id;
        MedicineName = source.MedicineName;
        StockQuantity = source.StockQuantity;
        PurchaseInvoiceNumber = source.PurchaseInvoiceNumber;
        PurchaseId = source.PurchaseId;
        CanEdit = canEdit;
        _batchNumber = source.BatchNumber;
        _expiryDate = source.ExpiryDate;
        _claimQuantity = source.ClaimQuantity;
        _purchasePrice = source.PurchasePrice;
        _gstPercent = source.GstPercent;
        _refundPercent = source.RefundPercent <= 0 ? 100m : source.RefundPercent;
        _lineTotal = source.LineTotal;
    }

    public event Action? Changed;

    public int Id { get; }
    public string MedicineName { get; }
    public decimal StockQuantity { get; }
    public string? PurchaseInvoiceNumber { get; }
    public int? PurchaseId { get; }
    public bool CanEdit { get; }

    public string BatchNumber
    {
        get => _batchNumber;
        set { if (SetProperty(ref _batchNumber, value)) Changed?.Invoke(); }
    }

    public DateTime? ExpiryDate
    {
        get => _expiryDate;
        set { if (SetProperty(ref _expiryDate, value)) Changed?.Invoke(); }
    }

    public decimal ClaimQuantity
    {
        get => _claimQuantity;
        set { if (SetProperty(ref _claimQuantity, Math.Max(0, value))) Recalc(); }
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

    private void Recalc()
    {
        var taxable = Math.Round(PurchasePrice * ClaimQuantity, 2);
        var tax = Math.Round(taxable * GstPercent / 100m, 2);
        LineTotal = Math.Round((taxable + tax) * RefundPercent / 100m, 2);
        Changed?.Invoke();
    }
}

public sealed class ExpiryClaimLineViewModel : ObservableObject
{
    private decimal _claimQuantity;
    private int? _supplierId;
    private bool _isSelected = true;

    public ExpiryClaimLineViewModel(ExpiryEligibleBatchDto source, IReadOnlyList<ExpirySupplierOptionDto> suppliers)
    {
        Source = source;
        Suppliers = suppliers;
        _claimQuantity = source.StockQuantity;
        _supplierId = source.SupplierId;
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public ExpiryEligibleBatchDto Source { get; }
    public IReadOnlyList<ExpirySupplierOptionDto> Suppliers { get; }

    public int? SupplierId
    {
        get => _supplierId;
        set
        {
            if (SetProperty(ref _supplierId, value))
                OnPropertyChanged(nameof(SupplierDisplay));
        }
    }

    public string SupplierDisplay =>
        Suppliers.FirstOrDefault(s => s.Id == SupplierId)?.Name
        ?? Source.SupplierName
        ?? "Select supplier";

    public decimal ClaimQuantity
    {
        get => _claimQuantity;
        set
        {
            var qty = value < 0 ? 0 : Math.Min(value, Source.StockQuantity);
            if (SetProperty(ref _claimQuantity, qty))
                OnPropertyChanged(nameof(ClaimValue));
        }
    }

    public decimal ClaimValue =>
        Source.StockQuantity <= 0
            ? 0
            : Math.Round(Source.StockValue / Source.StockQuantity * ClaimQuantity, 2);
}

public sealed class HorizonOption(int days, string label)
{
    public int Days { get; } = days;
    public string Label { get; } = label;
    public override string ToString() => Label;
}

public sealed class SupplierFilterOption(int? supplierId, string label)
{
    public static SupplierFilterOption All { get; } = new(null, "All suppliers");
    public static SupplierFilterOption Unmapped { get; } = new(-1, "No supplier");
    public int? SupplierId { get; } = supplierId;
    public string Label { get; } = label;
    public override string ToString() => Label;
    public override bool Equals(object? obj) => obj is SupplierFilterOption o && o.SupplierId == SupplierId;
    public override int GetHashCode() => SupplierId.GetHashCode();
}
