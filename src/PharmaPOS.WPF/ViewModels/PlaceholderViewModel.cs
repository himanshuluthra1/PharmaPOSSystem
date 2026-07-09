using PharmaPOS.WPF.Mvvm;

namespace PharmaPOS.WPF.ViewModels;

/// <summary>
/// Base for module screens that are scaffolded but not yet implemented. Each
/// derived type describes the module and the features planned for it, so the
/// shell navigation is fully wired ahead of feature work.
/// </summary>
public abstract class PlaceholderViewModel : ObservableObject
{
    public abstract string Title { get; }
    public abstract string Description { get; }
    public abstract string[] PlannedFeatures { get; }
}

public class InventoryViewModel : PlaceholderViewModel
{
    public override string Title => "Inventory";
    public override string Description => "Real-time stock across batches with expiry, valuation and movement tracking.";
    public override string[] PlannedFeatures =>
    [
        "Current stock & stock ledger", "Stock adjustment & physical verification",
        "Branch transfer", "Near-expiry / expired / dead stock", "FIFO & FEFO",
        "Auto reorder", "Stock valuation"
    ];
}

public class AccountingViewModel : PlaceholderViewModel
{
    public override string Title => "Accounting";
    public override string Description => "Double-entry accounting with ledgers, GST reports and financial statements.";
    public override string[] PlannedFeatures =>
    [
        "Cash & bank book", "General ledger & journal", "Payment / receipt / expense entry",
        "Supplier & customer ledger", "P&L, Balance Sheet, Trial Balance", "GST & TDS reports"
    ];
}

public class ReportsViewModel : PlaceholderViewModel
{
    public override string Title => "Reports";
    public override string Description => "Comprehensive sales, purchase, stock, tax and analytics reporting.";
    public override string[] PlannedFeatures =>
    [
        "Daily / monthly / yearly sales", "GST & purchase reports", "Profit & stock reports",
        "Expiry & dead stock", "ABC / XYZ analysis", "PDF & Excel export"
    ];
}

public class SettingsViewModel : PlaceholderViewModel
{
    public override string Title => "Settings";
    public override string Description => "Configure company, printing, taxes, themes, backup and integrations.";
    public override string[] PlannedFeatures =>
    [
        "Company & store details", "Printer & invoice templates", "Tax & discount rules",
        "Barcode settings", "Backup scheduler", "Email / SMS / WhatsApp API", "User & role management"
    ];
}
