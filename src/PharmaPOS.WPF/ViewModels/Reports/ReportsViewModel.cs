using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Win32;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Reports;
using PharmaPOS.Application.Features.SaleReturns;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Shared.Constants;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Reports;

public class ReportsViewModel : ObservableObject
{
    private readonly IReportsService _reports;
    private readonly ISaleReturnService _saleReturns;
    private readonly IInvoiceViewerDialogService _invoiceViewer;
    private readonly int? _branchId;
    private readonly IDialogService _dialog;

    private ReportKindOption _selectedReport;
    private DateTime _fromDate = DateTime.Today;
    private DateTime _toDate = DateTime.Today;
    private ReportSummaryDto _summary = new();
    private GstSummaryDto? _gstSummary;
    private bool _isBusy;
    private string? _statusMessage;
    private string _activeGrid = "Sales";
    private string _filterText = string.Empty;
    private FilterOption _selectedFilterOption = FilterOption.All;
    private List<FilterOption> _filterOptions = [FilterOption.All];

    private List<SalesReportRowDto> _allSales = [];
    private List<PurchaseReportRowDto> _allPurchases = [];
    private List<GstDetailRowDto> _allGst = [];
    private List<ProfitReportRowDto> _allProfit = [];
    private List<MedicineSalesRowDto> _allMedicineSales = [];
    private List<StockValuationReportRowDto> _allStock = [];
    private List<ExpiryReportRowDto> _allExpiry = [];
    private List<LowStockReportRowDto> _allLowStock = [];
    private List<SaleReturnSummaryRowDto> _allSaleReturns = [];
    private List<MedicineReturnReportRowDto> _allMedicineReturns = [];

    public ReportsViewModel(
        IReportsService reports,
        ISaleReturnService saleReturns,
        IInvoiceViewerDialogService invoiceViewer,
        ICurrentUserService currentUser,
        IDialogService dialog)
    {
        _reports = reports;
        _saleReturns = saleReturns;
        _invoiceViewer = invoiceViewer;
        _branchId = currentUser.CurrentUser?.BranchId;
        _dialog = dialog;

        CanExport = currentUser.HasAnyPermission(
            AppConstants.Permissions.ReportsExport, AppConstants.Permissions.ReportsManage);

        ReportOptions =
        [
            new(ReportKind.Sales, "Sales Report", "Completed sales invoices for the selected period."),
            new(ReportKind.Purchases, "Purchase Report", "Received purchase / GRN invoices for the period."),
            new(ReportKind.GstSummary, "GST Summary", "Output vs input GST with invoice-wise detail."),
            new(ReportKind.Profit, "Gross Profit", "Revenue vs estimated cost per sale invoice."),
            new(ReportKind.SalesByMedicine, "Sales by Medicine", "Quantity and revenue ranked by medicine."),
            new(ReportKind.StockValuation, "Stock Valuation", "Current stock value at purchase cost."),
            new(ReportKind.Expiry, "Expiry Report", "Expired and near-expiry batches."),
            new(ReportKind.LowStock, "Low Stock", "Medicines at or below reorder level."),
            new(ReportKind.SaleReturns, "Sale Returns", "Return transactions for the selected period."),
            new(ReportKind.MedicineReturns, "Medicine-wise Returns", "Returned quantities grouped by medicine and batch.")
        ];
        _selectedReport = ReportOptions[0];
        RefreshFilterOptions();

        RunReportCommand = new AsyncRelayCommand(_ => RunReportAsync(), _ => !IsBusy);
        ExportCsvCommand = new RelayCommand(_ => ExportCsv(), _ => CanExport && HasData && !IsBusy);
        ClearFilterCommand = new RelayCommand(_ => ClearFilters());
        OpenSaleRowCommand = new AsyncRelayCommand(
            p => OpenSaleRowAsync(p as SalesReportRowDto),
            _ => !IsBusy);
        OpenPurchaseRowCommand = new AsyncRelayCommand(
            p => OpenPurchaseRowAsync(p as PurchaseReportRowDto),
            _ => !IsBusy);
        ApplyTodayCommand = new RelayCommand(_ => ApplyPreset(DateTime.Today, DateTime.Today));
        ApplyThisMonthCommand = new RelayCommand(_ =>
        {
            var today = DateTime.Today;
            ApplyPreset(new DateTime(today.Year, today.Month, 1), today);
        });
        ApplyLastMonthCommand = new RelayCommand(_ =>
        {
            var today = DateTime.Today;
            var first = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
            ApplyPreset(first, first.AddMonths(1).AddDays(-1));
        });
    }

    public IReadOnlyList<ReportKindOption> ReportOptions { get; }

    public bool CanExport { get; }

    public ObservableCollection<SalesReportRowDto> SalesRows { get; } = new();
    public ObservableCollection<PurchaseReportRowDto> PurchaseRows { get; } = new();
    public ObservableCollection<GstDetailRowDto> GstRows { get; } = new();
    public ObservableCollection<ProfitReportRowDto> ProfitRows { get; } = new();
    public ObservableCollection<MedicineSalesRowDto> MedicineSalesRows { get; } = new();
    public ObservableCollection<StockValuationReportRowDto> StockValuationRows { get; } = new();
    public ObservableCollection<ExpiryReportRowDto> ExpiryRows { get; } = new();
    public ObservableCollection<LowStockReportRowDto> LowStockRows { get; } = new();
    public ObservableCollection<SaleReturnSummaryRowDto> SaleReturnRows { get; } = new();
    public ObservableCollection<MedicineReturnReportRowDto> MedicineReturnRows { get; } = new();

    public ReportKindOption SelectedReport
    {
        get => _selectedReport;
        set
        {
            if (!SetProperty(ref _selectedReport, value)) return;
            OnPropertyChanged(nameof(UsesDateRange));
            OnPropertyChanged(nameof(SelectedReportDescription));
            OnPropertyChanged(nameof(FilterTextHint));
            RefreshFilterOptions();
            ClearFilters(apply: false);
            ClearAllRows();
            GstSummary = null;
            StatusMessage = null;
            SetActiveGrid(GridNameFor(value.Kind));
            OnPropertyChanged(nameof(HasData));
            OnPropertyChanged(nameof(HasSourceData));
            OnPropertyChanged(nameof(ShowFilters));
            OnPropertyChanged(nameof(HasNoFilterMatches));
            _ = RunReportAsync();
        }
    }

    public string SelectedReportDescription => SelectedReport.Description;

    public bool UsesDateRange => SelectedReport.Kind is not (
        ReportKind.StockValuation or ReportKind.Expiry or ReportKind.LowStock);

    public string FilterTextHint => SelectedReport.Kind switch
    {
        ReportKind.Sales or ReportKind.Profit => "Filter by invoice # or customer...",
        ReportKind.Purchases => "Filter by invoice # or supplier...",
        ReportKind.GstSummary => "Filter by invoice # or party...",
        ReportKind.SalesByMedicine or ReportKind.LowStock => "Filter by medicine or generic...",
        ReportKind.StockValuation or ReportKind.Expiry => "Filter by medicine or batch...",
        ReportKind.SaleReturns => "Filter by return #, invoice, or customer...",
        ReportKind.MedicineReturns => "Filter by medicine or batch...",
        _ => "Filter results..."
    };

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
                ApplyFilters();
        }
    }

    public IReadOnlyList<FilterOption> FilterOptions => _filterOptions;

    public FilterOption SelectedFilterOption
    {
        get => _selectedFilterOption;
        set
        {
            if (SetProperty(ref _selectedFilterOption, value))
                ApplyFilters();
        }
    }

    public bool ShowFilterOption => FilterOptions.Count > 1;

    public bool HasActiveFilter =>
        !string.IsNullOrWhiteSpace(FilterText) ||
        SelectedFilterOption.Key != "all";

    public DateTime FromDate
    {
        get => _fromDate;
        set => SetProperty(ref _fromDate, value);
    }

    public DateTime ToDate
    {
        get => _toDate;
        set => SetProperty(ref _toDate, value);
    }

    public ReportSummaryDto Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public GstSummaryDto? GstSummary
    {
        get => _gstSummary;
        private set => SetProperty(ref _gstSummary, value);
    }

    public string ActiveGrid
    {
        get => _activeGrid;
        private set => SetProperty(ref _activeGrid, value);
    }

    public bool ShowSalesGrid => ActiveGrid == "Sales";
    public bool ShowPurchaseGrid => ActiveGrid == "Purchases";
    public bool ShowGstGrid => ActiveGrid == "Gst";
    public bool ShowGstSummary => ActiveGrid == "Gst" && GstSummary is not null;
    public bool ShowProfitGrid => ActiveGrid == "Profit";
    public bool ShowMedicineGrid => ActiveGrid == "Medicine";
    public bool ShowStockGrid => ActiveGrid == "Stock";
    public bool ShowExpiryGrid => ActiveGrid == "Expiry";
    public bool ShowLowStockGrid => ActiveGrid == "LowStock";
    public bool ShowSaleReturnGrid => ActiveGrid == "SaleReturns";
    public bool ShowMedicineReturnGrid => ActiveGrid == "MedicineReturns";

    /// <summary>Stock valuation uses Amount (MRP) + Cost KPIs instead of tax/discount.</summary>
    public bool ShowStockSummaryKpis => ShowStockGrid && HasData;
    public bool ShowGenericSummaryKpis => HasData && !ShowStockGrid;
    public bool ShowTaxDiscountKpis => HasData && !ShowStockGrid;

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

    public bool HasData => Summary.RecordCount > 0;

    /// <summary>True after a report run that returned at least one source row (before filtering).</summary>
    public bool HasSourceData => GetSourceCount() > 0;

    public bool ShowFilters => HasSourceData;

    public bool HasNoFilterMatches => HasSourceData && HasActiveFilter && !HasData;

    public ICommand RunReportCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand ClearFilterCommand { get; }
    public ICommand OpenSaleRowCommand { get; }
    public ICommand OpenPurchaseRowCommand { get; }
    public ICommand ApplyTodayCommand { get; }
    public ICommand ApplyThisMonthCommand { get; }
    public ICommand ApplyLastMonthCommand { get; }

    public Task OpenSaleRowAsync(SalesReportRowDto? row)
    {
        if (row is null || row.SaleId <= 0) return Task.CompletedTask;
        return _invoiceViewer.ShowSaleAsync(row.SaleId);
    }

    public Task OpenPurchaseRowAsync(PurchaseReportRowDto? row)
    {
        if (row is null || row.PurchaseId <= 0) return Task.CompletedTask;
        return _invoiceViewer.ShowPurchaseAsync(row.PurchaseId);
    }

    private void ApplyPreset(DateTime from, DateTime to)
    {
        FromDate = from;
        ToDate = to;
    }

    private void RefreshFilterOptions()
    {
        _filterOptions = SelectedReport.Kind switch
        {
            ReportKind.Sales or ReportKind.Purchases =>
            [
                FilterOption.All,
                new("unpaid", "Unpaid / due"),
                new("partial", "Partially paid"),
                new("paid", "Fully paid")
            ],
            ReportKind.GstSummary =>
            [
                FilterOption.All,
                new("sale", "Sales only"),
                new("purchase", "Purchases only")
            ],
            ReportKind.Expiry =>
            [
                FilterOption.All,
                new("expired", "Expired"),
                new("near", "Near expiry")
            ],
            ReportKind.LowStock =>
            [
                FilterOption.All,
                new("critical", "Out of stock"),
                new("low", "Below reorder")
            ],
            ReportKind.SaleReturns =>
            [
                FilterOption.All,
                new("full", "Full returns"),
                new("partial", "Partial returns"),
                new("cash", "Cash refund"),
                new("credit", "Credit note")
            ],
            _ => [FilterOption.All]
        };

        _selectedFilterOption = _filterOptions[0];
        OnPropertyChanged(nameof(FilterOptions));
        OnPropertyChanged(nameof(SelectedFilterOption));
        OnPropertyChanged(nameof(ShowFilterOption));
    }

    private void ClearFilters(bool apply = true)
    {
        _filterText = string.Empty;
        _selectedFilterOption = FilterOptions.FirstOrDefault() ?? FilterOption.All;
        OnPropertyChanged(nameof(FilterText));
        OnPropertyChanged(nameof(SelectedFilterOption));
        OnPropertyChanged(nameof(HasActiveFilter));
        if (apply) ApplyFilters();
    }

    private CancellationTokenSource? _runCts;
    private int _runId;
    private readonly SemaphoreSlim _runGate = new(1, 1);

    private async Task RunReportAsync()
    {
        var runId = Interlocked.Increment(ref _runId);
        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = new CancellationTokenSource();
        var token = _runCts.Token;

        // Serialize runs on the shared ReportsService/DbContext. Cancel alone is not
        // enough — EF may still be mid-query when the next report starts.
        await _runGate.WaitAsync();
        try
        {
            if (runId != _runId) return;

            IsBusy = true;
            StatusMessage = "Running report...";
            ClearAllRows();
            GstSummary = null;
            ClearFilters(apply: false);

            try
            {
                switch (SelectedReport.Kind)
                {
                    case ReportKind.Sales:
                        var sales = await _reports.GetSalesReportAsync(FromDate, ToDate, _branchId, token);
                        if (runId != _runId) return;
                        _allSales = sales.Rows;
                        Summary = sales.Summary;
                        SetActiveGrid("Sales");
                        break;

                    case ReportKind.Purchases:
                        var purchases = await _reports.GetPurchaseReportAsync(FromDate, ToDate, _branchId, token);
                        if (runId != _runId) return;
                        _allPurchases = purchases.Rows;
                        Summary = purchases.Summary;
                        SetActiveGrid("Purchases");
                        break;

                    case ReportKind.GstSummary:
                        var gst = await _reports.GetGstReportAsync(FromDate, ToDate, _branchId, token);
                        if (runId != _runId) return;
                        GstSummary = gst.Summary;
                        _allGst = gst.Rows;
                        Summary = new ReportSummaryDto
                        {
                            RecordCount = gst.Rows.Count,
                            TotalAmount = gst.Summary.SalesGrandTotal,
                            TotalTax = gst.Summary.NetTaxPayable,
                            FooterNote = $"Net GST payable: {gst.Summary.NetTaxPayable:N2}"
                        };
                        SetActiveGrid("Gst");
                        break;

                    case ReportKind.Profit:
                        var profit = await _reports.GetProfitReportAsync(FromDate, ToDate, _branchId, token);
                        if (runId != _runId) return;
                        _allProfit = profit.Rows;
                        Summary = profit.Summary;
                        SetActiveGrid("Profit");
                        break;

                    case ReportKind.SalesByMedicine:
                        var med = await _reports.GetSalesByMedicineReportAsync(FromDate, ToDate, _branchId, token);
                        if (runId != _runId) return;
                        _allMedicineSales = med.Rows;
                        Summary = med.Summary;
                        SetActiveGrid("Medicine");
                        break;

                    case ReportKind.StockValuation:
                        var stock = await _reports.GetStockValuationReportAsync(_branchId, token);
                        if (runId != _runId) return;
                        _allStock = stock.Rows;
                        Summary = stock.Summary;
                        SetActiveGrid("Stock");
                        break;

                    case ReportKind.Expiry:
                        var expiry = await _reports.GetExpiryReportAsync(_branchId, token);
                        if (runId != _runId) return;
                        _allExpiry = expiry.Rows;
                        Summary = expiry.Summary;
                        SetActiveGrid("Expiry");
                        break;

                    case ReportKind.LowStock:
                        var low = await _reports.GetLowStockReportAsync(_branchId, token);
                        if (runId != _runId) return;
                        _allLowStock = low.Rows;
                        Summary = low.Summary;
                        SetActiveGrid("LowStock");
                        break;

                    case ReportKind.SaleReturns:
                        var returns = await _saleReturns.ListReturnsAsync(FromDate, ToDate, _branchId, token);
                        if (runId != _runId) return;
                        _allSaleReturns = returns;
                        Summary = new ReportSummaryDto
                        {
                            RecordCount = returns.Count,
                            TotalAmount = returns.Sum(r => r.RefundAmount),
                            FooterNote = $"{returns.Count} return(s)"
                        };
                        SetActiveGrid("SaleReturns");
                        break;

                    case ReportKind.MedicineReturns:
                        var medRet = await _saleReturns.GetMedicineReturnReportAsync(FromDate, ToDate, _branchId, token);
                        if (runId != _runId) return;
                        _allMedicineReturns = medRet;
                        Summary = new ReportSummaryDto
                        {
                            RecordCount = medRet.Count,
                            TotalAmount = medRet.Sum(r => r.RefundAmount),
                            FooterNote = $"{medRet.Count} medicine/batch group(s)"
                        };
                        SetActiveGrid("MedicineReturns");
                        break;
                }

                if (runId != _runId) return;
                ApplyFilters();
                CommandManager.InvalidateRequerySuggested();
            }
            catch (OperationCanceledException)
            {
                // Switched to another report; ignore.
            }
            catch (Exception ex)
            {
                if (runId != _runId) return;
                StatusMessage = $"Report failed: {ex.Message}";
                _dialog.ShowError(ex.Message);
            }
            finally
            {
                if (runId == _runId)
                    IsBusy = false;
            }
        }
        finally
        {
            _runGate.Release();
        }
    }

    private void ApplyFilters()
    {
        var term = FilterText.Trim();
        var option = SelectedFilterOption.Key;

        switch (ActiveGrid)
        {
            case "Sales":
                Fill(SalesRows, _allSales.Where(r =>
                    Matches(term, r.InvoiceNumber, r.CustomerName) &&
                    option switch
                    {
                        "unpaid" => r.PaidAmount <= 0 && r.BalanceDue > 0,
                        "partial" => r.PaidAmount > 0 && r.BalanceDue > 0,
                        "paid" => r.BalanceDue <= 0,
                        _ => true
                    }));
                break;

            case "Purchases":
                Fill(PurchaseRows, _allPurchases.Where(r =>
                    Matches(term, r.InvoiceNumber, r.SupplierName) &&
                    option switch
                    {
                        "unpaid" => r.PaidAmount <= 0 && r.BalanceDue > 0,
                        "partial" => r.PaidAmount > 0 && r.BalanceDue > 0,
                        "paid" => r.BalanceDue <= 0,
                        _ => true
                    }));
                break;

            case "Gst":
                Fill(GstRows, _allGst.Where(r =>
                    Matches(term, r.InvoiceNumber, r.PartyName, r.DocumentType) &&
                    option switch
                    {
                        "sale" => r.DocumentType.Contains("Sale", StringComparison.OrdinalIgnoreCase),
                        "purchase" => r.DocumentType.Contains("Purchase", StringComparison.OrdinalIgnoreCase),
                        _ => true
                    }));
                break;

            case "Profit":
                Fill(ProfitRows, _allProfit.Where(r => Matches(term, r.InvoiceNumber, r.CustomerName)));
                break;

            case "Medicine":
                Fill(MedicineSalesRows, _allMedicineSales.Where(r => Matches(term, r.MedicineName, r.GenericName)));
                break;

            case "Stock":
                Fill(StockValuationRows, _allStock.Where(r => Matches(term, r.MedicineName, r.BatchNumber)));
                break;

            case "Expiry":
                Fill(ExpiryRows, _allExpiry.Where(r =>
                    Matches(term, r.MedicineName, r.BatchNumber, r.ExpiryStatus) &&
                    option switch
                    {
                        "expired" => r.ExpiryStatus.Contains("Expired", StringComparison.OrdinalIgnoreCase),
                        "near" => r.ExpiryStatus.Contains("Near", StringComparison.OrdinalIgnoreCase),
                        _ => true
                    }));
                break;

            case "LowStock":
                Fill(LowStockRows, _allLowStock.Where(r =>
                    Matches(term, r.MedicineName, r.GenericName) &&
                    option switch
                    {
                        "critical" => r.IsCritical,
                        "low" => !r.IsCritical,
                        _ => true
                    }));
                break;

            case "SaleReturns":
                Fill(SaleReturnRows, _allSaleReturns.Where(r =>
                    Matches(term, r.ReturnNumber, r.OriginalInvoiceNumber, r.CustomerName, r.CashierName, r.RefundMode.ToString()) &&
                    option switch
                    {
                        "full" => r.IsFullReturn,
                        "partial" => !r.IsFullReturn,
                        "cash" => r.RefundMode is RefundMode.Cash or RefundMode.Card or RefundMode.Upi or RefundMode.Wallet,
                        "credit" => r.RefundMode is RefundMode.StoreCredit or RefundMode.CreditNote,
                        _ => true
                    }));
                break;

            case "MedicineReturns":
                Fill(MedicineReturnRows, _allMedicineReturns.Where(r => Matches(term, r.MedicineName, r.BatchNumber)));
                break;
        }

        UpdateFilteredSummary();
        OnPropertyChanged(nameof(HasActiveFilter));
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(HasSourceData));
        OnPropertyChanged(nameof(ShowFilters));
        OnPropertyChanged(nameof(HasNoFilterMatches));
        CommandManager.InvalidateRequerySuggested();
    }

    private int GetSourceCount() => ActiveGrid switch
    {
        "Sales" => _allSales.Count,
        "Purchases" => _allPurchases.Count,
        "Gst" => _allGst.Count,
        "Profit" => _allProfit.Count,
        "Medicine" => _allMedicineSales.Count,
        "Stock" => _allStock.Count,
        "Expiry" => _allExpiry.Count,
        "LowStock" => _allLowStock.Count,
        "SaleReturns" => _allSaleReturns.Count,
        "MedicineReturns" => _allMedicineReturns.Count,
        _ => 0
    };

    private void UpdateFilteredSummary()
    {
        var (count, amount, tax, discount) = ActiveGrid switch
        {
            "Sales" => (SalesRows.Count, SalesRows.Sum(r => r.GrandTotal), SalesRows.Sum(r => r.TaxAmount), SalesRows.Sum(r => r.DiscountAmount)),
            "Purchases" => (PurchaseRows.Count, PurchaseRows.Sum(r => r.GrandTotal), PurchaseRows.Sum(r => r.TaxAmount), PurchaseRows.Sum(r => r.DiscountAmount)),
            "Gst" => (GstRows.Count, GstRows.Sum(r => r.GrandTotal), GstRows.Sum(r => r.TotalTax), 0m),
            "Profit" => (ProfitRows.Count, ProfitRows.Sum(r => r.Revenue), ProfitRows.Sum(r => r.Cost), ProfitRows.Sum(r => r.GrossProfit)),
            "Medicine" => (MedicineSalesRows.Count, MedicineSalesRows.Sum(r => r.Revenue), MedicineSalesRows.Sum(r => r.Cost), MedicineSalesRows.Sum(r => r.GrossProfit)),
            "Stock" => (StockValuationRows.Count,
                StockValuationRows.Sum(r => r.StockAmount),
                StockValuationRows.Sum(r => r.StockValue),
                0m),
            "Expiry" => (ExpiryRows.Count, ExpiryRows.Sum(r => r.StockValue), 0m, 0m),
            "LowStock" => (LowStockRows.Count, 0m, 0m, LowStockRows.Sum(r => r.Shortfall)),
            "SaleReturns" => (SaleReturnRows.Count, SaleReturnRows.Sum(r => r.RefundAmount), 0m, 0m),
            "MedicineReturns" => (MedicineReturnRows.Count, MedicineReturnRows.Sum(r => r.RefundAmount), 0m, 0m),
            _ => (0, 0m, 0m, 0m)
        };

        var totalSource = GetSourceCount();

        Summary = new ReportSummaryDto
        {
            RecordCount = count,
            TotalAmount = amount,
            TotalTax = tax,
            TotalDiscount = discount,
            FooterNote = HasActiveFilter
                ? (count == 0
                    ? $"No matching records (filtered from {totalSource})"
                    : $"Showing {count} of {totalSource} record(s)")
                : ActiveGrid == "Stock"
                    ? $"{count} batch(es) — Amount {amount:N2} · Cost {tax:N2}"
                    : $"{count} record(s) — total {amount:N2}"
        };
        StatusMessage = Summary.FooterNote;
        OnPropertyChanged(nameof(ShowStockSummaryKpis));
        OnPropertyChanged(nameof(ShowGenericSummaryKpis));
        OnPropertyChanged(nameof(ShowTaxDiscountKpis));
    }

    private static void Fill<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
            target.Add(item);
    }

    private static bool Matches(string term, params string?[] values)
    {
        if (string.IsNullOrWhiteSpace(term)) return true;
        return values.Any(v => !string.IsNullOrWhiteSpace(v) &&
                               v.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private void SetActiveGrid(string name)
    {
        ActiveGrid = name;
        OnPropertyChanged(nameof(ShowSalesGrid));
        OnPropertyChanged(nameof(ShowPurchaseGrid));
        OnPropertyChanged(nameof(ShowGstGrid));
        OnPropertyChanged(nameof(ShowGstSummary));
        OnPropertyChanged(nameof(ShowProfitGrid));
        OnPropertyChanged(nameof(ShowMedicineGrid));
        OnPropertyChanged(nameof(ShowStockGrid));
        OnPropertyChanged(nameof(ShowExpiryGrid));
        OnPropertyChanged(nameof(ShowLowStockGrid));
        OnPropertyChanged(nameof(ShowSaleReturnGrid));
        OnPropertyChanged(nameof(ShowMedicineReturnGrid));
        OnPropertyChanged(nameof(ShowStockSummaryKpis));
        OnPropertyChanged(nameof(ShowGenericSummaryKpis));
        OnPropertyChanged(nameof(ShowTaxDiscountKpis));
    }

    private static string GridNameFor(ReportKind kind) => kind switch
    {
        ReportKind.Sales => "Sales",
        ReportKind.Purchases => "Purchases",
        ReportKind.GstSummary => "Gst",
        ReportKind.Profit => "Profit",
        ReportKind.SalesByMedicine => "Medicine",
        ReportKind.StockValuation => "Stock",
        ReportKind.Expiry => "Expiry",
        ReportKind.LowStock => "LowStock",
        ReportKind.SaleReturns => "SaleReturns",
        ReportKind.MedicineReturns => "MedicineReturns",
        _ => "Sales"
    };

    private void ClearAllRows()
    {
        SalesRows.Clear();
        PurchaseRows.Clear();
        GstRows.Clear();
        ProfitRows.Clear();
        MedicineSalesRows.Clear();
        StockValuationRows.Clear();
        ExpiryRows.Clear();
        LowStockRows.Clear();
        SaleReturnRows.Clear();
        MedicineReturnRows.Clear();
        _allSales = [];
        _allPurchases = [];
        _allGst = [];
        _allProfit = [];
        _allMedicineSales = [];
        _allStock = [];
        _allExpiry = [];
        _allLowStock = [];
        _allSaleReturns = [];
        _allMedicineReturns = [];
        Summary = new ReportSummaryDto();
    }

    private void ExportCsv()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"{SelectedReport.Kind}_{DateTime.Today:yyyyMMdd}.csv"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            switch (ActiveGrid)
            {
                case "Sales": ReportCsvExporter.Export(dialog.FileName, SalesRows); break;
                case "Purchases": ReportCsvExporter.Export(dialog.FileName, PurchaseRows); break;
                case "Gst": ReportCsvExporter.Export(dialog.FileName, GstRows); break;
                case "Profit": ReportCsvExporter.Export(dialog.FileName, ProfitRows); break;
                case "Medicine": ReportCsvExporter.Export(dialog.FileName, MedicineSalesRows); break;
                case "Stock": ReportCsvExporter.Export(dialog.FileName, StockValuationRows); break;
                case "Expiry": ReportCsvExporter.Export(dialog.FileName, ExpiryRows); break;
                case "LowStock": ReportCsvExporter.Export(dialog.FileName, LowStockRows); break;
                case "SaleReturns": ReportCsvExporter.Export(dialog.FileName, SaleReturnRows); break;
                case "MedicineReturns": ReportCsvExporter.Export(dialog.FileName, MedicineReturnRows); break;
            }
            _dialog.ShowInfo($"Exported to {dialog.FileName}");
        }
        catch (Exception ex)
        {
            _dialog.ShowError($"Export failed: {ex.Message}");
        }
    }
}

public sealed class FilterOption(string key, string label)
{
    public static FilterOption All { get; } = new("all", "All");

    public string Key { get; } = key;
    public string Label { get; } = label;

    public override string ToString() => Label;
}
