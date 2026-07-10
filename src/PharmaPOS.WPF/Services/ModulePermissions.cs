using PharmaPOS.WPF.ViewModels;
using PharmaPOS.WPF.ViewModels.Accounting;
using PharmaPOS.WPF.ViewModels.Inventory;
using PharmaPOS.WPF.ViewModels.Masters;
using PharmaPOS.WPF.ViewModels.Purchases;
using PharmaPOS.WPF.ViewModels.Reports;
using PharmaPOS.WPF.ViewModels.Sales;
using PharmaPOS.WPF.ViewModels.Settings;

namespace PharmaPOS.WPF.Services;

/// <summary>Maps shell view models to the application module used for access checks.</summary>
public static class ModulePermissions
{
    public static readonly IReadOnlyDictionary<Type, string> ViewModelMap = new Dictionary<Type, string>
    {
        [typeof(DashboardViewModel)] = "dashboard",
        [typeof(SalesViewModel)] = "sales",
        [typeof(PurchaseViewModel)] = "purchase",
        [typeof(InventoryViewModel)] = "inventory",
        [typeof(MastersViewModel)] = "masters",
        [typeof(AccountingViewModel)] = "accounting",
        [typeof(ReportsViewModel)] = "reports",
        [typeof(SettingsViewModel)] = "settings",
    };

    public static string? For(Type viewModelType)
        => ViewModelMap.TryGetValue(viewModelType, out var module) ? module : null;
}
