using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.ExpiryReturns;
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

    private HorizonOption _selectedHorizon;
    private SupplierFilterOption _selectedSupplier = SupplierFilterOption.All;
    private bool _isBusy;
    private string? _statusMessage;
    private string? _remarks;
    private bool _awaitingCreditOnly = true;
    private ExpiryClaimListRowDto? _selectedClaim;
    private string _creditNoteNumber = string.Empty;
    private DateTime? _creditNoteDate = DateTime.Today;
    private decimal? _creditNoteAmount;
    private ExpiryClaimDetailDto? _claimDetail;

    public ExpiryReturnViewModel(
        IExpiryReturnService service,
        ICurrentUserService currentUser,
        IDialogService dialog)
    {
        _service = service;
        _currentUser = currentUser;
        _dialog = dialog;
        _branchId = currentUser.CurrentUser?.BranchId;
        CanManage = currentUser.HasAnyPermission(
            AppConstants.Permissions.PurchaseReturn, AppConstants.Permissions.PurchaseReturnManage);

        HorizonOptions =
        [
            new(0, "Expired only"),
            new(30, "Next 30 days"),
            new(90, "Next 90 days"),
            new(180, "Next 6 months")
        ];
        _selectedHorizon = HorizonOptions[2];

        RefreshEligibleCommand = new AsyncRelayCommand(_ => RefreshEligibleAsync(), _ => !IsBusy);
        SubmitClaimCommand = new AsyncRelayCommand(_ => SubmitAsync(), _ => CanManage && !IsBusy && Batches.Any(b => b.ClaimQuantity > 0));
        RefreshClaimsCommand = new AsyncRelayCommand(_ => RefreshClaimsAsync(), _ => !IsBusy);
        AttachCreditNoteCommand = new AsyncRelayCommand(_ => AttachCreditNoteAsync(), _ => CanManage && !IsBusy && SelectedClaim is not null);
        SelectAllCommand = new RelayCommand(_ => SetAllClaims(true), _ => Batches.Count > 0);
        ClearQtyCommand = new RelayCommand(_ => SetAllClaims(false), _ => Batches.Count > 0);

        _ = RefreshEligibleAsync();
        _ = RefreshClaimsAsync();
    }

    public bool CanManage { get; }

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

    public SupplierFilterOption SelectedSupplier
    {
        get => _selectedSupplier;
        set
        {
            if (!SetProperty(ref _selectedSupplier, value)) return;
            _ = RefreshEligibleAsync();
        }
    }

    public ObservableCollection<ExpiryClaimLineViewModel> Batches { get; } = new();
    public ObservableCollection<ExpiryClaimListRowDto> Claims { get; } = new();

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
            _ = LoadClaimDetailAsync();
        }
    }

    public bool HasSelectedClaim => SelectedClaim is not null;

    public ExpiryClaimDetailDto? ClaimDetail
    {
        get => _claimDetail;
        private set => SetProperty(ref _claimDetail, value);
    }

    public string CreditNoteNumber
    {
        get => _creditNoteNumber;
        set => SetProperty(ref _creditNoteNumber, value);
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

    public decimal ClaimTotal => Batches.Where(b => b.ClaimQuantity > 0).Sum(b => b.ClaimValue);
    public int ClaimLineCount => Batches.Count(b => b.ClaimQuantity > 0);

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
    public ICommand SelectAllCommand { get; }
    public ICommand ClearQtyCommand { get; }

    private async Task RefreshEligibleAsync()
    {
        IsBusy = true;
        try
        {
            if (AllSuppliers.Count == 0)
                AllSuppliers = await _service.ListSuppliersAsync();

            var supplierId = SelectedSupplier.SupplierId;
            var rows = await _service.ListEligibleBatchesAsync(SelectedHorizon.Days, supplierId, _branchId);
            var keepSupplier = SelectedSupplier.SupplierId;

            var named = rows
                .Where(r => r.SupplierId is > 0 && !string.IsNullOrWhiteSpace(r.SupplierName))
                .GroupBy(r => r.SupplierId!.Value)
                .OrderBy(g => g.First().SupplierName)
                .Select(g => new SupplierFilterOption(g.Key, g.First().SupplierName!))
                .ToList();

            // When filtered, keep the current supplier in the combo even if the row set is small.
            SupplierOptions = [SupplierFilterOption.All, SupplierFilterOption.Unmapped, .. named];
            if (keepSupplier is > 0 && SupplierOptions.All(s => s.SupplierId != keepSupplier))
            {
                var extra = AllSuppliers.FirstOrDefault(s => s.Id == keepSupplier);
                if (extra is not null)
                    SupplierOptions.Insert(2, new SupplierFilterOption(extra.Id, extra.Name));
            }
            OnPropertyChanged(nameof(SupplierOptions));
            OnPropertyChanged(nameof(AllSuppliers));
            _selectedSupplier = SupplierOptions.FirstOrDefault(s => s.SupplierId == keepSupplier)
                                ?? SupplierFilterOption.All;
            OnPropertyChanged(nameof(SelectedSupplier));

            Batches.Clear();
            foreach (var r in rows)
            {
                var line = new ExpiryClaimLineViewModel(r, AllSuppliers);
                line.PropertyChanged += (_, _) =>
                {
                    OnPropertyChanged(nameof(ClaimTotal));
                    OnPropertyChanged(nameof(ClaimLineCount));
                    CommandManager.InvalidateRequerySuggested();
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

        var selected = Batches.Where(b => b.ClaimQuantity > 0).ToList();
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
        if (SelectedClaim is null) return;
        var result = await _service.GetClaimAsync(SelectedClaim.Id, _branchId);
        if (result.IsSuccess)
        {
            ClaimDetail = result.Value;
            CreditNoteNumber = result.Value?.CreditNoteNumber ?? "";
            CreditNoteDate = result.Value?.CreditNoteDate ?? DateTime.Today;
            CreditNoteAmount = result.Value?.CreditNoteAmount ?? result.Value?.ExpectedCreditAmount;
        }
    }

    private async Task AttachCreditNoteAsync()
    {
        if (SelectedClaim is null) return;
        var result = await _service.AttachCreditNoteAsync(
            new AttachExpiryCreditNoteRequest
            {
                ClaimId = SelectedClaim.Id,
                CreditNoteNumber = CreditNoteNumber,
                CreditNoteDate = CreditNoteDate,
                CreditNoteAmount = CreditNoteAmount
            },
            _currentUser.CurrentUser?.FullName);

        if (result.IsFailure)
        {
            _dialog.ShowError(result.Error ?? "Could not save credit note.");
            return;
        }

        _dialog.ShowInfo("Supplier credit note recorded.");
        await RefreshClaimsAsync();
        await LoadClaimDetailAsync();
    }

    private void SetAllClaims(bool fill)
    {
        foreach (var b in Batches)
            b.ClaimQuantity = fill ? b.Source.StockQuantity : 0;
    }
}

public sealed class ExpiryClaimLineViewModel : ObservableObject
{
    private decimal _claimQuantity;
    private int? _supplierId;

    public ExpiryClaimLineViewModel(ExpiryEligibleBatchDto source, IReadOnlyList<ExpirySupplierOptionDto> suppliers)
    {
        Source = source;
        Suppliers = suppliers;
        _claimQuantity = source.StockQuantity;
        _supplierId = source.SupplierId;
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
