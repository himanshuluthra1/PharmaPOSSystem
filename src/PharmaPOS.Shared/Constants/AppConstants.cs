namespace PharmaPOS.Shared.Constants;

/// <summary>
/// Application-wide constants and default configuration keys.
/// </summary>
public static class AppConstants
{
    public const string ApplicationName = "PharmaPOS";
    public const string ApplicationVersion = "1.0.0";

    public static class Roles
    {
        public const string SuperAdmin = "SuperAdmin";
        public const string Admin = "Admin";
        public const string Manager = "Manager";
        public const string Pharmacist = "Pharmacist";
        public const string Cashier = "Cashier";
        public const string Accountant = "Accountant";
    }

    public static class Config
    {
        public const string ConnectionStringName = "PharmaPosDb";
        public const int SessionTimeoutMinutes = 30;
        public const int NearExpiryDays = 90;
        public const int DefaultLowStockThreshold = 10;
    }

    public static class Permissions
    {
        // Dashboard
        public const string DashboardView = "dashboard.view";

        // Sales
        public const string SalesView = "sales.view";
        public const string SalesCreate = "sales.create";
        public const string SalesDiscount = "sales.discount";
        public const string SalesPrint = "sales.print";
        public const string SalesManage = "sales.manage";

        // Purchase
        public const string PurchaseView = "purchase.view";
        public const string PurchaseCreate = "purchase.create";
        public const string PurchaseSearch = "purchase.search";
        public const string PurchaseManage = "purchase.manage";

        // Inventory
        public const string InventoryView = "inventory.view";
        public const string InventoryAdjust = "inventory.adjust";
        public const string InventoryManage = "inventory.manage";

        // Masters
        public const string MastersView = "masters.view";
        public const string MastersEdit = "masters.edit";
        public const string MastersManage = "masters.manage";

        // Accounting
        public const string AccountingView = "accounting.view";
        public const string AccountingVouchers = "accounting.vouchers";
        public const string AccountingJournal = "accounting.journal";
        public const string AccountingManage = "accounting.manage";

        // Reports
        public const string ReportsView = "reports.view";
        public const string ReportsExport = "reports.export";
        public const string ReportsManage = "reports.manage";

        // Settings
        public const string SettingsCompany = "settings.company";
        public const string SettingsBranches = "settings.branches";
        public const string SettingsPreferences = "settings.preferences";
        public const string SettingsManage = "settings.manage";

        // Security
        public const string UsersView = "users.view";
        public const string UsersEdit = "users.edit";
        public const string UsersRoles = "users.roles";
        public const string UsersManage = "users.manage";
    }
}
