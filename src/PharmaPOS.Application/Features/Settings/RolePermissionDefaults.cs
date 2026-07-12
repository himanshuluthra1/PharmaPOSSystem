using PharmaPOS.Shared.Constants;

namespace PharmaPOS.Application.Features.Settings;

/// <summary>Default granular permissions granted to each built-in role.</summary>
public static class RolePermissionDefaults
{
    private static readonly string[] All = PermissionCatalog.All.Select(p => p.Key).ToArray();

    public static IReadOnlyDictionary<string, string[]> Map { get; } = new Dictionary<string, string[]>
    {
        [AppConstants.Roles.SuperAdmin] = All,
        [AppConstants.Roles.Admin] = All,
        [AppConstants.Roles.Manager] =
        [
            AppConstants.Permissions.DashboardView,
            AppConstants.Permissions.SalesView,
            AppConstants.Permissions.SalesCreate,
            AppConstants.Permissions.SalesDiscount,
            AppConstants.Permissions.SalesPrint,
            AppConstants.Permissions.SalesReturn,
            AppConstants.Permissions.SalesReturnHighValue,
            AppConstants.Permissions.SalesReturnOverride,
            AppConstants.Permissions.PurchaseView,
            AppConstants.Permissions.PurchaseCreate,
            AppConstants.Permissions.PurchaseSearch,
            AppConstants.Permissions.InventoryView,
            AppConstants.Permissions.InventoryAdjust,
            AppConstants.Permissions.MastersView,
            AppConstants.Permissions.MastersEdit,
            AppConstants.Permissions.ReportsView,
            AppConstants.Permissions.ReportsExport,
        ],
        [AppConstants.Roles.Pharmacist] =
        [
            AppConstants.Permissions.DashboardView,
            AppConstants.Permissions.SalesView,
            AppConstants.Permissions.SalesCreate,
            AppConstants.Permissions.SalesDiscount,
            AppConstants.Permissions.SalesPrint,
            AppConstants.Permissions.SalesReturn,
            AppConstants.Permissions.InventoryView,
            AppConstants.Permissions.MastersView,
        ],
        [AppConstants.Roles.Cashier] =
        [
            AppConstants.Permissions.DashboardView,
            AppConstants.Permissions.SalesView,
            AppConstants.Permissions.SalesCreate,
            AppConstants.Permissions.SalesPrint,
            AppConstants.Permissions.SalesReturn,
        ],
        [AppConstants.Roles.Accountant] =
        [
            AppConstants.Permissions.DashboardView,
            AppConstants.Permissions.PurchaseView,
            AppConstants.Permissions.PurchaseCreate,
            AppConstants.Permissions.PurchaseSearch,
            AppConstants.Permissions.AccountingView,
            AppConstants.Permissions.AccountingVouchers,
            AppConstants.Permissions.AccountingJournal,
            AppConstants.Permissions.ReportsView,
            AppConstants.Permissions.ReportsExport,
        ],
    };

    public static string[] ForRole(string roleName)
        => Map.TryGetValue(roleName, out var keys)
            ? keys
            : [AppConstants.Permissions.DashboardView];
}
