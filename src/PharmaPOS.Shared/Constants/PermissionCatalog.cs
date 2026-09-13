namespace PharmaPOS.Shared.Constants;

/// <summary>Canonical list of assignable permissions shown in Roles &amp; Permissions.</summary>
public static class PermissionCatalog
{
    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        // Dashboard
        new(AppConstants.Permissions.DashboardView, "Menu — Dashboard", "Dashboard"),

        // Sales
        new(AppConstants.Permissions.SalesView, "Menu — Sales", "Sales"),
        new(AppConstants.Permissions.SalesCreate, "Create & Save Invoices", "Sales"),
        new(AppConstants.Permissions.SalesEdit, "Edit Unlocked Sale Invoices", "Sales"),
        new(AppConstants.Permissions.SalesUnlock, "Unlock Sale Invoices for Edit", "Sales"),
        new(AppConstants.Permissions.SalesDiscount, "Apply Line Discounts", "Sales"),
        new(AppConstants.Permissions.SalesPrint, "Print Invoices", "Sales"),
        new(AppConstants.Permissions.SalesManage, "Full Sales Access", "Sales"),
        new(AppConstants.Permissions.SalesReturn, "Process Sale Returns", "Sales"),
        new(AppConstants.Permissions.SalesReturnHighValue, "High-Value Sale Returns", "Sales"),
        new(AppConstants.Permissions.SalesReturnOverride, "Override Return Policy", "Sales"),
        new(AppConstants.Permissions.SalesReturnManage, "Full Sale Return Access", "Sales"),

        // Purchase — menu
        new(AppConstants.Permissions.PurchaseMenuInvoice, "Menu — Purchase invoice", "Purchase"),
        new(AppConstants.Permissions.PurchaseMenuOrder, "Menu — Purchase order", "Purchase"),
        new(AppConstants.Permissions.PurchaseMenuReturn, "Menu — Purchase return", "Purchase"),
        new(AppConstants.Permissions.PurchaseMenuExpiry, "Menu — Expiry to company", "Purchase"),
        // Purchase — actions
        new(AppConstants.Permissions.PurchaseView, "View Purchases", "Purchase"),
        new(AppConstants.Permissions.PurchaseCreate, "Create & Receive Stock", "Purchase"),
        new(AppConstants.Permissions.PurchaseEdit, "Edit Unlocked Purchase Invoices", "Purchase"),
        new(AppConstants.Permissions.PurchaseUnlock, "Unlock Purchase Invoices for Edit", "Purchase"),
        new(AppConstants.Permissions.PurchaseSearch, "Search Purchase Bills", "Purchase"),
        new(AppConstants.Permissions.PurchaseManage, "Full Purchase Access", "Purchase"),
        new(AppConstants.Permissions.PurchaseReturn, "Process Purchase Returns", "Purchase"),
        new(AppConstants.Permissions.PurchaseReturnManage, "Full Purchase Return Access", "Purchase"),

        // Inventory — menu
        new(AppConstants.Permissions.InventoryMenuOnhand, "Menu — On Hand", "Inventory"),
        new(AppConstants.Permissions.InventoryMenuLedger, "Menu — Ledger", "Inventory"),
        new(AppConstants.Permissions.InventoryMenuAdjustment, "Menu — Adjustment", "Inventory"),
        new(AppConstants.Permissions.InventoryMenuTransfer, "Menu — Transfer", "Inventory"),
        new(AppConstants.Permissions.InventoryMenuTransferHistory, "Menu — Recent Transfer", "Inventory"),
        new(AppConstants.Permissions.InventoryMenuShortage, "Menu — Shortage book", "Inventory"),
        // Inventory — actions
        new(AppConstants.Permissions.InventoryView, "View Stock & Ledger", "Inventory"),
        new(AppConstants.Permissions.InventoryAdjust, "Stock Adjustments", "Inventory"),
        new(AppConstants.Permissions.InventoryTransfer, "Transfer Stock Between Stores", "Inventory"),
        new(AppConstants.Permissions.InventoryManage, "Full Inventory Access", "Inventory"),

        // Masters — menu
        new(AppConstants.Permissions.MastersMenuSuppliers, "Menu — Suppliers", "Masters"),
        new(AppConstants.Permissions.MastersMenuCustomers, "Menu — Customers", "Masters"),
        new(AppConstants.Permissions.MastersMenuDoctors, "Menu — Doctors", "Masters"),
        new(AppConstants.Permissions.MastersMenuManufacturers, "Menu — Manufacturers", "Masters"),
        new(AppConstants.Permissions.MastersMenuEmployees, "Menu — Employees", "Masters"),
        new(AppConstants.Permissions.MastersMenuMedicines, "Menu — Medicines", "Masters"),
        // Masters — actions
        new(AppConstants.Permissions.MastersView, "View Master Data", "Masters"),
        new(AppConstants.Permissions.MastersEdit, "Create & Edit Masters", "Masters"),
        new(AppConstants.Permissions.MastersManage, "Full Masters Access", "Masters"),

        // Accounting — menu
        new(AppConstants.Permissions.AccountingMenuParties, "Menu — Parties", "Accounting"),
        new(AppConstants.Permissions.AccountingMenuDues, "Menu — Customer Dues", "Accounting"),
        new(AppConstants.Permissions.AccountingMenuVouchers, "Menu — Vouchers", "Accounting"),
        new(AppConstants.Permissions.AccountingMenuCashbook, "Menu — Cash Book", "Accounting"),
        new(AppConstants.Permissions.AccountingMenuJournal, "Menu — Journal", "Accounting"),
        // Accounting — actions
        new(AppConstants.Permissions.AccountingView, "View Ledgers & Cash Book", "Accounting"),
        new(AppConstants.Permissions.AccountingVouchers, "Record Payment / Receipt / Expense", "Accounting"),
        new(AppConstants.Permissions.AccountingJournal, "Post Journal Entries", "Accounting"),
        new(AppConstants.Permissions.AccountingManage, "Full Accounting Access", "Accounting"),

        // Reports — actions
        new(AppConstants.Permissions.ReportsView, "View Reports", "Reports"),
        new(AppConstants.Permissions.ReportsExport, "Export Reports (CSV)", "Reports"),
        new(AppConstants.Permissions.ReportsManage, "Full Reports Access", "Reports"),
        // Reports — menu (one per report kind)
        new(AppConstants.Permissions.ReportsMenuSales, "Menu — Invoice Register", "Reports"),
        new(AppConstants.Permissions.ReportsMenuSalesByCustomer, "Menu — By Customer / Customer-wise Sales", "Reports"),
        new(AppConstants.Permissions.ReportsMenuSalesByMedicine, "Menu — By Medicine", "Reports"),
        new(AppConstants.Permissions.ReportsMenuSalesByPaymentMode, "Menu — By Payment Mode", "Reports"),
        new(AppConstants.Permissions.ReportsMenuSalesDayWise, "Menu — Day-wise Summary", "Reports"),
        new(AppConstants.Permissions.ReportsMenuSalesCreditDue, "Menu — Credit Due / Pending Collections", "Reports"),
        new(AppConstants.Permissions.ReportsMenuProfit, "Menu — Gross Profit", "Reports"),
        new(AppConstants.Permissions.ReportsMenuSaleReturns, "Menu — Sale Returns", "Reports"),
        new(AppConstants.Permissions.ReportsMenuMedicineReturns, "Menu — Medicine-wise Returns", "Reports"),
        new(AppConstants.Permissions.ReportsMenuScheduleRegister, "Menu — Schedule H / H1 Register", "Reports"),
        new(AppConstants.Permissions.ReportsMenuPurchases, "Menu — Purchase Register", "Reports"),
        new(AppConstants.Permissions.ReportsMenuPurchasesBySupplier, "Menu — Purchases By Supplier", "Reports"),
        new(AppConstants.Permissions.ReportsMenuSupplierOutstanding, "Menu — Supplier Outstanding", "Reports"),
        new(AppConstants.Permissions.ReportsMenuSupplierPayments, "Menu — Purchase Payments", "Reports"),
        new(AppConstants.Permissions.ReportsMenuPurchaseReturns, "Menu — Purchase Returns Report", "Reports"),
        new(AppConstants.Permissions.ReportsMenuExpiryToCompanyClaims, "Menu — Expiry to Company Claims Report", "Reports"),
        new(AppConstants.Permissions.ReportsMenuCustomerOutstanding, "Menu — Customer Outstanding", "Reports"),
        new(AppConstants.Permissions.ReportsMenuCustomerReceipts, "Menu — Customer Receipts", "Reports"),
        new(AppConstants.Permissions.ReportsMenuPaymentVouchers, "Menu — Payment Vouchers", "Reports"),
        new(AppConstants.Permissions.ReportsMenuReceiptVouchers, "Menu — Receipt Vouchers", "Reports"),
        new(AppConstants.Permissions.ReportsMenuExpenseRegister, "Menu — Expense Register", "Reports"),
        new(AppConstants.Permissions.ReportsMenuExpenseByAccount, "Menu — Expense by Account", "Reports"),
        new(AppConstants.Permissions.ReportsMenuCashBookSummary, "Menu — Cash Book Summary", "Reports"),
        new(AppConstants.Permissions.ReportsMenuGstSummary, "Menu — GST Summary", "Reports"),
        new(AppConstants.Permissions.ReportsMenuGstr1, "Menu — GSTR-1 export", "Reports"),
        new(AppConstants.Permissions.ReportsMenuGstr2B, "Menu — GSTR-2B worksheet", "Reports"),
        new(AppConstants.Permissions.ReportsMenuStockValuation, "Menu — Stock Valuation", "Reports"),
        new(AppConstants.Permissions.ReportsMenuBatchStock, "Menu — Batch-wise Stock", "Reports"),
        new(AppConstants.Permissions.ReportsMenuExpiry, "Menu — Expiry Report", "Reports"),
        new(AppConstants.Permissions.ReportsMenuLowStock, "Menu — Low Stock", "Reports"),
        new(AppConstants.Permissions.ReportsMenuSlowMovingStock, "Menu — Slow / Non-moving Stock", "Reports"),

        // Settings — menu
        new(AppConstants.Permissions.SettingsMenuCompany, "Menu — Company", "Settings"),
        new(AppConstants.Permissions.SettingsMenuBranches, "Menu — Branches", "Settings"),
        new(AppConstants.Permissions.SettingsMenuCounters, "Menu — Counters", "Settings"),
        new(AppConstants.Permissions.SettingsMenuPreferences, "Menu — Preferences", "Settings"),
        new(AppConstants.Permissions.SettingsMenuMedicineMapping, "Menu — Medicine Mapping", "Settings"),
        new(AppConstants.Permissions.SettingsMenuNewMedicineMapping, "Menu — New Medicine Mapping", "Settings"),
        new(AppConstants.Permissions.SettingsMenuMedWinImport, "Menu — MedWin Import", "Settings"),
        new(AppConstants.Permissions.SettingsMenuRoles, "Menu — Roles & Permissions", "Settings"),
        new(AppConstants.Permissions.SettingsMenuUsers, "Menu — Users", "Settings"),
        new(AppConstants.Permissions.SettingsMenuPassword, "Menu — My Password", "Settings"),
        new(AppConstants.Permissions.SettingsMenuAppearance, "Menu — Appearance", "Settings"),
        new(AppConstants.Permissions.SettingsMenuBackup, "Menu — Backup", "Settings"),
        new(AppConstants.Permissions.SettingsMenuUpdates, "Menu — Shop updates", "Settings"),
        // Settings — actions
        new(AppConstants.Permissions.SettingsCompany, "Edit Company Profile", "Settings"),
        new(AppConstants.Permissions.SettingsBranches, "Manage Branches", "Settings"),
        new(AppConstants.Permissions.SettingsPreferences, "Edit Preferences", "Settings"),
        new(AppConstants.Permissions.SettingsManage, "Full Settings Access", "Settings"),

        // Security
        new(AppConstants.Permissions.UsersView, "View Users", "Security"),
        new(AppConstants.Permissions.UsersEdit, "Create & Edit Users", "Security"),
        new(AppConstants.Permissions.UsersRoles, "Manage Roles & Permissions", "Security"),
        new(AppConstants.Permissions.UsersManage, "Full User Administration", "Security"),
    ];
}

public record PermissionDefinition(string Key, string Name, string Module);
