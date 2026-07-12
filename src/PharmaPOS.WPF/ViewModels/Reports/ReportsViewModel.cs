using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Win32;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Reports;
using PharmaPOS.Application.Features.SaleReturns;
using PharmaPOS.Shared.Constants;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Reports;

public class ReportsViewModel : ObservableObject
{
    private readonly IReportsService _reports;
    private readonly ISaleReturnService _saleReturns;
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

    public ReportsViewModel(
        IReportsService reports,
        ISaleReturnService saleReturns,
        ICurrentUserService currentUser,
        IDialogService dialog)
    {
        _reports = reports;
        _saleReturns = saleReturns;
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

        RunReportCommand = new AsyncRelayCommand(_ => RunReportAsync(), _ => !IsBusy);
        ExportCsvCommand = new RelayCommand(_ => ExportCsv(), _ => CanExport && HasData && !IsBusy);
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
        }
    }

    public string SelectedReportDescription => SelectedReport.Description;

    public bool UsesDateRange => SelectedReport.Kind is not (
        ReportKind.StockValuation or ReportKind.Expiry or ReportKind.LowStock);

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

    public ICommand RunReportCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand ApplyTodayCommand { get; }
    public ICommand ApplyThisMonthCommand { get; }
    public ICommand ApplyLastMonthCommand { get; }

    private void ApplyPreset(DateTime from, DateTime to)
    {
        FromDate = from;
        ToDate = to;
    }

    private async Task RunReportAsync()
    {
        IsBusy = true;
        StatusMessage = "Running report...";
        ClearAllRows();
        GstSummary = null;

        try
        {
            switch (SelectedReport.Kind)
            {
                case ReportKind.Sales:
                    var sales = await _reports.GetSalesReportAsync(FromDate, ToDate, _branchId);
                    Summary = sales.Summary;
                    foreach (var r in sales.Rows) SalesRows.Add(r);
                    SetActiveGrid("Sales");
                    break;

                case ReportKind.Purchases:
                    var purchases = await _reports.GetPurchaseReportAsync(FromDate, ToDate, _branchId);
                    Summary = purchases.Summary;
                    foreach (var r in purchases.Rows) PurchaseRows.Add(r);
                    SetActiveGrid("Purchases");
                    break;

                case ReportKind.GstSummary:
                    var gst = await _reports.GetGstReportAsync(FromDate, ToDate, _branchId);
                    GstSummary = gst.Summary;
                    Summary = new ReportSummaryDto
                    {
                        RecordCount = gst.Rows.Count,
                        TotalAmount = gst.Summary.SalesGrandTotal,
                        TotalTax = gst.Summary.NetTaxPayable,
                        FooterNote = $"Net GST payable: {gst.Summary.NetTaxPayable:N2}"
                    };
                    foreach (var r in gst.Rows) GstRows.Add(r);
                    SetActiveGrid("Gst");
                    break;

                case ReportKind.Profit:
                    var profit = await _reports.GetProfitReportAsync(FromDate, ToDate, _branchId);
                    Summary = profit.Summary;
                    foreach (var r in profit.Rows) ProfitRows.Add(r);
                    SetActiveGrid("Profit");
                    break;

                case ReportKind.SalesByMedicine:
                    var med = await _reports.GetSalesByMedicineReportAsync(FromDate, ToDate, _branchId);
                    Summary = med.Summary;
                    foreach (var r in med.Rows) MedicineSalesRows.Add(r);
                    SetActiveGrid("Medicine");
                    break;

                case ReportKind.StockValuation:
                    var stock = await _reports.GetStockValuationReportAsync(_branchId);
                    Summary = stock.Summary;
                    foreach (var r in stock.Rows) StockValuationRows.Add(r);
                    SetActiveGrid("Stock");
                    break;

                case ReportKind.Expiry:
                    var expiry = await _reports.GetExpiryReportAsync(_branchId);
                    Summary = expiry.Summary;
                    foreach (var r in expiry.Rows) ExpiryRows.Add(r);
                    SetActiveGrid("Expiry");
                    break;

                case ReportKind.LowStock:
                    var low = await _reports.GetLowStockReportAsync(_branchId);
                    Summary = low.Summary;
                    foreach (var r in low.Rows) LowStockRows.Add(r);
                    SetActiveGrid("LowStock");
                    break;

                case ReportKind.SaleReturns:
                    var returns = await _saleReturns.ListReturnsAsync(FromDate, ToDate, _branchId);
                    Summary = new ReportSummaryDto
                    {
                        RecordCount = returns.Count,
                        TotalAmount = returns.Sum(r => r.RefundAmount),
                        FooterNote = $"{returns.Count} return(s)"
                    };
                    foreach (var r in returns) SaleReturnRows.Add(r);
                    SetActiveGrid("SaleReturns");
                    break;

                case ReportKind.MedicineReturns:
                    var medRet = await _saleReturns.GetMedicineReturnReportAsync(FromDate, ToDate, _branchId);
                    Summary = new ReportSummaryDto
                    {
                        RecordCount = medRet.Count,
                        TotalAmount = medRet.Sum(r => r.RefundAmount),
                        FooterNote = $"{medRet.Count} medicine/batch group(s)"
                    };
                    foreach (var r in medRet) MedicineReturnRows.Add(r);
                    SetActiveGrid("MedicineReturns");
                    break;
            }

            StatusMessage = Summary.FooterNote ??
                            $"{Summary.RecordCount} record(s) — total {Summary.TotalAmount:N2}";
            OnPropertyChanged(nameof(HasData));
            CommandManager.InvalidateRequerySuggested();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Report failed: {ex.Message}";
            _dialog.ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
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
    }

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
