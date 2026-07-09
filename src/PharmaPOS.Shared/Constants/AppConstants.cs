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
        // Module-level permission keys used by the role-based access system.
        public const string DashboardView = "dashboard.view";
        public const string SalesManage = "sales.manage";
        public const string PurchaseManage = "purchase.manage";
        public const string InventoryManage = "inventory.manage";
        public const string MastersManage = "masters.manage";
        public const string AccountingManage = "accounting.manage";
        public const string ReportsView = "reports.view";
        public const string SettingsManage = "settings.manage";
        public const string UsersManage = "users.manage";
    }
}
