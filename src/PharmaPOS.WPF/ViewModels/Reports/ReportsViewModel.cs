using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using Microsoft.Win32;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Reports;
using PharmaPOS.Application.Features.Settings;
using PharmaPOS.Application.Features.ShortageBook;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Shared.Constants;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Reports;

public class ReportsViewModel : ObservableObject
{
    private readonly IReportsService _reports;
    private readonly IInvoiceViewerDialogService _invoiceViewer;
    private readonly IInvoicePrintService _printService;
    private readonly IShortageBookService _shortageBook;
    private readonly ICurrentUserService _currentUser;
    private readonly int? _branchId;
    private readonly IDialogService _dialog;
    private readonly IFinancialYearContext _financialYear;

    private ReportKindOption _selectedReport;
    private DateTime _fromDate = DateTime.Today;
    private DateTime _toDate = DateTime.Today;
    private ReportSummaryDto _summary = new();
    private GstSummaryDto? _gstSummary;
    private bool _isBusy;
    private string? _statusMessage;
    private string _filterText = string.Empty;
    private FilterOption _selectedFilterOption = FilterOption.All;
    private List<FilterOption> _filterOptions = [FilterOption.All];
    private string _selectedExpirySupplierKey = "all";
    private List<FilterOption> _expirySupplierOptions = [FilterOption.All];
    private DateTime _expiryFilterToday = DateTime.Today;
    private List<ReportColumnDto> _columns = [];
    private List<Dictionary<string, object?>> _allRows = [];
    private GstReturnExportDto? _gstReturnExport;
    private ScheduleRegisterReportDto? _scheduleRegisterReport;
    private ScheduleRegisterFilterOption _selectedScheduleFilter;
    private ReportDefinition _definition;
    private ReportRowViewModel? _selectedRow;

    public ReportsViewModel(
        IReportsService reports,
        IInvoiceViewerDialogService invoiceViewer,
        IInvoicePrintService printService,
        IShortageBookService shortageBook,
        ICurrentUserService currentUser,
        IFinancialYearContext financialYear,
        IDialogService dialog)
    {
        _reports = reports;
        _invoiceViewer = invoiceViewer;
        _printService = printService;
        _shortageBook = shortageBook;
        _currentUser = currentUser;
        _branchId = currentUser.CurrentUser?.BranchId;
        _financialYear = financialYear;
        _dialog = dialog;

        ApplyActiveFinancialYearDates();

        CanExport = currentUser.HasAnyPermission(
            AppConstants.Permissions.ReportsExport, AppConstants.Permissions.ReportsManage);

        ScheduleFilterOptions =
        [
            new(ScheduleRegisterFilter.HAndH1, "H + H1"),
            new(ScheduleRegisterFilter.ScheduleH, "Schedule H only"),
            new(ScheduleRegisterFilter.ScheduleH1, "Schedule H1 only")
        ];
        _selectedScheduleFilter = ScheduleFilterOptions[0];

        ReportOptions = ReportCatalog.DistinctOptions()
            .Where(o => currentUser.HasPermission(ReportMenuPermissions.For(o.Kind)))
            .ToList();
        if (ReportOptions.Count == 0 && currentUser.CanAccessModule("reports"))
            ReportOptions = ReportCatalog.DistinctOptions().ToList();

        _selectedReport = ReportOptions.FirstOrDefault()
            ?? ReportCatalog.DistinctOptions().First();
        _definition = ReportCatalog.Get(_selectedReport.Kind);
        RefreshFilterOptions();

        RunReportCommand = new AsyncRelayCommand(_ => RunReportAsync(), _ => !IsBusy);
        ExportCsvCommand = new RelayCommand(_ => ExportCsv(), _ => CanExport && HasData && !IsBusy);
        ExportExcelCommand = new RelayCommand(_ => ExportExcel(), _ => CanExport && HasData && !IsBusy);
        ExportPdfCommand = new RelayCommand(_ => ExportPdf(), _ => CanExport && HasData && !IsBusy);
        ExportGstJsonCommand = new RelayCommand(_ => ExportGstJson(), _ => CanExport && ShowGstReturnExport && _gstReturnExport is not null && !IsBusy);
        ExportGstExcelCommand = new RelayCommand(_ => ExportGstExcel(), _ => CanExport && ShowGstReturnExport && _gstReturnExport is not null && !IsBusy);
        PrintScheduleRegisterCommand = new RelayCommand(
            _ => PrintScheduleRegister(),
            _ => ShowScheduleActions && HasData && !IsBusy);
        ClearFilterCommand = new RelayCommand(_ => ClearFilters());
        OpenRowCommand = new AsyncRelayCommand(p => OpenRowAsync(p as ReportRowViewModel), _ => !IsBusy);
        AddToShortageBookCommand = new AsyncRelayCommand(
            _ => AddSelectedToShortageBookAsync(),
            _ => ShowShortageBookActions && SelectedRow is not null && !IsBusy);
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

    public event Action? ColumnsChanged;

    public IReadOnlyList<ReportKindOption> ReportOptions { get; }
    public bool CanExport { get; }

    public ObservableCollection<ReportColumnDto> Columns { get; } = new();
    public ObservableCollection<ReportRowViewModel> Rows { get; } = new();

    public IReadOnlyList<ScheduleRegisterFilterOption> ScheduleFilterOptions { get; }

    public ScheduleRegisterFilterOption SelectedScheduleFilter
    {
        get => _selectedScheduleFilter;
        set
        {
            if (!SetProperty(ref _selectedScheduleFilter, value)) return;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool ShowScheduleFilter => SelectedReport.Kind == ReportKind.ScheduleRegister;
    public bool ShowScheduleActions => SelectedReport.Kind == ReportKind.ScheduleRegister;
    public bool ShowShortageBookActions => SelectedReport.Kind == ReportKind.MedicinesSoldByDate;

    public ReportKindOption SelectedReport
    {
        get => _selectedReport;
        set
        {
            if (!SetProperty(ref _selectedReport, value)) return;
            _definition = ReportCatalog.Get(value.Kind);
            OnPropertyChanged(nameof(UsesDateRange));
            OnPropertyChanged(nameof(SelectedReportDescription));
            OnPropertyChanged(nameof(FilterTextHint));
            OnPropertyChanged(nameof(ShowScheduleFilter));
            OnPropertyChanged(nameof(ShowScheduleActions));
            OnPropertyChanged(nameof(ShowShortageBookActions));
            OnPropertyChanged(nameof(ShowGstReturnExport));
            OnPropertyChanged(nameof(CanOpenInvoiceHint));
            OnPropertyChanged(nameof(OpenRowHint));
            RefreshFilterOptions();
            ClearFilters(apply: false);
            ClearAllRows();
            GstSummary = null;
            StatusMessage = null;
            OnPropertyChanged(nameof(HasData));
            OnPropertyChanged(nameof(HasSourceData));
            OnPropertyChanged(nameof(ShowFilters));
            OnPropertyChanged(nameof(HasNoFilterMatches));
            _ = RunReportAsync();
        }
    }

    public ReportRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (SetProperty(ref _selectedRow, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public void SelectReport(ReportKind kind)
    {
        var option = ReportOptions.FirstOrDefault(o => o.Kind == kind);
        if (option is not null)
            SelectedReport = option;
    }

    public string SelectedReportDescription => SelectedReport.Description;

    public bool UsesDateRange => _definition.UsesDateRange;

    public string FilterTextHint => SelectedReport.Kind switch
    {
        ReportKind.Sales or ReportKind.Profit or ReportKind.SalesCreditDue => "Filter by invoice # or customer...",
        ReportKind.Purchases or ReportKind.PurchasesBySupplier or ReportKind.SupplierOutstanding =>
            "Filter by invoice #, supplier, or due reason...",
        ReportKind.GstSummary => "Filter by invoice # or party...",
        ReportKind.Gstr1 or ReportKind.Gstr2B => "Filter by invoice, party, GSTIN, or section...",
        ReportKind.SalesByMedicine or ReportKind.MedicinesSoldByDate or ReportKind.LowStock or
            ReportKind.SlowMovingStock or ReportKind.BatchStock or ReportKind.StockAdjustments =>
            "Filter by medicine, invoice, or batch...",
        ReportKind.ScheduleRegister => "Filter by invoice, patient, doctor, or medicine...",
        ReportKind.StockValuation or ReportKind.Expiry => "Filter by medicine, batch, or supplier...",
        ReportKind.SaleReturns => "Filter by return #, invoice, or customer...",
        ReportKind.MedicineReturns => "Filter by medicine or batch...",
        ReportKind.SalesByCustomer or ReportKind.CustomerOutstanding or ReportKind.CustomerReceipts =>
            "Filter by customer...",
        ReportKind.PaymentVouchers or ReportKind.ReceiptVouchers or ReportKind.SupplierPayments or
            ReportKind.ExpenseRegister => "Filter by voucher #, party, or account...",
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

    public IReadOnlyList<FilterOption> ExpirySupplierOptions => _expirySupplierOptions;

    public string SelectedExpirySupplierKey
    {
        get => _selectedExpirySupplierKey;
        set
        {
            var key = string.IsNullOrWhiteSpace(value) ? "all" : value;
            if (SetProperty(ref _selectedExpirySupplierKey, key))
                ApplyFilters();
        }
    }

    public bool ShowExpirySupplierFilter =>
        SelectedReport.Kind == ReportKind.Expiry && HasSourceData;

    public bool HasActiveFilter =>
        !string.IsNullOrWhiteSpace(FilterText) ||
        SelectedFilterOption.Key != "all" ||
        (ShowExpirySupplierFilter && SelectedExpirySupplierKey != "all");

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
        private set
        {
            if (!SetProperty(ref _gstSummary, value)) return;
            OnPropertyChanged(nameof(ShowGstSummary));
        }
    }

    public bool ShowGstSummary => SelectedReport.Kind == ReportKind.GstSummary && GstSummary is not null;
    public bool ShowGstReturnExport => SelectedReport.Kind is ReportKind.Gstr1 or ReportKind.Gstr2B;
    public string? GstReturnDisclaimer => _gstReturnExport?.Disclaimer;

    public bool ShowStockSummaryKpis => SelectedReport.Kind == ReportKind.StockValuation && HasData;
    public bool ShowScheduleSummaryKpis => SelectedReport.Kind == ReportKind.ScheduleRegister && HasData;
    public bool ShowGstReturnKpis => ShowGstReturnExport && HasData;
    public bool ShowPaymentSummaryKpis => SelectedReport.Kind == ReportKind.SupplierPayments && HasData;
    public bool ShowGenericSummaryKpis =>
        HasData && !ShowStockSummaryKpis && !ShowScheduleSummaryKpis && !ShowGstReturnKpis && !ShowPaymentSummaryKpis;
    public bool ShowTaxDiscountKpis => ShowGenericSummaryKpis;

    public bool CanOpenInvoiceHint => SelectedReport.Kind is
        ReportKind.Sales or ReportKind.Purchases or ReportKind.ScheduleRegister or ReportKind.SalesCreditDue
            or ReportKind.MedicinesSoldByDate or ReportKind.SaleReturns or ReportKind.PurchaseReturns
            or ReportKind.Profit or ReportKind.SalesByCustomer or ReportKind.SalesDayWise
            or ReportKind.SalesByPaymentMode or ReportKind.SalesByMedicine or ReportKind.MedicineReturns
            or ReportKind.PurchasesBySupplier or ReportKind.SupplierOutstanding or ReportKind.CustomerOutstanding
            or ReportKind.GstSummary or ReportKind.SupplierPayments;

    public string OpenRowHint => IsConsolidatedBillReport(SelectedReport.Kind)
        ? "Double-click a row (or press Enter) to list underlying bills, then open any bill for details."
        : "Double-click a row (or press Enter) to open the related invoice.";

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
    public bool HasSourceData => _allRows.Count > 0;
    public bool ShowFilters => HasSourceData;
    public bool HasNoFilterMatches => HasSourceData && HasActiveFilter && !HasData;

    public ICommand RunReportCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand ExportExcelCommand { get; }
    public ICommand ExportPdfCommand { get; }
    public ICommand ExportGstJsonCommand { get; }
    public ICommand ExportGstExcelCommand { get; }
    public ICommand PrintScheduleRegisterCommand { get; }
    public ICommand ClearFilterCommand { get; }
    public ICommand OpenRowCommand { get; }
    public ICommand AddToShortageBookCommand { get; }
    public ICommand ApplyTodayCommand { get; }
    public ICommand ApplyThisMonthCommand { get; }
    public ICommand ApplyLastMonthCommand { get; }

    public Task OpenRowAsync(ReportRowViewModel? row)
    {
        if (row is null) return Task.CompletedTask;
        if (IsConsolidatedBillReport(SelectedReport.Kind))
            return OpenConsolidatedBillsAsync(row);
        if (TryGetInt(row, "SaleId", out var saleId) && saleId > 0)
            return _invoiceViewer.ShowSaleAsync(saleId);
        if (TryGetInt(row, "PurchaseId", out var purchaseId) && purchaseId > 0)
            return _invoiceViewer.ShowPurchaseAsync(purchaseId);
        return Task.CompletedTask;
    }

    private static bool IsConsolidatedBillReport(ReportKind kind) => kind is
        ReportKind.SalesByCustomer or ReportKind.SalesDayWise or ReportKind.SalesByPaymentMode
            or ReportKind.SalesByMedicine or ReportKind.MedicineReturns
            or ReportKind.PurchasesBySupplier or ReportKind.SupplierOutstanding
            or ReportKind.CustomerOutstanding;

    private async Task OpenConsolidatedBillsAsync(ReportRowViewModel row)
    {
        var query = BuildDrillDownQuery(row);
        if (query is null)
        {
            _dialog.ShowInfo("This row does not have enough information to list bills.", "Bills");
            return;
        }

        try
        {
            IsBusy = true;
            var bills = await _reports.ListUnderlyingBillsAsync(query);
            var subtitle = query.Kind is ReportKind.CustomerOutstanding or ReportKind.SupplierOutstanding
                ? (bills.Count == 0
                    ? "No open bills found."
                    : $"{bills.Count} open bill(s)")
                : (bills.Count == 0
                    ? $"No bills from {query.From:dd-MMM-yyyy} to {query.To:dd-MMM-yyyy}."
                    : $"{bills.Count} bill(s) · {query.From:dd-MMM-yyyy} – {query.To:dd-MMM-yyyy}");

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var window = new Views.ReportBillsDrillDownWindow(
                    query.Title,
                    subtitle,
                    bills,
                    OpenListedBillAsync)
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };
                window.ShowDialog();
            });
        }
        catch (Exception ex)
        {
            _dialog.ShowError($"Could not load bills.\n{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private ReportBillDrillDownQuery? BuildDrillDownQuery(ReportRowViewModel row)
    {
        var kind = SelectedReport.Kind;
        return kind switch
        {
            ReportKind.SalesByCustomer => string.IsNullOrWhiteSpace(GetString(row, "Customer"))
                ? null
                : new ReportBillDrillDownQuery
                {
                    Kind = kind,
                    From = FromDate,
                    To = ToDate,
                    BranchId = _branchId,
                    CustomerKey = GetString(row, "Customer"),
                    Title = GetString(row, "Customer")
                },
            ReportKind.SalesDayWise => !DateTime.TryParseExact(
                    GetString(row, "DayKey"), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var day)
                ? null
                : new ReportBillDrillDownQuery
                {
                    Kind = kind,
                    From = FromDate,
                    To = ToDate,
                    BranchId = _branchId,
                    Day = day,
                    Title = $"Sales · {day:dd-MMM-yyyy}"
                },
            ReportKind.SalesByPaymentMode => string.IsNullOrWhiteSpace(GetString(row, "Method"))
                ? null
                : new ReportBillDrillDownQuery
                {
                    Kind = kind,
                    From = FromDate,
                    To = ToDate,
                    BranchId = _branchId,
                    PaymentMethod = GetString(row, "Method"),
                    Title = $"Payment · {GetString(row, "Method")}"
                },
            ReportKind.SalesByMedicine => !TryGetInt(row, "MedicineId", out var medId) || medId <= 0
                ? null
                : new ReportBillDrillDownQuery
                {
                    Kind = kind,
                    From = FromDate,
                    To = ToDate,
                    BranchId = _branchId,
                    MedicineId = medId,
                    Title = GetString(row, "MedicineName").Length > 0
                        ? GetString(row, "MedicineName")
                        : $"Medicine #{medId}"
                },
            ReportKind.MedicineReturns => !TryGetInt(row, "MedicineId", out var retMedId) || retMedId <= 0
                ? null
                : new ReportBillDrillDownQuery
                {
                    Kind = kind,
                    From = FromDate,
                    To = ToDate,
                    BranchId = _branchId,
                    MedicineId = retMedId,
                    BatchNumber = GetString(row, "BatchNumber"),
                    Title = $"{GetString(row, "MedicineName")} · {GetString(row, "BatchNumber")}"
                },
            ReportKind.PurchasesBySupplier => !TryGetInt(row, "SupplierId", out var supplierId) || supplierId <= 0
                ? null
                : new ReportBillDrillDownQuery
                {
                    Kind = kind,
                    From = FromDate,
                    To = ToDate,
                    BranchId = _branchId,
                    SupplierId = supplierId,
                    Title = GetString(row, "Supplier").Length > 0
                        ? GetString(row, "Supplier")
                        : $"Supplier #{supplierId}"
                },
            ReportKind.SupplierOutstanding => !TryGetInt(row, "SupplierId", out var outSupplierId) || outSupplierId <= 0
                ? null
                : new ReportBillDrillDownQuery
                {
                    Kind = kind,
                    From = FromDate,
                    To = ToDate,
                    BranchId = _branchId,
                    SupplierId = outSupplierId,
                    OpenBillsOnly = true,
                    Title = GetString(row, "Name").Length > 0
                        ? GetString(row, "Name")
                        : $"Supplier #{outSupplierId}"
                },
            ReportKind.CustomerOutstanding => !TryGetInt(row, "CustomerId", out var customerId) || customerId <= 0
                ? null
                : new ReportBillDrillDownQuery
                {
                    Kind = kind,
                    From = FromDate,
                    To = ToDate,
                    BranchId = _branchId,
                    CustomerId = customerId,
                    OpenBillsOnly = true,
                    Title = GetString(row, "Name").Length > 0
                        ? GetString(row, "Name")
                        : $"Customer #{customerId}"
                },
            _ => null
        };
    }

    private Task OpenListedBillAsync(ReportBillListRowDto bill)
        => bill.DocumentKind == ReportDocumentKind.Purchase
            ? _invoiceViewer.ShowPurchaseAsync(bill.DocumentId)
            : _invoiceViewer.ShowSaleAsync(bill.DocumentId);

    public Task AddSelectedToShortageBookAsync()
        => AddRowToShortageBookAsync(SelectedRow);

    public async Task AddRowToShortageBookAsync(ReportRowViewModel? row)
    {
        if (row is null || !ShowShortageBookActions) return;
        if (!TryGetInt(row, "MedicineId", out var medicineId) || medicineId <= 0)
        {
            _dialog.ShowInfo("Select a medicine row first.", "Shortage book");
            return;
        }

        var medicineName = GetString(row, "MedicineName");
        if (string.IsNullOrWhiteSpace(medicineName))
            medicineName = $"Medicine #{medicineId}";

        try
        {
            var onHand = await _shortageBook.GetOnHandQuantityAsync(medicineId, _branchId);
            var defaultRequested = Math.Max(1m, onHand > 0 ? onHand + 1 : 1m);
            var prompt = _dialog.PromptShortageDetails(
                medicineName,
                defaultRequested,
                detailLine: $"On hand: {onHand:0.##}. Wanted quantity and customer name are optional.");
            if (prompt is null) return;

            var result = await _shortageBook.RecordAsync(
                new RecordShortageRequest(
                    medicineId,
                    prompt.WantedQuantity,
                    onHand,
                    ShortageSource.Manual,
                    prompt.CustomerName),
                _branchId,
                _currentUser.CurrentUser?.FullName ?? _currentUser.CurrentUser?.Username);

            if (result.IsFailure)
            {
                _dialog.ShowError(result.Error ?? "Could not record shortage.");
                return;
            }

            StatusMessage = $"Added to shortage book: {result.Value!.MedicineName}";
            _dialog.ShowInfo($"Added to shortage book: {result.Value.MedicineName}", "Shortage book");
        }
        catch (Exception ex)
        {
            _dialog.ShowError(ex.Message);
        }
    }

    private void PrintScheduleRegister()
    {
        if (_scheduleRegisterReport is null || Rows.Count == 0)
        {
            _dialog.ShowInfo("Run the Schedule H / H1 register first.");
            return;
        }

        var visibleKeys = Rows
            .Select(r => (
                TryGetInt(r, "SaleId", out var id) ? id : 0,
                GetString(r, "InvoiceNumber"),
                GetString(r, "MedicineName"),
                GetString(r, "BatchNumber")))
            .ToHashSet();

        var printable = new ScheduleRegisterReportDto
        {
            FromDate = _scheduleRegisterReport.FromDate,
            ToDate = _scheduleRegisterReport.ToDate,
            Filter = _scheduleRegisterReport.Filter,
            FilterLabel = _scheduleRegisterReport.FilterLabel,
            CompanyName = _scheduleRegisterReport.CompanyName,
            DrugLicenseNumber = _scheduleRegisterReport.DrugLicenseNumber,
            Address = _scheduleRegisterReport.Address,
            Phone = _scheduleRegisterReport.Phone,
            Rows = _scheduleRegisterReport.Rows
                .Where(r => visibleKeys.Contains((r.SaleId, r.InvoiceNumber, r.MedicineName, r.BatchNumber ?? "")))
                .ToList()
        };
        _printService.ShowScheduleRegisterPreview(printable);
    }

    private void ApplyPreset(DateTime from, DateTime to)
    {
        FromDate = from;
        ToDate = to;
    }

    private void ApplyActiveFinancialYearDates()
    {
        var fy = _financialYear.Active;
        FromDate = fy.Start;
        var lastDay = fy.EndExclusive.AddDays(-1);
        ToDate = fy.IsCurrent && DateTime.Today < lastDay
            ? DateTime.Today
            : lastDay;
    }

    private void RefreshFilterOptions()
    {
        var preset = _definition.FilterPresets.FirstOrDefault();
        _filterOptions = preset switch
        {
            FilterPreset.PaymentStatus =>
            [
                FilterOption.All,
                new("due", "Pending + partial paid"),
                new("unpaid", "Pending / unpaid"),
                new("partial", "Partially paid"),
                new("paid", "Fully paid")
            ],
            FilterPreset.GstDocumentType =>
            [
                FilterOption.All,
                new("sale", "Sales only"),
                new("purchase", "Purchases only")
            ],
            FilterPreset.Gstr1Section =>
            [
                FilterOption.All,
                new("B2B", "B2B"),
                new("B2CS", "B2CS"),
                new("CDNR", "Credit notes")
            ],
            FilterPreset.Gstr2BSection =>
            [
                FilterOption.All,
                new("B2B", "B2B registered"),
                new("Unregistered", "Unregistered"),
                new("CDN", "Credit/debit notes")
            ],
            FilterPreset.ExpiryWindow =>
            [
                new("expired", "Expired"),
                new("1", "1 month"),
                new("2", "2 months"),
                new("3", "3 months"),
                new("4", "4 months"),
                new("5", "5 months"),
                new("6", "6 months"),
                new("7", "7 months"),
                new("8", "8 months"),
                new("9", "9 months"),
                new("10", "10 months"),
                new("11", "11 months"),
                new("12", "12 months")
            ],
            FilterPreset.LowStockSeverity =>
            [
                FilterOption.All,
                new("critical", "Out of stock"),
                new("low", "Below reorder")
            ],
            FilterPreset.SaleReturnMode =>
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
        _selectedExpirySupplierKey = "all";
        OnPropertyChanged(nameof(FilterText));
        OnPropertyChanged(nameof(SelectedFilterOption));
        OnPropertyChanged(nameof(SelectedExpirySupplierKey));
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
                var table = await _reports.GetReportTableAsync(
                    SelectedReport.Kind, FromDate, ToDate, _branchId,
                    SelectedScheduleFilter.Filter, token);
                if (runId != _runId) return;

                ApplyTable(table);
                ApplyFilters();
                CommandManager.InvalidateRequerySuggested();
            }
            catch (OperationCanceledException)
            {
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

    private void ApplyTable(ReportTableDto table)
    {
        _columns = table.Columns;
        _allRows = table.Rows;
        GstSummary = table.GstSummary;
        _gstReturnExport = table.GstReturnExport;
        _scheduleRegisterReport = table.ScheduleRegister;
        if (SelectedReport.Kind == ReportKind.Expiry)
            _expiryFilterToday = DateTime.Today;

        Columns.Clear();
        foreach (var c in _columns)
            Columns.Add(c);
        ColumnsChanged?.Invoke();

        OnPropertyChanged(nameof(GstReturnDisclaimer));
        OnPropertyChanged(nameof(ShowGstReturnExport));
        OnPropertyChanged(nameof(ShowGstSummary));
    }

    private void ApplyFilters()
    {
        var term = FilterText.Trim();
        var option = SelectedFilterOption.Key;
        IEnumerable<Dictionary<string, object?>> filtered = _allRows;

        if (SelectedReport.Kind == ReportKind.Expiry)
        {
            var windowRows = _allRows.Where(r => MatchesExpiryWindowAndText(r, term)).ToList();
            RefreshExpirySupplierOptions(windowRows);
            filtered = windowRows.Where(MatchesSelectedSupplier);
        }
        else
        {
            filtered = _allRows.Where(r =>
                MatchesText(term, r) &&
                MatchesOption(option, r));
        }

        Rows.Clear();
        foreach (var row in filtered)
            Rows.Add(new ReportRowViewModel(row));

        UpdateFilteredSummary();
        OnPropertyChanged(nameof(HasActiveFilter));
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(HasSourceData));
        OnPropertyChanged(nameof(ShowFilters));
        OnPropertyChanged(nameof(ShowExpirySupplierFilter));
        OnPropertyChanged(nameof(HasNoFilterMatches));
        OnPropertyChanged(nameof(ShowStockSummaryKpis));
        OnPropertyChanged(nameof(ShowGenericSummaryKpis));
        OnPropertyChanged(nameof(ShowTaxDiscountKpis));
        OnPropertyChanged(nameof(ShowPaymentSummaryKpis));
        OnPropertyChanged(nameof(ShowScheduleSummaryKpis));
        OnPropertyChanged(nameof(ShowGstReturnKpis));
        CommandManager.InvalidateRequerySuggested();
    }

    private bool MatchesText(string term, Dictionary<string, object?> row)
    {
        if (string.IsNullOrWhiteSpace(term)) return true;
        return row.Values.Any(v =>
            v is not null &&
            v.ToString() is { Length: > 0 } s &&
            s.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private bool MatchesOption(string option, Dictionary<string, object?> row)
    {
        if (option is "all" or "") return true;

        return SelectedReport.Kind switch
        {
            ReportKind.Sales or ReportKind.Purchases or ReportKind.SupplierPayments => option switch
            {
                "due" => ToDecimal(Get(row, "BalanceDue")) > 0,
                "unpaid" => ToDecimal(Get(row, "PaidAmount")) <= 0
                            && ToDecimal(Get(row, "AdjustedAmount")) <= 0
                            && ToDecimal(Get(row, "BalanceDue")) > 0,
                "partial" => (ToDecimal(Get(row, "PaidAmount")) > 0
                              || ToDecimal(Get(row, "AdjustedAmount")) > 0)
                             && ToDecimal(Get(row, "BalanceDue")) > 0,
                "paid" => ToDecimal(Get(row, "BalanceDue")) <= 0,
                _ => true
            },
            ReportKind.GstSummary => option switch
            {
                "sale" => GetString(row, "DocumentType").Contains("Sale", StringComparison.OrdinalIgnoreCase),
                "purchase" => GetString(row, "DocumentType").Contains("Purchase", StringComparison.OrdinalIgnoreCase),
                _ => true
            },
            ReportKind.Gstr1 or ReportKind.Gstr2B =>
                string.Equals(GetString(row, "Section"), option, StringComparison.OrdinalIgnoreCase),
            ReportKind.LowStock => option switch
            {
                "critical" => IsTruthy(Get(row, "IsCritical")),
                "low" => !IsTruthy(Get(row, "IsCritical")),
                _ => true
            },
            ReportKind.SaleReturns => option switch
            {
                "full" => IsTruthy(Get(row, "IsFullReturn")) ||
                          string.Equals(GetString(row, "IsFullReturn"), "Yes", StringComparison.OrdinalIgnoreCase),
                "partial" => !(IsTruthy(Get(row, "IsFullReturn")) ||
                               string.Equals(GetString(row, "IsFullReturn"), "Yes", StringComparison.OrdinalIgnoreCase)),
                "cash" => IsCashRefund(GetString(row, "RefundMode")),
                "credit" => IsCreditRefund(GetString(row, "RefundMode")),
                _ => true
            },
            _ => true
        };
    }

    private bool MatchesExpiryWindowAndText(Dictionary<string, object?> row, string term)
    {
        if (!MatchesText(term, row)) return false;

        var expiryObj = Get(row, "ExpiryDate");
        DateTime? expiryDate = expiryObj switch
        {
            DateTime dt => dt.Date,
            DateTimeOffset dto => dto.Date,
            string s when DateTime.TryParse(s, out var parsed) => parsed.Date,
            _ => null
        };
        if (expiryDate is null)
        {
            // Fall back to ExpiryLabel parsing is unreliable; use ExpiryStatus for expired.
            var status = GetString(row, "ExpiryStatus");
            if (SelectedFilterOption.Key == "expired")
                return status.Contains("Expired", StringComparison.OrdinalIgnoreCase);
            return true;
        }

        var today = _expiryFilterToday.Date;
        var key = SelectedFilterOption.Key;
        if (key == "expired")
            return expiryDate < today;

        if (int.TryParse(key, out var months) && months is >= 1 and <= 12)
        {
            if (expiryDate < today) return false;
            return expiryDate <= today.AddMonths(months);
        }

        return true;
    }

    private bool MatchesSelectedSupplier(Dictionary<string, object?> row)
    {
        if (SelectedExpirySupplierKey == "all") return true;
        var id = Get(row, "SupplierId")?.ToString();
        if (string.Equals(id, SelectedExpirySupplierKey, StringComparison.Ordinal))
            return true;
        var selectedLabel = _expirySupplierOptions
            .FirstOrDefault(o => o.Key == SelectedExpirySupplierKey)?.Label;
        var name = GetString(row, "SupplierName");
        return !string.IsNullOrWhiteSpace(selectedLabel) &&
               !string.IsNullOrWhiteSpace(name) &&
               string.Equals(name.Trim(), selectedLabel.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshExpirySupplierOptions(IReadOnlyList<Dictionary<string, object?>> windowRows)
    {
        var options = windowRows
            .Select(r => (Id: Get(r, "SupplierId"), Name: GetString(r, "SupplierName")))
            .Where(x => x.Id is int and > 0 && !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => (int)x.Id!)
            .OrderBy(g => g.First().Name)
            .Select(g => new FilterOption(g.Key.ToString(), g.First().Name.Trim()))
            .ToList();

        var newKeys = options.Select(o => o.Key).Append("all").ToHashSet(StringComparer.Ordinal);
        var oldKeys = _expirySupplierOptions.Select(o => o.Key).ToHashSet(StringComparer.Ordinal);
        if (newKeys.SetEquals(oldKeys)) return;

        _expirySupplierOptions = [FilterOption.All, .. options];
        OnPropertyChanged(nameof(ExpirySupplierOptions));
        if (!newKeys.Contains(SelectedExpirySupplierKey))
        {
            _selectedExpirySupplierKey = "all";
            OnPropertyChanged(nameof(SelectedExpirySupplierKey));
        }
    }

    private void UpdateFilteredSummary()
    {
        var count = Rows.Count;
        var amount = SumKey("GrandTotal", "Total", "Revenue", "RefundAmount", "StockAmount", "InvoiceValue", "OutstandingBalance", "Amount");
        var tax = SumKey("TaxAmount", "TotalTax", "Cost", "StockValue", "CgstAmount");
        var discount = SumKey("DiscountAmount", "GrossProfit", "TaxableAmount", "Shortfall");

        // Prefer typed summary keys per report family.
        amount = SelectedReport.Kind switch
        {
            ReportKind.StockValuation => SumKey("StockAmount"),
            ReportKind.Expiry or ReportKind.BatchStock or ReportKind.SlowMovingStock => SumKey("StockValue"),
            ReportKind.Profit => SumKey("Revenue"),
            ReportKind.SalesByMedicine or ReportKind.MedicinesSoldByDate => SumKey("Revenue"),
            ReportKind.StockAdjustments => SumKey("Difference"),
            ReportKind.ScheduleRegister => SumKey("Quantity"),
            ReportKind.Gstr1 or ReportKind.Gstr2B => SumKey("InvoiceValue"),
            ReportKind.SaleReturns or ReportKind.MedicineReturns => SumKey("RefundAmount"),
            ReportKind.SupplierPayments => SumKey("GrandTotal"),
            ReportKind.LowStock => 0m,
            _ => amount
        };
        tax = SelectedReport.Kind switch
        {
            ReportKind.StockValuation => SumKey("StockValue"),
            ReportKind.Profit or ReportKind.SalesByMedicine => SumKey("Cost"),
            ReportKind.Gstr1 or ReportKind.Gstr2B => SumKey("TotalTax"),
            ReportKind.GstSummary => SumKey("TotalTax"),
            ReportKind.SupplierPayments => SumKey("PaidAmount"),
            _ => tax
        };
        discount = SelectedReport.Kind switch
        {
            ReportKind.Profit or ReportKind.SalesByMedicine => SumKey("GrossProfit"),
            ReportKind.Gstr1 or ReportKind.Gstr2B => SumKey("TaxableAmount"),
            ReportKind.LowStock => SumKey("Shortfall"),
            ReportKind.StockAdjustments => SumKey("SystemQuantity"),
            ReportKind.SupplierPayments => SumKey("BalanceDue"),
            _ => discount
        };

        var adjustedPayments = SelectedReport.Kind == ReportKind.SupplierPayments
            ? SumKey("AdjustedAmount")
            : 0m;

        var totalSource = _allRows.Count;
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
                : SelectedReport.Kind == ReportKind.StockValuation
                    ? $"{count} batch(es) — Amount {amount:N2} · Cost {tax:N2}"
                    : SelectedReport.Kind == ReportKind.ScheduleRegister
                        ? $"{count} line(s) — total qty {amount:0.##}"
                        : SelectedReport.Kind == ReportKind.SupplierPayments
                            ? $"{count} bill(s) — paid {tax:N2} · adjusted {adjustedPayments:N2} · pending {discount:N2}"
                        : ShowGstReturnExport
                            ? $"{count} GST line(s) — taxable {discount:N2} · tax {tax:N2}"
                            : $"{count} record(s) — total {amount:N2}"
        };
        StatusMessage = Summary.FooterNote;
    }

    private decimal SumKey(params string[] keys)
    {
        decimal total = 0;
        foreach (var row in Rows)
        {
            foreach (var key in keys)
            {
                if (row.Values.ContainsKey(key))
                {
                    total += ToDecimal(row.Get(key));
                    break;
                }
            }
        }
        return total;
    }

    private void ClearAllRows()
    {
        Rows.Clear();
        Columns.Clear();
        _columns = [];
        _allRows = [];
        _gstReturnExport = null;
        _scheduleRegisterReport = null;
        _expirySupplierOptions = [FilterOption.All];
        _selectedExpirySupplierKey = "all";
        OnPropertyChanged(nameof(ExpirySupplierOptions));
        OnPropertyChanged(nameof(SelectedExpirySupplierKey));
        OnPropertyChanged(nameof(ShowExpirySupplierFilter));
        Summary = new ReportSummaryDto();
        ColumnsChanged?.Invoke();
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
            ReportCsvExporter.ExportTable(dialog.FileName, _columns, Rows.Select(r => r.Values));
            _dialog.ShowInfo($"Exported to {dialog.FileName}");
        }
        catch (Exception ex)
        {
            _dialog.ShowError($"Export failed: {ex.Message}");
        }
    }

    private void ExportExcel()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Excel workbook (*.xlsx)|*.xlsx",
            FileName = $"{SelectedReport.Kind}_{DateTime.Today:yyyyMMdd}.xlsx"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            ReportExcelExporter.Export(
                dialog.FileName,
                SelectedReport.Label,
                _columns,
                Rows.Select(r => r.Values));
            _dialog.ShowInfo($"Exported to {dialog.FileName}");
        }
        catch (Exception ex)
        {
            _dialog.ShowError($"Export failed: {ex.Message}");
        }
    }

    private void ExportPdf()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            FileName = $"{SelectedReport.Kind}_{DateTime.Today:yyyyMMdd}.pdf"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var subtitle = UsesDateRange
                ? $"{FromDate:dd MMM yyyy} – {ToDate:dd MMM yyyy}"
                : "As of today";
            ReportPdfExporter.Export(
                dialog.FileName,
                SelectedReport.Label,
                subtitle,
                _columns,
                Rows.Select(r => r.Values));
            _dialog.ShowInfo($"Exported to {dialog.FileName}");
        }
        catch (Exception ex)
        {
            _dialog.ShowError($"Export failed: {ex.Message}");
        }
    }

    private void ExportGstJson()
    {
        if (_gstReturnExport is null) return;
        var kind = _gstReturnExport.Kind == GstReturnKind.Gstr1 ? "GSTR1" : "GSTR2B";
        var dialog = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
            FileName = $"{kind}_{_gstReturnExport.FilingPeriod}.json"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, _gstReturnExport.JsonPayload);
            _dialog.ShowInfo($"Exported to {dialog.FileName}");
        }
        catch (Exception ex)
        {
            _dialog.ShowError($"Export failed: {ex.Message}");
        }
    }

    private void ExportGstExcel()
    {
        if (_gstReturnExport is null) return;
        var kind = _gstReturnExport.Kind == GstReturnKind.Gstr1 ? "GSTR1" : "GSTR2B";
        var dialog = new SaveFileDialog
        {
            Filter = "Excel workbook (*.xlsx)|*.xlsx",
            FileName = $"{kind}_{_gstReturnExport.FilingPeriod}.xlsx"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            GstReturnWorkbookWriter.Write(dialog.FileName, _gstReturnExport.ExcelSheets);
            _dialog.ShowInfo($"Exported to {dialog.FileName}");
        }
        catch (Exception ex)
        {
            _dialog.ShowError($"Export failed: {ex.Message}");
        }
    }

    private static object? Get(Dictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var v) ? v : null;

    private static string GetString(Dictionary<string, object?> row, string key) =>
        Get(row, key)?.ToString() ?? "";

    private static string GetString(ReportRowViewModel row, string key) =>
        row.Get(key)?.ToString() ?? "";

    private static bool TryGetInt(ReportRowViewModel row, string key, out int value)
    {
        value = 0;
        var v = row.Get(key);
        switch (v)
        {
            case int i:
                value = i;
                return true;
            case long l:
                value = (int)l;
                return true;
            case string s when int.TryParse(s, out var parsed):
                value = parsed;
                return true;
            default:
                return false;
        }
    }

    private static decimal ToDecimal(object? value) => value switch
    {
        null => 0m,
        decimal d => d,
        double d => (decimal)d,
        float f => (decimal)f,
        int i => i,
        long l => l,
        string s when decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) => d,
        string s when decimal.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out var d2) => d2,
        _ => 0m
    };

    private static bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        string s => s.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                    s.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                    s == "1",
        int i => i != 0,
        _ => false
    };

    private static bool IsCashRefund(string mode) =>
        mode.Contains("Cash", StringComparison.OrdinalIgnoreCase) ||
        mode.Contains("Card", StringComparison.OrdinalIgnoreCase) ||
        mode.Contains("Upi", StringComparison.OrdinalIgnoreCase) ||
        mode.Contains("Wallet", StringComparison.OrdinalIgnoreCase);

    private static bool IsCreditRefund(string mode) =>
        mode.Contains("Credit", StringComparison.OrdinalIgnoreCase) ||
        mode.Contains("Store", StringComparison.OrdinalIgnoreCase);
}

public sealed class ReportRowViewModel(Dictionary<string, object?> values)
{
    public Dictionary<string, object?> Values { get; } = values;

    public object? Get(string key) =>
        Values.TryGetValue(key, out var v) ? v : null;

    public object? this[string key] => Get(key);
}

public sealed class FilterOption(string key, string label)
{
    public static FilterOption All { get; } = new("all", "All");
    public string Key { get; } = key;
    public string Label { get; } = label;
    public override string ToString() => Label;
}

public sealed class ScheduleRegisterFilterOption(ScheduleRegisterFilter filter, string label)
{
    public ScheduleRegisterFilter Filter { get; } = filter;
    public string Label { get; } = label;
    public override string ToString() => Label;
}
