using System.Collections.ObjectModel;
using PharmaPOS.Application.Features.Purchases;
using PharmaPOS.WPF.Mvvm;

namespace PharmaPOS.WPF.ViewModels.Purchases;

/// <summary>View model for the purchase supplier search popup.</summary>
public class PurchaseSearchViewModel : ObservableObject
{
    public const string AllSuppliersLabel = "All";

    private readonly IPurchaseService _purchaseService;
    private readonly int? _branchId;

    private string _supplierFilterText = AllSuppliersLabel;
    private int? _selectedSupplierId;
    private int _supplierSuggestionIndex = -1;
    private int _selectedBillIndex = -1;
    private string? _hint;
    private bool _suppressFilterSearch;
    private decimal _totalAmount;
    private decimal _totalBalance;
    private CancellationTokenSource? _searchCts;
    private readonly SemaphoreSlim _searchGate = new(1, 1);

    public PurchaseSearchViewModel(IPurchaseService purchaseService, int? branchId)
    {
        _purchaseService = purchaseService;
        _branchId = branchId;
        Hint = "Type supplier name or keep All to list every purchase invoice.";
    }

    public ObservableCollection<SupplierLookupDto> SupplierSuggestions { get; } = new();
    public ObservableCollection<PurchaseSupplierBillDto> Bills { get; } = new();
    public ObservableCollection<PurchaseInvoiceLineRowDto> InvoiceLines { get; } = new();

    public string SupplierFilterText
    {
        get => _supplierFilterText;
        set
        {
            if (SetProperty(ref _supplierFilterText, value) && !_suppressFilterSearch)
                _ = OnSupplierFilterChangedAsync(value);
        }
    }

    public int SupplierSuggestionIndex
    {
        get => _supplierSuggestionIndex;
        set => SetProperty(ref _supplierSuggestionIndex, value);
    }

    public int SelectedBillIndex
    {
        get => _selectedBillIndex;
        set
        {
            if (SetProperty(ref _selectedBillIndex, value))
            {
                OnPropertyChanged(nameof(SelectedBill));
                _ = LoadInvoiceLinesAsync();
            }
        }
    }

    public PurchaseSupplierBillDto? SelectedBill =>
        SelectedBillIndex >= 0 && SelectedBillIndex < Bills.Count ? Bills[SelectedBillIndex] : null;

    public string? Hint
    {
        get => _hint;
        private set => SetProperty(ref _hint, value);
    }

    public decimal TotalAmount
    {
        get => _totalAmount;
        private set => SetProperty(ref _totalAmount, value);
    }

    public decimal TotalBalance
    {
        get => _totalBalance;
        private set => SetProperty(ref _totalBalance, value);
    }

    public bool ShowSupplierSuggestions => SupplierSuggestions.Count > 0;

    public bool ShowInvoiceLines => InvoiceLines.Count > 0;

    public bool ShowBillTotals => Bills.Count > 0;

    public bool IsAllSuppliersFilter =>
        string.Equals(SupplierFilterText.Trim(), AllSuppliersLabel, StringComparison.OrdinalIgnoreCase);

    public void SelectSupplierSuggestion(SupplierLookupDto supplier)
    {
        _suppressFilterSearch = true;
        SupplierFilterText = supplier.Name;
        _suppressFilterSearch = false;
        _selectedSupplierId = supplier.Id;
        SupplierSuggestions.Clear();
        OnPropertyChanged(nameof(ShowSupplierSuggestions));
        _ = LoadBillsAsync(supplier.Id);
    }

    public void SelectAllSuppliers()
    {
        _suppressFilterSearch = true;
        SupplierFilterText = AllSuppliersLabel;
        _suppressFilterSearch = false;
        _selectedSupplierId = null;
        SupplierSuggestions.Clear();
        OnPropertyChanged(nameof(ShowSupplierSuggestions));
        _ = LoadBillsAsync(null);
    }

    public Task SelectAllSuppliersAsync() => LoadBillsAsync(null);

    public void MoveSupplierSuggestion(int delta)
    {
        if (SupplierSuggestions.Count == 0)
        {
            SupplierSuggestionIndex = -1;
            return;
        }

        if (SupplierSuggestionIndex < 0)
            SupplierSuggestionIndex = 0;
        else
            SupplierSuggestionIndex = Math.Clamp(SupplierSuggestionIndex + delta, 0, SupplierSuggestions.Count - 1);
    }

    public void ConfirmSupplierSuggestion()
    {
        if (SupplierSuggestionIndex >= 0 && SupplierSuggestionIndex < SupplierSuggestions.Count)
            SelectSupplierSuggestion(SupplierSuggestions[SupplierSuggestionIndex]);
        else if (IsAllSuppliersFilter)
            SelectAllSuppliers();
    }

    public void MoveBillSelection(int delta)
    {
        if (Bills.Count == 0)
        {
            SelectedBillIndex = -1;
            return;
        }

        if (SelectedBillIndex < 0)
            SelectedBillIndex = 0;
        else
            SelectedBillIndex = Math.Clamp(SelectedBillIndex + delta, 0, Bills.Count - 1);
    }

    private async Task OnSupplierFilterChangedAsync(string term)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        term = term.Trim();
        if (IsAllSuppliersFilter)
        {
            SupplierSuggestions.Clear();
            OnPropertyChanged(nameof(ShowSupplierSuggestions));
            _selectedSupplierId = null;
            await LoadBillsAsync(null, token);
            return;
        }

        ClearBills();
        Hint = "Searching suppliers...";
        try
        {
            await Task.Delay(250, token);
            if (token.IsCancellationRequested) return;

            await _searchGate.WaitAsync(token);
            try
            {
                if (token.IsCancellationRequested) return;
                var suppliers = await _purchaseService.SearchSuppliersAsync(term, token);
                if (token.IsCancellationRequested) return;

                SupplierSuggestions.Clear();
                foreach (var s in suppliers)
                    SupplierSuggestions.Add(s);
                SupplierSuggestionIndex = suppliers.Count > 0 ? 0 : -1;
                OnPropertyChanged(nameof(ShowSupplierSuggestions));
                Hint = suppliers.Count == 0
                    ? $"No suppliers found for \"{term}\"."
                    : "Select a supplier to list purchase invoices.";
            }
            finally
            {
                _searchGate.Release();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Hint = $"Search failed: {ex.Message}";
        }
    }

    private Task LoadBillsAsync(int? supplierId, CancellationToken token = default)
        => LoadBillsCoreAsync(supplierId, token);

    private async Task LoadBillsCoreAsync(int? supplierId, CancellationToken token)
    {
        _selectedSupplierId = supplierId;
        Hint = "Loading purchase invoices...";
        InvoiceLines.Clear();
        OnPropertyChanged(nameof(ShowInvoiceLines));

        try
        {
            await _searchGate.WaitAsync(token);
            try
            {
                if (token.IsCancellationRequested) return;
                var rows = await _purchaseService.ListPurchasesBySupplierAsync(supplierId, _branchId, token);
                if (token.IsCancellationRequested) return;

                Bills.Clear();
                foreach (var row in rows)
                    Bills.Add(row);

                UpdateBillTotals();
                SelectedBillIndex = rows.Count > 0 ? 0 : -1;
                Hint = rows.Count == 0
                    ? "No purchase invoices found."
                    : IsAllSuppliersFilter
                        ? $"Showing all {rows.Count} purchase invoice(s)."
                        : $"Showing {rows.Count} invoice(s) for the selected supplier.";
            }
            finally
            {
                _searchGate.Release();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Hint = $"Could not load invoices: {ex.Message}";
        }
    }

    private void UpdateBillTotals()
    {
        TotalAmount = Bills.Sum(b => b.GrandTotal);
        TotalBalance = Bills.Sum(b => b.PaymentDue);
        OnPropertyChanged(nameof(ShowBillTotals));
    }

    private async Task LoadInvoiceLinesAsync()
    {
        InvoiceLines.Clear();
        OnPropertyChanged(nameof(ShowInvoiceLines));
        if (SelectedBill is not PurchaseSupplierBillDto bill) return;

        try
        {
            await _searchGate.WaitAsync();
            try
            {
                var result = await _purchaseService.GetPurchaseForLoadAsync(bill.PurchaseId, _branchId);
                if (result.IsFailure || result.Value is null)
                {
                    Hint = result.Error ?? "Could not load invoice details.";
                    return;
                }

                foreach (var line in result.Value.Lines)
                {
                    var lineTotal = line.PurchasePrice * line.Quantity;
                    InvoiceLines.Add(new PurchaseInvoiceLineRowDto(
                        line.MedicineName,
                        line.BatchNumber,
                        line.Quantity,
                        line.FreeQuantity,
                        line.PurchasePrice,
                        lineTotal));
                }

                OnPropertyChanged(nameof(ShowInvoiceLines));
            }
            finally
            {
                _searchGate.Release();
            }
        }
        catch (Exception ex)
        {
            Hint = $"Could not load invoice lines: {ex.Message}";
        }
    }

    private void ClearBills()
    {
        Bills.Clear();
        InvoiceLines.Clear();
        SelectedBillIndex = -1;
        TotalAmount = 0;
        TotalBalance = 0;
        OnPropertyChanged(nameof(ShowInvoiceLines));
        OnPropertyChanged(nameof(ShowBillTotals));
    }
}
