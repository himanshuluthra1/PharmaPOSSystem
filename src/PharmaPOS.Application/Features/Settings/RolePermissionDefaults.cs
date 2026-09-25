using PharmaPOS.Shared.Constants;

namespace PharmaPOS.Application.Features.Settings;

/// <summary>Default granular permissions granted to each built-in role.</summary>
public static class RolePermissionDefaults
{
    private static readonly string[] All = PermissionCatalog.All.Select(p => p.Key).ToArray();

    private static readonly string[] PurchaseMenus =
    [
        AppConstants.Permissions.PurchaseMenuInvoice,
        AppConstants.Permissions.PurchaseMenuOrder,
        AppConstants.Permissions.PurchaseMenuReturn,
        AppConstants.Permissions.PurchaseMenuExpiry,
    ];

    private static readonly string[] InventoryMenus =
    [
        AppConstants.Permissions.InventoryMenuOnhand,
        AppConstants.Permissions.InventoryMenuLedger,
        AppConstants.Permissions.InventoryMenuAdjustment,
        AppConstants.Permissions.InventoryMenuTransfer,
        AppConstants.Permissions.InventoryMenuTransferHistory,
        AppConstants.Permissions.InventoryMenuShortage,
    ];

    private static readonly string[] MastersMenus =
    [
        AppConstants.Permissions.MastersMenuSuppliers,
        AppConstants.Permissions.MastersMenuCustomers,
        AppConstants.Permissions.MastersMenuDoctors,
        AppConstants.Permissions.MastersMenuManufacturers,
        AppConstants.Permissions.MastersMenuEmployees,
        AppConstants.Permissions.MastersMenuMedicines,
    ];

    private static readonly string[] AccountingMenus =
    [
        AppConstants.Permissions.AccountingMenuParties,
        AppConstants.Permissions.AccountingMenuDues,
        AppConstants.Permissions.AccountingMenuVouchers,
        AppConstants.Permissions.AccountingMenuCashbook,
        AppConstants.Permissions.AccountingMenuJournal,
    ];

    public static IReadOnlyDictionary<string, string[]> Map { get; } = new Dictionary<string, string[]>
    {
        [AppConstants.Roles.SuperAdmin] = All,
        [AppConstants.Roles.Admin] = All,
        [AppConstants.Roles.Manager] =
        [
            AppConstants.Permissions.DashboardView,
            AppConstants.Permissions.SalesView,
            AppConstants.Permissions.SalesCreate,
            AppConstants.Permissions.SalesEdit,
            AppConstants.Permissions.SalesUnlock,
            AppConstants.Permissions.SalesDiscount,
            AppConstants.Permissions.SalesPrint,
            AppConstants.Permissions.SalesReturn,
            AppConstants.Permissions.SalesReturnHighValue,
            AppConstants.Permissions.SalesReturnOverride,
            AppConstants.Permissions.PurchaseView,
            AppConstants.Permissions.PurchaseCreate,
            AppConstants.Permissions.PurchaseEdit,
            AppConstants.Permissions.PurchaseUnlock,
            AppConstants.Permissions.PurchaseSearch,
            AppConstants.Permissions.PurchaseReturn,
            ..PurchaseMenus,
            AppConstants.Permissions.InventoryView,
            AppConstants.Permissions.InventoryAdjust,
            AppConstants.Permissions.InventoryTransfer,
            ..InventoryMenus,
            AppConstants.Permissions.MastersView,
            AppConstants.Permissions.MastersEdit,
            ..MastersMenus,
            AppConstants.Permissions.ReportsView,
            AppConstants.Permissions.ReportsExport,
            ..ReportMenuPermissions.AllKeys,
            AppConstants.Permissions.SettingsMenuPassword,
            AppConstants.Permissions.SettingsMenuAppearance,
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
            AppConstants.Permissions.InventoryMenuOnhand,
            AppConstants.Permissions.InventoryMenuLedger,
            AppConstants.Permissions.MastersView,
            AppConstants.Permissions.MastersMenuMedicines,
            AppConstants.Permissions.MastersMenuCustomers,
            AppConstants.Permissions.MastersMenuDoctors,
            AppConstants.Permissions.SettingsMenuPassword,
            AppConstants.Permissions.SettingsMenuAppearance,
        ],
        // Captured from vendor PC Cashier role (Settings → Roles & Permissions) for all shops.
        [AppConstants.Roles.Cashier] =
        [
            AppConstants.Permissions.DashboardView,

            AppConstants.Permissions.SalesView,
            AppConstants.Permissions.SalesCreate,
            AppConstants.Permissions.SalesEdit,
            AppConstants.Permissions.SalesUnlock,
            AppConstants.Permissions.SalesDiscount,
            AppConstants.Permissions.SalesPrint,
            AppConstants.Permissions.SalesManage,
            AppConstants.Permissions.SalesReturn,
            AppConstants.Permissions.SalesReturnHighValue,
            AppConstants.Permissions.SalesReturnOverride,
            AppConstants.Permissions.SalesReturnManage,

            AppConstants.Permissions.PurchaseView,
            AppConstants.Permissions.PurchaseCreate,
            AppConstants.Permissions.PurchaseEdit,
            AppConstants.Permissions.PurchaseUnlock,
            AppConstants.Permissions.PurchaseSearch,
            AppConstants.Permissions.PurchaseManage,
            AppConstants.Permissions.PurchaseReturn,
            AppConstants.Permissions.PurchaseReturnManage,
            ..PurchaseMenus,

            AppConstants.Permissions.InventoryView,
            AppConstants.Permissions.InventoryAdjust,
            AppConstants.Permissions.InventoryTransfer,
            AppConstants.Permissions.InventoryManage,
            ..InventoryMenus,

            AppConstants.Permissions.MastersView,
            AppConstants.Permissions.MastersEdit,
            AppConstants.Permissions.MastersManage,
            ..MastersMenus,

            AppConstants.Permissions.AccountingView,
            AppConstants.Permissions.AccountingVouchers,
            AppConstants.Permissions.AccountingJournal,
            AppConstants.Permissions.AccountingManage,
            ..AccountingMenus,

            AppConstants.Permissions.ReportsExport,
            AppConstants.Permissions.ReportsMenuSalesCreditDue,
            AppConstants.Permissions.ReportsMenuSaleReturns,
            AppConstants.Permissions.ReportsMenuMedicineReturns,
            AppConstants.Permissions.ReportsMenuScheduleRegister,
            AppConstants.Permissions.ReportsMenuPurchasesBySupplier,
            AppConstants.Permissions.ReportsMenuSupplierPayments,
            AppConstants.Permissions.ReportsMenuPurchaseReturns,
            AppConstants.Permissions.ReportsMenuExpiryToCompanyClaims,
            AppConstants.Permissions.ReportsMenuCustomerOutstanding,
            AppConstants.Permissions.ReportsMenuCustomerReceipts,
            AppConstants.Permissions.ReportsMenuExpenseRegister,
            AppConstants.Permissions.ReportsMenuExpenseByAccount,
            AppConstants.Permissions.ReportsMenuExpiry,
            AppConstants.Permissions.ReportsMenuLowStock,
            AppConstants.Permissions.ReportsMenuSlowMovingStock,
            AppConstants.Permissions.ReportsMenuMedicinesSoldByDate,

            AppConstants.Permissions.SettingsMenuPassword,
            AppConstants.Permissions.SettingsMenuAppearance,
            AppConstants.Permissions.SettingsMenuMedicineMapping,
            AppConstants.Permissions.SettingsMenuNewMedicineMapping,
        ],
        [AppConstants.Roles.Accountant] =
        [
            AppConstants.Permissions.DashboardView,
            AppConstants.Permissions.PurchaseView,
            AppConstants.Permissions.PurchaseCreate,
            AppConstants.Permissions.PurchaseSearch,
            AppConstants.Permissions.PurchaseReturn,
            ..PurchaseMenus,
            AppConstants.Permissions.AccountingView,
            AppConstants.Permissions.AccountingVouchers,
            AppConstants.Permissions.AccountingJournal,
            ..AccountingMenus,
            AppConstants.Permissions.ReportsView,
            AppConstants.Permissions.ReportsExport,
            ..ReportMenuPermissions.AllKeys,
            AppConstants.Permissions.SettingsMenuPassword,
            AppConstants.Permissions.SettingsMenuAppearance,
        ],
    };

    public static string[] ForRole(string roleName)
        => Map.TryGetValue(roleName, out var keys)
            ? keys
            : [AppConstants.Permissions.DashboardView];
}
