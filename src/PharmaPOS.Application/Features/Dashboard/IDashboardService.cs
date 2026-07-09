namespace PharmaPOS.Application.Features.Dashboard;

public interface IDashboardService
{
    Task<DashboardDto> GetDashboardAsync(int? branchId = null, CancellationToken ct = default);
}
