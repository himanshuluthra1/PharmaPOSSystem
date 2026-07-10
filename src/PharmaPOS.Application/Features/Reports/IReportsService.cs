namespace PharmaPOS.Application.Features.Reports;

public interface IReportsService
{
    Task<(ReportSummaryDto Summary, List<SalesReportRowDto> Rows)> GetSalesReportAsync(
        DateTime from, DateTime to, int? branchId, CancellationToken ct = default);

    Task<(ReportSummaryDto Summary, List<PurchaseReportRowDto> Rows)> GetPurchaseReportAsync(
        DateTime from, DateTime to, int? branchId, CancellationToken ct = default);

    Task<(GstSummaryDto Summary, List<GstDetailRowDto> Rows)> GetGstReportAsync(
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
}
