using Microsoft.Extensions.DependencyInjection;
using PharmaPOS.Application.Features.Authentication;
using PharmaPOS.Application.Features.Dashboard;
using PharmaPOS.Application.Features.Masters;
using PharmaPOS.Application.Features.Accounting;
using PharmaPOS.Application.Features.Inventory;
using PharmaPOS.Application.Features.Reports;
using PharmaPOS.Application.Features.Settings;
using PharmaPOS.Application.Features.Purchases;
using PharmaPOS.Application.Features.Sales;

namespace PharmaPOS.Application;

/// <summary>Registers Application-layer services into the DI container.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<ILinkedMedWinIdCache, LinkedMedWinIdCache>();
        services.AddSingleton<IMedicineMedWinMappingBackfillService, MedicineMedWinMappingBackfillService>();
        services.AddTransient<IAuthService, AuthService>();
        services.AddTransient<IDashboardService, DashboardService>();
        services.AddTransient<ISalesService, SalesService>();
        services.AddTransient<IPurchaseService, PurchaseService>();
        services.AddTransient<IMastersService, MastersService>();
        services.AddTransient<IMedicineMappingService, MedicineMappingService>();
        services.AddTransient<IInventoryService, InventoryService>();
        services.AddTransient<IAccountingService, AccountingService>();
        services.AddTransient<IReportsService, ReportsService>();
        services.AddTransient<ISettingsService, SettingsService>();
        return services;
    }
}
