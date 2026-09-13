using PharmaPOS.Application.Features.SaleReturns;

namespace PharmaPOS.Application.Features.Reports;

public interface IReportsService
{
    Task<ReportTableDto> GetReportTableAsync(
        ReportKind kind,
        DateTime from,
        DateTime to,
        int? branchId,
        ScheduleRegisterFilter scheduleFilter = ScheduleRegisterFilter.HAndH1,
        CancellationToken ct = default);

    Task<(ReportSummaryDto Summary, List<SalesReportRowDto> Rows)> GetSalesReportAsync(
        DateTime from, DateTime to, int? branchId, CancellationToken ct = default);

    Task<(ReportSummaryDto Summary, List<PurchaseReportRowDto> Rows)> GetPurchaseReportAsync(
        DateTime from, DateTime to, int? branchId, CancellationToken ct = default);

    Task<(GstSummaryDto Summary, List<GstDetailRowDto> Rows)> GetGstReportAsync(
        DateTime from, DateTime to, int? branchId, CancellationToken ct = default);

    Task<GstReturnExportDto> GetGstr1ExportAsync(
        DateTime from, DateTime to, int? branchId, CancellationToken ct = default);

    Task<GstReturnExportDto> GetGstr2BExportAsync(
        DateTime from, DateTime to, int? branchId, CancellationToken ct = default);

    Task<(ReportSummaryDto Summary, List<ProfitReportRowDto> Rows)> GetProfitReportAsync(
        DateTime from, DateTime to, int? branchId, CancellationToken ct = default);

    Task<(ReportSummaryDto Summary, List<MedicineSalesRowDto> Rows)> GetSalesByMedicineReportAsync(
        DateTime from, DateTime to, int? branchId, CancellationToken ct = default);

    Task<(ReportSummaryDto Summary, List<StockValuationReportRowDto> Rows)> GetStockValuationReportAsync(
        int? branchId, CancellationToken ct = default);

    Task<(ReportSummaryDto Summary, List<ExpiryReportRowDto> Rows)> GetExpiryReportAsync(
        int? branchId, CancellationToken ct = default);

    Task<(ReportSummaryDto Summary, List<LowStockReportRowDto> Rows)> GetLowStockReportAsync(
        int? branchId, CancellationToken ct = default);

    Task<(ReportSummaryDto Summary, ScheduleRegisterReportDto Report)> GetScheduleRegisterAsync(
        DateTime from,
        DateTime to,
        int? branchId,
        ScheduleRegisterFilter filter = ScheduleRegisterFilter.HAndH1,
        CancellationToken ct = default);

    /// <summary>
    /// Bills for one customer key in the period (same grouping as Sales By Customer:
    /// registered customer name, else billing name, else "Walk-in").
    /// </summary>
    Task<IReadOnlyList<CustomerSaleBillRowDto>> ListCustomerSalesAsync(
        DateTime from,
        DateTime to,
        string customerKey,
        int? branchId,
        CancellationToken ct = default);

    /// <summary>List sale/purchase bills underlying a consolidated report row.</summary>
    Task<IReadOnlyList<ReportBillListRowDto>> ListUnderlyingBillsAsync(
        ReportBillDrillDownQuery query,
        CancellationToken ct = default);
}

/// <summary>Optional sale-return data provider used by the unified report table pipeline.</summary>
public interface IReportSaleReturnSource
{
    Task<(ReportSummaryDto Summary, List<SaleReturnSummaryRowDto> Rows)> GetSaleReturnsAsync(
        DateTime from, DateTime to, int? branchId, CancellationToken ct = default);

    Task<(ReportSummaryDto Summary, List<MedicineReturnReportRowDto> Rows)> GetMedicineReturnsAsync(
        DateTime from, DateTime to, int? branchId, CancellationToken ct = default);
}
