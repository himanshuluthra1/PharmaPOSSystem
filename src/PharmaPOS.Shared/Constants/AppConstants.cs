namespace PharmaPOS.Shared.Constants;

/// <summary>
/// Application-wide constants and default configuration keys.
/// </summary>
public static class AppConstants
{
    public const string ApplicationName = "PharmaPOS";
    public const string ApplicationVersion = "1.3.10";

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
        public const string SalesEdit = "sales.edit";
        public const string SalesUnlock = "sales.unlock";
        public const string SalesDiscount = "sales.discount";
        public const string SalesPrint = "sales.print";
        public const string SalesManage = "sales.manage";
        public const string SalesReturn = "sales.return";
        public const string SalesReturnHighValue = "sales.return.highvalue";
        public const string SalesReturnOverride = "sales.return.override";
        public const string SalesReturnManage = "sales.return.manage";

        // Purchase
        public const string PurchaseView = "purchase.view";
        public const string PurchaseCreate = "purchase.create";
        public const string PurchaseEdit = "purchase.edit";
        public const string PurchaseUnlock = "purchase.unlock";
        public const string PurchaseSearch = "purchase.search";
        public const string PurchaseManage = "purchase.manage";
        public const string PurchaseReturn = "purchase.return";
        public const string PurchaseReturnManage = "purchase.return.manage";
        public const string PurchaseMenuInvoice = "purchase.menu.invoice";
        public const string PurchaseMenuOrder = "purchase.menu.order";
        public const string PurchaseMenuReturn = "purchase.menu.return";
        public const string PurchaseMenuExpiry = "purchase.menu.expiry";

        // Inventory
        public const string InventoryView = "inventory.view";
        public const string InventoryAdjust = "inventory.adjust";
        public const string InventoryTransfer = "inventory.transfer";
        public const string InventoryManage = "inventory.manage";
        public const string InventoryMenuOnhand = "inventory.menu.onhand";
        public const string InventoryMenuLedger = "inventory.menu.ledger";
        public const string InventoryMenuAdjustment = "inventory.menu.adjustment";
        public const string InventoryMenuTransfer = "inventory.menu.transfer";
        public const string InventoryMenuTransferHistory = "inventory.menu.transferhistory";
        public const string InventoryMenuShortage = "inventory.menu.shortage";

        // Masters
        public const string MastersView = "masters.view";
        public const string MastersEdit = "masters.edit";
        public const string MastersManage = "masters.manage";
        public const string MastersMenuSuppliers = "masters.menu.suppliers";
        public const string MastersMenuCustomers = "masters.menu.customers";
        public const string MastersMenuDoctors = "masters.menu.doctors";
        public const string MastersMenuManufacturers = "masters.menu.manufacturers";
        public const string MastersMenuEmployees = "masters.menu.employees";
        public const string MastersMenuMedicines = "masters.menu.medicines";

        // Accounting
        public const string AccountingView = "accounting.view";
        public const string AccountingVouchers = "accounting.vouchers";
        public const string AccountingJournal = "accounting.journal";
        public const string AccountingManage = "accounting.manage";
        public const string AccountingMenuParties = "accounting.menu.parties";
        public const string AccountingMenuDues = "accounting.menu.dues";
        public const string AccountingMenuVouchers = "accounting.menu.vouchers";
        public const string AccountingMenuCashbook = "accounting.menu.cashbook";
        public const string AccountingMenuJournal = "accounting.menu.journal";

        // Reports
        public const string ReportsView = "reports.view";
        public const string ReportsExport = "reports.export";
        public const string ReportsManage = "reports.manage";
        public const string ReportsMenuSales = "reports.menu.sales";
        public const string ReportsMenuSalesByCustomer = "reports.menu.salesbycustomer";
        public const string ReportsMenuSalesByMedicine = "reports.menu.salesbymedicine";
        public const string ReportsMenuSalesByPaymentMode = "reports.menu.salesbypaymentmode";
        public const string ReportsMenuSalesDayWise = "reports.menu.salesdaywise";
        public const string ReportsMenuSalesCreditDue = "reports.menu.salescreditdue";
        public const string ReportsMenuProfit = "reports.menu.profit";
        public const string ReportsMenuSaleReturns = "reports.menu.salereturns";
        public const string ReportsMenuMedicineReturns = "reports.menu.medicinereturns";
        public const string ReportsMenuScheduleRegister = "reports.menu.scheduleregister";
        public const string ReportsMenuPurchases = "reports.menu.purchases";
        public const string ReportsMenuPurchasesBySupplier = "reports.menu.purchasesbysupplier";
        public const string ReportsMenuSupplierOutstanding = "reports.menu.supplieroutstanding";
        public const string ReportsMenuSupplierPayments = "reports.menu.supplierpayments";
        public const string ReportsMenuPurchaseReturns = "reports.menu.purchasereturns";
        public const string ReportsMenuExpiryToCompanyClaims = "reports.menu.expirytocompanyclaims";
        public const string ReportsMenuCustomerOutstanding = "reports.menu.customeroutstanding";
        public const string ReportsMenuCustomerReceipts = "reports.menu.customerreceipts";
        public const string ReportsMenuPaymentVouchers = "reports.menu.paymentvouchers";
        public const string ReportsMenuReceiptVouchers = "reports.menu.receiptvouchers";
        public const string ReportsMenuExpenseRegister = "reports.menu.expenseregister";
        public const string ReportsMenuExpenseByAccount = "reports.menu.expensebyaccount";
        public const string ReportsMenuCashBookSummary = "reports.menu.cashbooksummary";
        public const string ReportsMenuGstSummary = "reports.menu.gstsummary";
        public const string ReportsMenuGstr1 = "reports.menu.gstr1";
        public const string ReportsMenuGstr2B = "reports.menu.gstr2b";
        public const string ReportsMenuStockValuation = "reports.menu.stockvaluation";
        public const string ReportsMenuBatchStock = "reports.menu.batchstock";
        public const string ReportsMenuExpiry = "reports.menu.expiry";
        public const string ReportsMenuLowStock = "reports.menu.lowstock";
        public const string ReportsMenuSlowMovingStock = "reports.menu.slowmovingstock";
        public const string ReportsMenuStockAdjustments = "reports.menu.stockadjustments";
        public const string ReportsMenuMedicinesSoldByDate = "reports.menu.medicinessoldbydate";

        // Settings
        public const string SettingsCompany = "settings.company";
        public const string SettingsBranches = "settings.branches";
        public const string SettingsPreferences = "settings.preferences";
        public const string SettingsManage = "settings.manage";
        public const string SettingsMenuCompany = "settings.menu.company";
        public const string SettingsMenuBranches = "settings.menu.branches";
        public const string SettingsMenuCounters = "settings.menu.counters";
        public const string SettingsMenuPreferences = "settings.menu.preferences";
        public const string SettingsMenuMedicineMapping = "settings.menu.medicinemapping";
        public const string SettingsMenuNewMedicineMapping = "settings.menu.newmedicinemapping";
        public const string SettingsMenuMedWinImport = "settings.menu.medwinimport";
        public const string SettingsMenuRoles = "settings.menu.roles";
        public const string SettingsMenuUsers = "settings.menu.users";
        public const string SettingsMenuPassword = "settings.menu.password";
        public const string SettingsMenuAppearance = "settings.menu.appearance";
        public const string SettingsMenuBackup = "settings.menu.backup";
        public const string SettingsMenuUpdates = "settings.menu.updates";

        // Security
        public const string UsersView = "users.view";
        public const string UsersEdit = "users.edit";
        public const string UsersRoles = "users.roles";
        public const string UsersManage = "users.manage";
    }
}
