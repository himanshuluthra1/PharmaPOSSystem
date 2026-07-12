namespace PharmaPOS.Shared.Constants;

/// <summary>Canonical list of assignable permissions shown in Roles &amp; Permissions.</summary>
public static class PermissionCatalog
{
    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        // Dashboard
        new(AppConstants.Permissions.DashboardView, "View Dashboard", "Dashboard"),

        // Sales
        new(AppConstants.Permissions.SalesView, "View Sales & Search Bills", "Sales"),
        new(AppConstants.Permissions.SalesCreate, "Create & Save Invoices", "Sales"),
        new(AppConstants.Permissions.SalesDiscount, "Apply Line Discounts", "Sales"),
        new(AppConstants.Permissions.SalesPrint, "Print Invoices", "Sales"),
        new(AppConstants.Permissions.SalesManage, "Full Sales Access", "Sales"),
        new(AppConstants.Permissions.SalesReturn, "Process Sale Returns", "Sales"),
        new(AppConstants.Permissions.SalesReturnHighValue, "High-Value Sale Returns", "Sales"),
        new(AppConstants.Permissions.SalesReturnOverride, "Override Return Policy", "Sales"),
        new(AppConstants.Permissions.SalesReturnManage, "Full Sale Return Access", "Sales"),

        // Purchase
        new(AppConstants.Permissions.PurchaseView, "View Purchases", "Purchase"),
        new(AppConstants.Permissions.PurchaseCreate, "Create & Receive Stock", "Purchase"),
        new(AppConstants.Permissions.PurchaseSearch, "Search Purchase Bills", "Purchase"),
        new(AppConstants.Permissions.PurchaseManage, "Full Purchase Access", "Purchase"),

        // Inventory
        new(AppConstants.Permissions.InventoryView, "View Stock & Ledger", "Inventory"),
        new(AppConstants.Permissions.InventoryAdjust, "Stock Adjustments", "Inventory"),
        new(AppConstants.Permissions.InventoryManage, "Full Inventory Access", "Inventory"),

        // Masters
        new(AppConstants.Permissions.MastersView, "View Master Data", "Masters"),
        new(AppConstants.Permissions.MastersEdit, "Create & Edit Masters", "Masters"),
        new(AppConstants.Permissions.MastersManage, "Full Masters Access", "Masters"),

        // Accounting
        new(AppConstants.Permissions.AccountingView, "View Ledgers & Cash Book", "Accounting"),
        new(AppConstants.Permissions.AccountingVouchers, "Record Payment / Receipt / Expense", "Accounting"),
        new(AppConstants.Permissions.AccountingJournal, "Post Journal Entries", "Accounting"),
        new(AppConstants.Permissions.AccountingManage, "Full Accounting Access", "Accounting"),

        // Reports
        new(AppConstants.Permissions.ReportsView, "View Reports", "Reports"),
        new(AppConstants.Permissions.ReportsExport, "Export Reports (CSV)", "Reports"),
        new(AppConstants.Permissions.ReportsManage, "Full Reports Access", "Reports"),

        // Settings
        new(AppConstants.Permissions.SettingsCompany, "Edit Company Profile", "Settings"),
        new(AppConstants.Permissions.SettingsBranches, "Manage Branches", "Settings"),
        new(AppConstants.Permissions.SettingsPreferences, "Edit Preferences", "Settings"),
        new(AppConstants.Permissions.SettingsManage, "Full Settings Access", "Settings"),

        // Security / users
        new(AppConstants.Permissions.UsersView, "View Users", "Security"),
        new(AppConstants.Permissions.UsersEdit, "Create & Edit Users", "Security"),
        new(AppConstants.Permissions.UsersRoles, "Manage Roles & Permissions", "Security"),
        new(AppConstants.Permissions.UsersManage, "Full User Administration", "Security"),
    ];
}

public record PermissionDefinition(string Key, string Name, string Module);
