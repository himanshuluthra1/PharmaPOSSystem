using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.SaleReturns;

public interface ISaleReturnService
{
    Task<SaleReturnPolicyDto> GetPolicyAsync(CancellationToken ct = default);
    Task<List<ReturnReasonDto>> ListReturnReasonsAsync(CancellationToken ct = default);
    Task<List<SaleReturnSearchResultDto>> SearchSalesAsync(
        SaleReturnSearchType type, string term, int? branchId, CancellationToken ct = default);
    Task<Result<SaleForReturnDto>> GetSaleForReturnAsync(int saleId, int? branchId, CancellationToken ct = default);
  Task<Result<SaleReturnReceiptDto>> CreateReturnAsync(
        CreateSaleReturnRequest request, int? branchId, string? userName, CancellationToken ct = default);
    Task<Result<SaleReturnReceiptDto>> GetReturnReceiptAsync(int saleReturnId, CancellationToken ct = default);
    Task<List<SaleReturnSummaryRowDto>> ListReturnsAsync(DateTime from, DateTime to, int? branchId, CancellationToken ct = default);
    Task<List<MedicineReturnReportRowDto>> GetMedicineReturnReportAsync(DateTime from, DateTime to, int? branchId, CancellationToken ct = default);
    Task<DailySaleReturnReportDto> GetDailyReturnSummaryAsync(DateTime date, int? branchId, CancellationToken ct = default);
}
