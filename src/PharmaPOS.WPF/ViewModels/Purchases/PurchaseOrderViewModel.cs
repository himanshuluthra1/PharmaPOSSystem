using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Purchases;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Shared.Constants;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Purchases;

public class PurchaseOrderViewModel : ObservableObject
{
    private readonly IPurchaseOrderService _orders;
    private readonly IPurchaseService _purchases;
    private readonly IMedicinePickerService _medicinePicker;
    private readonly ICurrentUserService _currentUser;
    private readonly IDialogService _dialog;
    private readonly INavigationService _navigation;
    private readonly IPurchaseOrderReceiveBridge _receiveBridge;

    private bool _isBusy;
    private string? _statusMessage;
    private PurchaseOrderListItemDto? _selectedListItem;
    private int? _editingId;
    private PurchaseStatus _status = PurchaseStatus.Draft;
    private string _orderNumber = string.Empty;
    private DateTime _orderDate = DateTime.Today;
    private DateTime? _expectedDate;
    private string? _remarks;
    private string _supplierSearchText = string.Empty;
    private SupplierLookupDto? _selectedSupplier;
    private int _supplierSuggestionIndex = -1;
    private bool _suppressSupplierSearch;
    private decimal _totalAmount;

    public PurchaseOrderViewModel(
        IPurchaseOrderService orders,
        IPurchaseService purchases,
        IMedicinePickerService medicinePicker,
        ICurrentUserService currentUser,
        IDialogService dialog,
        INavigationService navigation,
        IPurchaseOrderReceiveBridge receiveBridge)
    {
        _orders = orders;
        _purchases = purchases;
        _medicinePicker = medicinePicker;
        _currentUser = currentUser;
        _dialog = dialog;
        _navigation = navigation;
        _receiveBridge = receiveBridge;

        CanCreate = currentUser.HasAnyPermission(
            AppConstants.Permissions.PurchaseCreate, AppConstants.Permissions.PurchaseManage);
        CanManage = currentUser.HasAnyPermission(AppConstants.Permissions.PurchaseManage)
                    || CanCreate;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        NewCommand = new RelayCommand(_ => StartNew(), _ => CanCreate && !IsBusy);
        SaveDraftCommand = new AsyncRelayCommand(SaveDraftAsync, () => CanCreate && !IsBusy && IsEditable);
        ConfirmCommand = new AsyncRelayCommand(ConfirmAsync, () => CanCreate && !IsBusy && Status == PurchaseStatus.Draft && _editingId.HasValue);
        CancelCommand = new AsyncRelayCommand(CancelAsync, () => CanManage && !IsBusy && CanCancelSelected);
        GenerateReorderCommand = new AsyncRelayCommand(GenerateReorderAsync, () => CanCreate && !IsBusy);
        GenerateFromShortageCommand = new AsyncRelayCommand(GenerateFromShortageAsync, () => CanCreate && !IsBusy);
        ReceiveCommand = new AsyncRelayCommand(ReceiveAsync, () => CanCreate && !IsBusy && CanReceiveSelected);
        AddMedicineCommand = new AsyncRelayCommand(AddMedicineAsync, () => CanCreate && !IsBusy && IsEditable);
        RemoveLineCommand = new RelayCommand(p =>
        {
            if (p is PurchaseOrderLineRow row && IsEditable)
                Lines.Remove(row);
            RecalcTotal();
        }, _ => IsEditable);
        ClearSupplierCommand = new RelayCommand(_ => ClearSupplier(), _ => IsEditable);

        _ = RefreshAsync();
    }

    public ObservableCollection<PurchaseOrderListItemDto> Orders { get; } = new();
    public ObservableCollection<PurchaseOrderLineRow> Lines { get; } = new();
    public ObservableCollection<SupplierLookupDto> SupplierResults { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand NewCommand { get; }
    public ICommand SaveDraftCommand { get; }
    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand GenerateReorderCommand { get; }
    public ICommand GenerateFromShortageCommand { get; }
    public ICommand ReceiveCommand { get; }
    public ICommand AddMedicineCommand { get; }
    public ICommand RemoveLineCommand { get; }
    public ICommand ClearSupplierCommand { get; }

    public bool CanCreate { get; }
    public bool CanManage { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsEditable));
                OnPropertyChanged(nameof(IsLinesReadOnly));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public PurchaseOrderListItemDto? SelectedListItem
    {
        get => _selectedListItem;
        set
        {
            if (!SetProperty(ref _selectedListItem, value)) return;
            if (value is not null)
                _ = LoadDetailAsync(value.Id);
        }
    }

    public bool IsEditable => Status == PurchaseStatus.Draft && CanCreate && !IsBusy;

    public bool IsLinesReadOnly => !IsEditable;

    public bool CanCancelSelected =>
        _editingId.HasValue && Status is PurchaseStatus.Draft or PurchaseStatus.Ordered;

    public bool CanReceiveSelected =>
        _editingId.HasValue && Status is PurchaseStatus.Ordered or PurchaseStatus.PartiallyReceived;

    public string OrderNumber
    {
        get => _orderNumber;
        private set => SetProperty(ref _orderNumber, value);
    }

    public PurchaseStatus Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(IsEditable));
                OnPropertyChanged(nameof(IsLinesReadOnly));
                OnPropertyChanged(nameof(CanCancelSelected));
                OnPropertyChanged(nameof(CanReceiveSelected));
                OnPropertyChanged(nameof(StatusDisplay));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string StatusDisplay => Status.ToString();

    public DateTime OrderDate
    {
        get => _orderDate;
        set => SetProperty(ref _orderDate, value);
    }

    public DateTime? ExpectedDate
    {
        get => _expectedDate;
        set => SetProperty(ref _expectedDate, value);
    }

    public string? Remarks
    {
        get => _remarks;
        set => SetProperty(ref _remarks, value);
    }

    public decimal TotalAmount
    {
        get => _totalAmount;
        private set => SetProperty(ref _totalAmount, value);
    }

    public string SupplierSearchText
    {
        get => _supplierSearchText;
        set
        {
            if (SetProperty(ref _supplierSearchText, value) && !_suppressSupplierSearch && IsEditable)
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

    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var list = await _orders.ListAsync(_currentUser.CurrentUser?.BranchId);
            Orders.Clear();
            foreach (var item in list)
                Orders.Add(item);
            StatusMessage = $"{Orders.Count} purchase order(s).";
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

    private void StartNew()
    {
        _editingId = null;
        _selectedListItem = null;
        OnPropertyChanged(nameof(SelectedListItem));
        OrderNumber = "(new)";
        Status = PurchaseStatus.Draft;
        OrderDate = DateTime.Today;
        ExpectedDate = null;
        Remarks = null;
        ClearSupplier();
        Lines.Clear();
        TotalAmount = 0;
        StatusMessage = "New draft purchase order.";
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task LoadDetailAsync(int id)
    {
        IsBusy = true;
        try
        {
            var result = await _orders.GetAsync(id, _currentUser.CurrentUser?.BranchId);
            if (result.IsFailure || result.Value is null)
            {
                _dialog.ShowError(result.Error ?? "Could not load purchase order.");
                return;
            }

            ApplyDetail(result.Value);
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

    private void ApplyDetail(PurchaseOrderDetailDto detail)
    {
        _editingId = detail.Id;
        OrderNumber = detail.OrderNumber;
        Status = detail.Status;
        OrderDate = detail.OrderDate;
        ExpectedDate = detail.ExpectedDate;
        Remarks = detail.Remarks;
        SelectedSupplier = new SupplierLookupDto(detail.SupplierId, detail.SupplierName, null, null, 0);
        Lines.Clear();
        foreach (var line in detail.Lines)
        {
            Lines.Add(new PurchaseOrderLineRow
            {
                MedicineId = line.MedicineId,
                MedicineName = line.MedicineName,
                GenericName = line.GenericName,
                Quantity = line.Quantity,
                ReceivedQuantity = line.ReceivedQuantity,
                EstimatedPrice = line.EstimatedPrice
            });
        }
        RecalcTotal();
        StatusMessage = $"{detail.OrderNumber} — {detail.Status}";
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task SearchSuppliersAsync(string term)
    {
        SupplierResults.Clear();
        SupplierSuggestionIndex = -1;
        OnPropertyChanged(nameof(ShowSupplierResults));
        if (string.IsNullOrWhiteSpace(term)) return;
        try
        {
            var results = await _purchases.SearchSuppliersAsync(term);
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

    private async Task AddMedicineAsync()
    {
        if (!IsEditable) return;
        var pick = await _medicinePicker.PickMedicineLookupAsync();
        if (pick is null) return;

        var medicine = await _purchases.GetMedicineAsync(pick.Id);
        if (medicine is null)
        {
            _dialog.ShowError("Medicine not found.");
            return;
        }

        var existing = Lines.FirstOrDefault(l => l.MedicineId == medicine.Id);
        if (existing is not null)
        {
            existing.Quantity += 1;
            RecalcTotal();
            StatusMessage = $"Increased qty for {medicine.Name}.";
            return;
        }

        Lines.Add(new PurchaseOrderLineRow
        {
            MedicineId = medicine.Id,
            MedicineName = medicine.Name,
            GenericName = medicine.GenericName,
            Quantity = 1,
            EstimatedPrice = medicine.PurchasePrice
        });
        RecalcTotal();
        StatusMessage = $"Added {medicine.Name}.";
    }

    private void RecalcTotal()
    {
        TotalAmount = Lines.Sum(l => Math.Round(l.Quantity * l.EstimatedPrice, 2));
        foreach (var line in Lines)
            line.NotifyTotals();
    }

    private async Task SaveDraftAsync()
    {
        if (SelectedSupplier is null)
        {
            _dialog.ShowError("Select a supplier.");
            return;
        }
        if (Lines.Count == 0 || Lines.Any(l => l.Quantity <= 0))
        {
            _dialog.ShowError("Add medicines with quantity greater than zero.");
            return;
        }

        IsBusy = true;
        try
        {
            var request = new SavePurchaseOrderRequest
            {
                Id = _editingId,
                SupplierId = SelectedSupplier.Id,
                OrderDate = OrderDate,
                ExpectedDate = ExpectedDate,
                Remarks = Remarks,
                Lines = Lines.Select(l => new PurchaseOrderLineRequest
                {
                    MedicineId = l.MedicineId,
                    Quantity = l.Quantity,
                    EstimatedPrice = l.EstimatedPrice
                }).ToList()
            };

            var result = await _orders.SaveDraftAsync(request, _currentUser.CurrentUser?.BranchId);
            if (result.IsFailure || result.Value is null)
            {
                _dialog.ShowError(result.Error ?? "Could not save draft.");
                return;
            }

            ApplyDetail(result.Value);
            await RefreshAsync();
            SelectedListItem = Orders.FirstOrDefault(o => o.Id == result.Value.Id);
            StatusMessage = $"Saved draft {result.Value.OrderNumber}.";
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

    private async Task ConfirmAsync()
    {
        if (_editingId is not int id) return;
        if (!_dialog.Confirm($"Confirm PO {OrderNumber} and mark it as Ordered?", "Confirm purchase order"))
            return;

        IsBusy = true;
        try
        {
            var result = await _orders.ConfirmAsync(id, _currentUser.CurrentUser?.BranchId);
            if (result.IsFailure || result.Value is null)
            {
                _dialog.ShowError(result.Error ?? "Could not confirm.");
                return;
            }

            ApplyDetail(result.Value);
            await RefreshAsync();
            SelectedListItem = Orders.FirstOrDefault(o => o.Id == id);
            StatusMessage = $"{result.Value.OrderNumber} confirmed (Ordered).";
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

    private async Task CancelAsync()
    {
        if (_editingId is not int id) return;
        if (!_dialog.Confirm($"Cancel PO {OrderNumber}?", "Cancel purchase order"))
            return;

        IsBusy = true;
        try
        {
            var result = await _orders.CancelAsync(id, _currentUser.CurrentUser?.BranchId);
            if (result.IsFailure)
            {
                _dialog.ShowError(result.Error ?? "Could not cancel.");
                return;
            }

            await RefreshAsync();
            await LoadDetailAsync(id);
            StatusMessage = $"{OrderNumber} cancelled.";
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

    private async Task GenerateReorderAsync()
    {
        if (!_dialog.Confirm(
                "Create draft purchase orders from:\n• medicines at/below reorder level\n• open shortage-book (lost sales)\n\nGrouped by last purchase supplier. Qty = max(reorder qty, shortage).",
                "Generate reorder"))
            return;

        await RunGenerateAsync(() => _orders.GenerateFromLowStockAsync(_currentUser.CurrentUser?.BranchId), "Auto reorder");
    }

    private async Task GenerateFromShortageAsync()
    {
        if (!_dialog.Confirm(
                "Create draft purchase orders from open shortage-book entries only (lost sales)?\n\nGrouped by last purchase supplier.",
                "Generate from shortage book"))
            return;

        await RunGenerateAsync(() => _orders.GenerateFromShortageBookAsync(_currentUser.CurrentUser?.BranchId), "Shortage book");
    }

    private async Task RunGenerateAsync(
        Func<Task<PharmaPOS.Shared.Results.Result<SuggestReorderResultDto>>> generate,
        string title)
    {
        IsBusy = true;
        try
        {
            var result = await generate();
            if (result.IsFailure || result.Value is null)
            {
                _dialog.ShowError(result.Error ?? "Could not generate purchase orders.");
                return;
            }

            var r = result.Value;
            await RefreshAsync();
            if (r.DraftOrdersCreated == 0)
            {
                _dialog.ShowInfo(
                    r.MedicinesSkippedNoSupplier > 0
                        ? $"No draft POs created. {r.MedicinesSkippedNoSupplier} medicine(s) have no prior supplier."
                        : "Nothing to order right now.",
                    title);
                StatusMessage = "No draft POs generated.";
                return;
            }

            _dialog.ShowInfo(
                $"Created {r.DraftOrdersCreated} draft PO(s) covering {r.MedicinesIncluded} medicine(s)." +
                (r.MedicinesSkippedNoSupplier > 0
                    ? $"\nSkipped {r.MedicinesSkippedNoSupplier} without a last supplier."
                    : string.Empty),
                title);
            StatusMessage = $"Generated {r.DraftOrdersCreated} draft PO(s).";
            if (r.CreatedOrders.Count > 0)
                SelectedListItem = Orders.FirstOrDefault(o => o.Id == r.CreatedOrders[0].Id);
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

    private async Task ReceiveAsync()
    {
        if (_editingId is not int id) return;

        IsBusy = true;
        try
        {
            var result = await _orders.GetReceiveDraftAsync(id, _currentUser.CurrentUser?.BranchId);
            if (result.IsFailure || result.Value is null)
            {
                _dialog.ShowError(result.Error ?? "Cannot receive against this PO.");
                return;
            }

            _receiveBridge.Queue(result.Value);
            _navigation.NavigateTo<PurchaseViewModel>();
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
}

public class PurchaseOrderLineRow : ObservableObject
{
    private decimal _quantity = 1;
    private decimal _estimatedPrice;

    public int MedicineId { get; set; }
    public string MedicineName { get; set; } = string.Empty;
    public string? GenericName { get; set; }
    public decimal ReceivedQuantity { get; set; }

    public decimal Quantity
    {
        get => _quantity;
        set
        {
            if (SetProperty(ref _quantity, value))
                NotifyTotals();
        }
    }

    public decimal EstimatedPrice
    {
        get => _estimatedPrice;
        set
        {
            if (SetProperty(ref _estimatedPrice, value))
                NotifyTotals();
        }
    }

    public decimal RemainingQuantity => Math.Max(0, Quantity - ReceivedQuantity);
    public decimal LineTotal => Math.Round(Quantity * EstimatedPrice, 2);

    public void NotifyTotals()
    {
        OnPropertyChanged(nameof(RemainingQuantity));
        OnPropertyChanged(nameof(LineTotal));
    }
}
