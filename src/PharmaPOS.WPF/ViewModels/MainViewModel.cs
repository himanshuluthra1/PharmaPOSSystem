using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.ReportingSync;
using PharmaPOS.Application.Features.Reports;
using PharmaPOS.Application.Features.Settings;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;
using PharmaPOS.WPF.ViewModels.Sales;
using PharmaPOS.WPF.ViewModels.Purchases;
using PharmaPOS.WPF.ViewModels.Masters;
using PharmaPOS.WPF.ViewModels.Inventory;
using PharmaPOS.WPF.ViewModels.Accounting;
using PharmaPOS.WPF.ViewModels.Reports;
using PharmaPOS.WPF.ViewModels.Settings;
using PharmaPOS.Shared.Constants;

namespace PharmaPOS.WPF.ViewModels;

/// <summary>
/// The application shell view model. Owns the navigation rail, the active content
/// (via <see cref="INavigationService"/>), theme toggle and session/logout.
/// </summary>
public class MainViewModel : ObservableObject
{
    private static readonly SolidColorBrush CurrentFyBarBrush = CreateFrozenBrush("#2E7D32");
    private static readonly SolidColorBrush PriorFyBarBrush = CreateFrozenBrush("#F9A825");

    private readonly INavigationService _navigation;
    private readonly ICurrentUserService _currentUser;
    private readonly IThemeService _themeService;
    private readonly IStoreIdentityService _storeIdentity;
    private readonly IFinancialYearContext _financialYear;

    private NavigationItem? _selectedItem;
    private bool _isDarkMode;
    private bool _suppressNavSync;

    public MainViewModel(
        INavigationService navigation,
        ICurrentUserService currentUser,
        IThemeService themeService,
        IStoreIdentityService storeIdentity,
        IFinancialYearContext financialYear)
    {
        _navigation = navigation;
        _currentUser = currentUser;
        _themeService = themeService;
        _storeIdentity = storeIdentity;
        _financialYear = financialYear;

        _navigation.CurrentChanged += () =>
        {
            OnPropertyChanged(nameof(CurrentViewModel));
            OnPropertyChanged(nameof(ActiveSales));
            OnPropertyChanged(nameof(ShowSalesCounterBar));
            SyncSelectedItemToCurrentView();
        };

        _financialYear.Changed += OnFinancialYearChanged;

        NavigateCommand = new RelayCommand(p => Navigate(p as NavigationItem));
        ToggleThemeCommand = new RelayCommand(_ => ToggleTheme());

        BuildMenu();
        SelectedItem = MenuItems.FirstOrDefault();
        RefreshMenuActiveStates();
    }

    private void OnFinancialYearChanged()
    {
        var currentType = _navigation.CurrentViewModel?.GetType();
        _navigation.ClearSessionCache();
        OnPropertyChanged(nameof(FinancialYearLabel));
        OnPropertyChanged(nameof(IsPriorFinancialYear));
        OnPropertyChanged(nameof(TopBarBackground));
        OnPropertyChanged(nameof(ShowFinancialYearBadge));

        // Refresh the open module (except Settings, which triggered the FY change).
        if (currentType is not null && currentType != typeof(SettingsViewModel))
            _navigation.NavigateTo(currentType);
    }

    public string FinancialYearLabel => _financialYear.Active.DisplayLabel;

    public bool IsPriorFinancialYear => !_financialYear.Active.IsCurrent;

    public bool ShowFinancialYearBadge => true;

    public Brush TopBarBackground =>
        IsPriorFinancialYear ? PriorFyBarBrush : CurrentFyBarBrush;

    private static SolidColorBrush CreateFrozenBrush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }

    /// <summary>Raised when the user chooses to sign out; the host returns to login.</summary>
    public event Action? LogoutRequested;

    /// <summary>Flat list of all navigable modules (used for shortcuts / sync).</summary>
    public ObservableCollection<NavigationItem> MenuItems { get; } = new();

    /// <summary>Top bar hierarchy (groups + leaf items).</summary>
    public ObservableCollection<NavMenuEntry> TopMenu { get; } = new();

    public ObservableObject? CurrentViewModel => _navigation.CurrentViewModel;

    /// <summary>Sales VM when that module is open (for counter strip in the app bar).</summary>
    public SalesViewModel? ActiveSales => _navigation.CurrentViewModel as SalesViewModel;

    public bool ShowSalesCounterBar => ActiveSales is not null;

    public string UserName => _currentUser.CurrentUser?.FullName ?? "Guest";
    public string RoleName => _currentUser.CurrentUser?.RoleName ?? string.Empty;
    public string BranchName => _currentUser.CurrentUser?.BranchName ?? "Head Office";
    public string StoreCodeDisplay =>
        string.IsNullOrWhiteSpace(_storeIdentity.StoreCode)
            ? $"v{AppConstants.ApplicationVersion}  ·  Store code: (not set)"
            : string.IsNullOrWhiteSpace(_storeIdentity.StoreId)
                ? $"v{AppConstants.ApplicationVersion}  ·  Store: {_storeIdentity.StoreCode}"
                : $"v{AppConstants.ApplicationVersion}  ·  Store: {_storeIdentity.StoreCode}  ·  ID: {_storeIdentity.StoreId}";

    public NavigationItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!SetProperty(ref _selectedItem, value) || value is null) return;

            try
            {
                _suppressNavSync = true;
                if (_navigation.CurrentViewModel?.GetType() != value.TargetViewModel)
                    _navigation.NavigateTo(value.TargetViewModel);
                ApplySubNavToCurrent(value);
            }
            finally
            {
                _suppressNavSync = false;
            }

            OnPropertyChanged(nameof(ActiveShortcuts));
            OnPropertyChanged(nameof(HasActiveShortcuts));
            OnPropertyChanged(nameof(ActiveSectionTitle));
            OnPropertyChanged(nameof(HasActiveSectionTitle));
            RefreshMenuActiveStates();
        }
    }

    /// <summary>Current submenu / screen title shown in the green app bar.</summary>
    public string? ActiveSectionTitle => _selectedItem?.Label;

    public bool HasActiveSectionTitle => !string.IsNullOrWhiteSpace(ActiveSectionTitle);

    /// <summary>Shortcuts for the currently selected module (bottom colored strip).</summary>
    public IReadOnlyList<ShortcutHint> ActiveShortcuts =>
        SelectedItem?.TargetViewModel == typeof(PurchaseReturnViewModel)
            ? PurchaseReturnShortcuts
            : GetShortcutsForModule(SelectedItem?.Module);

    public bool HasActiveShortcuts => ActiveShortcuts.Count > 0;

    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            if (SetProperty(ref _isDarkMode, value))
                _themeService.SetDarkMode(value);
        }
    }

    public ICommand NavigateCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand LogoutCommand => new RelayCommand(_ => LogoutRequested?.Invoke());

    private void Navigate(NavigationItem? item)
    {
        if (item is not null) SelectedItem = item;
    }

    private void SyncSelectedItemToCurrentView()
    {
        if (_suppressNavSync) return;
        var current = _navigation.CurrentViewModel;
        if (current is null) return;

        var currentType = current.GetType();
        var (tabIndex, reportKind) = ReadSubNav(current);

        var match = MenuItems.FirstOrDefault(m =>
            m.TargetViewModel == currentType
            && m.TabIndex == tabIndex
            && m.ReportKind == reportKind)
            ?? MenuItems.FirstOrDefault(m => m.TargetViewModel == currentType);

        if (match is null || ReferenceEquals(_selectedItem, match)) return;
        _selectedItem = match;
        OnPropertyChanged(nameof(SelectedItem));
        OnPropertyChanged(nameof(ActiveShortcuts));
        OnPropertyChanged(nameof(HasActiveShortcuts));
        OnPropertyChanged(nameof(ActiveSectionTitle));
        OnPropertyChanged(nameof(HasActiveSectionTitle));
        RefreshMenuActiveStates();
    }

    private static (int? TabIndex, ReportKind? ReportKind) ReadSubNav(ObservableObject vm) => vm switch
    {
        InventoryViewModel inventory => (inventory.SelectedTab, null),
        MastersViewModel masters => (masters.SelectedTab, null),
        AccountingViewModel accounting => (accounting.SelectedTab, null),
        SettingsViewModel settings => (settings.SelectedTab, null),
        ReportsViewModel reports => (null, reports.SelectedReport.Kind),
        _ => (null, null)
    };

    private void ApplySubNavToCurrent(NavigationItem item)
    {
        switch (_navigation.CurrentViewModel)
        {
            case InventoryViewModel inventory when item.TabIndex is int tab:
                inventory.SelectedTab = tab;
                break;
            case MastersViewModel masters when item.TabIndex is int tab:
                masters.SelectedTab = tab;
                break;
            case AccountingViewModel accounting when item.TabIndex is int tab:
                accounting.SelectedTab = tab;
                break;
            case SettingsViewModel settings when item.TabIndex is int tab:
                settings.SelectTab(tab);
                break;
            case ReportsViewModel reports when item.ReportKind is ReportKind kind:
                reports.SelectReport(kind);
                break;
        }
    }

    private void ToggleTheme() => IsDarkMode = !IsDarkMode;

    private void RefreshMenuActiveStates()
    {
        foreach (var entry in TopMenu)
            SetActiveRecursive(entry, _selectedItem);
    }

    private static void SetActiveRecursive(NavMenuEntry entry, NavigationItem? selected)
    {
        entry.IsActive = entry.Contains(selected);
        foreach (var child in entry.Children)
            SetActiveRecursive(child, selected);
    }

    /// <summary>Navigate to the Sales module (F2 shortcut).</summary>
    public void NavigateToSales()
    {
        var sales = MenuItems.FirstOrDefault(m => m.TargetViewModel == typeof(SalesViewModel));
        if (sales is null) return;
        SelectedItem = sales;
    }

    /// <summary>Navigate to the Purchase module.</summary>
    public void NavigateToPurchase()
    {
        var purchase = MenuItems.FirstOrDefault(m => m.TargetViewModel == typeof(PurchaseViewModel));
        if (purchase is null) return;
        SelectedItem = purchase;
    }

    private static readonly (string Bg, string Border)[] ShortcutPalette =
    [
        ("#E3F2FD", "#90CAF9"),
        ("#FFF3E0", "#FFCC80"),
        ("#E8F5E9", "#A5D6A7"),
        ("#F3E5F5", "#CE93D8"),
        ("#FFEBEE", "#EF9A9A"),
        ("#E0F7FA", "#80DEEA"),
        ("#FFFDE7", "#FFF59D"),
        ("#ECEFF1", "#B0BEC5"),
        ("#FBE9E7", "#FFAB91"),
        ("#E8EAF6", "#9FA8DA"),
        ("#E0F2F1", "#80CBC4"),
        ("#FFF8E1", "#FFD54F"),
        ("#FCE4EC", "#F48FB1"),
    ];

    private static ShortcutHint Hint(string key, string description, int index)
    {
        var (bg, border) = ShortcutPalette[index % ShortcutPalette.Length];
        return new ShortcutHint(key, description, bg, border);
    }

    private static IReadOnlyList<ShortcutHint> Hints(params (string Key, string Description)[] items)
        => items.Select((x, i) => Hint(x.Key, x.Description, i)).ToList();

    private static IReadOnlyList<ShortcutHint> GetShortcutsForModule(string? module) => module switch
    {
        "sales" => Hints(
            ("F2", "Sales"),
            ("Ctrl+T", "New customer"),
            ("Ctrl+1-4", "Switch bill"),
            ("Ctrl+W", "Close bill"),
            ("Esc", "Clear bill"),
            ("F3", "Customer / Save"),
            ("F4", "Medicine details"),
            ("Ctrl+L", "Medicine ledger"),
            ("F5", "Substitute"),
            ("F6", "Clone bill"),
            ("F8", "Sale return"),
            ("F9", "Save / Print"),
            ("Enter", "Search item")),
        "purchase" => Hints(
            ("F2", "Sales"),
            ("Esc", "New purchase"),
            ("F9", "Save"),
            ("Ctrl+L", "Medicine ledger"),
            ("Enter", "Search item / barcode"),
            ("Scan bill", "OCR supplier invoice")),
        "inventory" => Hints(
            ("F2", "Sales"),
            ("Ctrl+L", "Medicine ledger")),
        "masters" => Hints(
            ("F2", "Sales"),
            ("Ctrl+L", "Medicine ledger")),
        "accounting" => Hints(
            ("F2", "Sales")),
        "reports" => Hints(
            ("F2", "Sales"),
            ("Enter", "Open bill")),
        "settings" => Hints(
            ("F2", "Sales")),
        "dashboard" => Hints(
            ("F2", "Sales")),
        _ => Hints(
            ("F2", "Sales"))
    };

    private static readonly IReadOnlyList<ShortcutHint> PurchaseReturnShortcuts = Hints(
        ("F2", "Sales"),
        ("F3", "Search bill"),
        ("F9", "Process return"),
        ("Enter", "Search"));

    private void BuildMenu()
    {
        var dashboard = Leaf("Dashboard", "ViewDashboard", typeof(DashboardViewModel), "dashboard",
            requiredPermission: AppConstants.Permissions.DashboardView);
        var sales = Leaf("Sales (F2)", "PointOfSale", typeof(SalesViewModel), "sales",
            requiredPermission: AppConstants.Permissions.SalesView);
        var purchase = Leaf("Purchase invoice", "TruckDelivery", typeof(PurchaseViewModel), "purchase",
            requiredPermission: AppConstants.Permissions.PurchaseMenuInvoice);
        var purchaseOrder = Leaf("Purchase order", "ClipboardText", typeof(PurchaseOrderViewModel), "purchase",
            requiredPermission: AppConstants.Permissions.PurchaseMenuOrder);
        var purchaseReturn = Leaf("Purchase return", "AssignmentReturn", typeof(PurchaseReturnViewModel), "purchase",
            requiredPermission: AppConstants.Permissions.PurchaseMenuReturn);
        var expiry = Leaf("Expiry to company", "CalendarRemove", typeof(ExpiryReturnViewModel), "purchase",
            requiredPermission: AppConstants.Permissions.PurchaseMenuExpiry);

        AddTopIfAllowed(dashboard);
        AddTopIfAllowed(sales);

        var purchaseChildren = new[] { purchase, purchaseOrder, purchaseReturn, expiry }
            .Where(CanShow)
            .ToList();
        AddGroupOrLeaf("Purchases", "TruckDelivery", purchaseChildren);

        AddModuleSubmenu(
            "Inventory", "PackageVariantClosed", "inventory", typeof(InventoryViewModel),
            BuildInventoryChildren());

        AddModuleSubmenu(
            "Masters", "DatabaseCog", "masters", typeof(MastersViewModel),
            BuildMastersChildren());

        AddModuleSubmenu(
            "Accounting", "Calculator", "accounting", typeof(AccountingViewModel),
            BuildAccountingChildren());

        AddReportsSubmenu();

        AddModuleSubmenu(
            "Settings", "Cog", "settings", typeof(SettingsViewModel),
            BuildSettingsChildren());
    }

    private List<NavigationItem> BuildInventoryChildren() =>
    [
        Leaf("On Hand", "PackageVariant", typeof(InventoryViewModel), "inventory", tabIndex: 0,
            requiredPermission: AppConstants.Permissions.InventoryMenuOnhand),
        Leaf("Ledger", "BookOpenPageVariant", typeof(InventoryViewModel), "inventory", tabIndex: 1,
            requiredPermission: AppConstants.Permissions.InventoryMenuLedger),
        Leaf("Adjustment", "SwapHorizontal", typeof(InventoryViewModel), "inventory", tabIndex: 2,
            requiredPermission: AppConstants.Permissions.InventoryMenuAdjustment),
        Leaf("Transfer", "TruckFast", typeof(InventoryViewModel), "inventory", tabIndex: 3,
            requiredPermission: AppConstants.Permissions.InventoryMenuTransfer),
        Leaf("Recent Transfer", "History", typeof(InventoryViewModel), "inventory", tabIndex: 4,
            requiredPermission: AppConstants.Permissions.InventoryMenuTransferHistory),
        Leaf("Shortage book", "ClipboardAlert", typeof(InventoryViewModel), "inventory", tabIndex: 5,
            requiredPermission: AppConstants.Permissions.InventoryMenuShortage),
    ];

    private List<NavigationItem> BuildMastersChildren() =>
    [
        Leaf("Suppliers", "Truck", typeof(MastersViewModel), "masters", tabIndex: 0,
            requiredPermission: AppConstants.Permissions.MastersMenuSuppliers),
        Leaf("Customers", "AccountGroup", typeof(MastersViewModel), "masters", tabIndex: 1,
            requiredPermission: AppConstants.Permissions.MastersMenuCustomers),
        Leaf("Doctors", "Doctor", typeof(MastersViewModel), "masters", tabIndex: 2,
            requiredPermission: AppConstants.Permissions.MastersMenuDoctors),
        Leaf("Manufacturers", "Factory", typeof(MastersViewModel), "masters", tabIndex: 3,
            requiredPermission: AppConstants.Permissions.MastersMenuManufacturers),
        Leaf("Employees", "BadgeAccountHorizontal", typeof(MastersViewModel), "masters", tabIndex: 4,
            requiredPermission: AppConstants.Permissions.MastersMenuEmployees),
        Leaf("Medicines", "Pill", typeof(MastersViewModel), "masters", tabIndex: 5,
            requiredPermission: AppConstants.Permissions.MastersMenuMedicines),
    ];

    private List<NavigationItem> BuildAccountingChildren() =>
    [
        Leaf("Parties", "AccountCash", typeof(AccountingViewModel), "accounting", tabIndex: 0,
            requiredPermission: AppConstants.Permissions.AccountingMenuParties),
        Leaf("Customer Dues", "CashMultiple", typeof(AccountingViewModel), "accounting", tabIndex: 1,
            requiredPermission: AppConstants.Permissions.AccountingMenuDues),
        Leaf("Vouchers", "Receipt", typeof(AccountingViewModel), "accounting", tabIndex: 2,
            requiredPermission: AppConstants.Permissions.AccountingMenuVouchers),
        Leaf("Cash Book", "BookOpenVariant", typeof(AccountingViewModel), "accounting", tabIndex: 3,
            requiredPermission: AppConstants.Permissions.AccountingMenuCashbook),
        Leaf("Journal", "NotebookOutline", typeof(AccountingViewModel), "accounting", tabIndex: 4,
            requiredPermission: AppConstants.Permissions.AccountingMenuJournal),
    ];

    private void AddReportsSubmenu()
    {
        var root = new NavMenuEntry("Reports", "ChartBar");
        foreach (var groupName in ReportCatalog.Groups)
        {
            var group = new NavMenuEntry(groupName, IconForReportGroup(groupName));
            foreach (var def in ReportCatalog.All.Where(d => d.Group == groupName))
            {
                var permission = ReportMenuPermissions.For(def.Kind);
                if (!CanAccessPermission(permission)) continue;

                var item = Leaf(def.Label, IconForReportKind(def.Kind), typeof(ReportsViewModel), "reports",
                    reportKind: def.Kind, requiredPermission: permission);
                group.AddChild(new NavMenuEntry(def.Label, IconForReportKind(def.Kind), item));
            }
            if (group.HasChildren)
                root.AddChild(group);
        }

        if (root.HasChildren)
            TopMenu.Add(root);
    }

    private static string IconForReportGroup(string group) => group switch
    {
        ReportCatalog.GroupSales => "PointOfSale",
        ReportCatalog.GroupPurchase => "TruckDelivery",
        ReportCatalog.GroupCustomers => "AccountGroup",
        ReportCatalog.GroupPayments => "CashMultiple",
        ReportCatalog.GroupGst => "FilePercent",
        ReportCatalog.GroupStock => "PackageVariantClosed",
        _ => "ChartBar"
    };

    private static string IconForReportKind(ReportKind kind) => kind switch
    {
        ReportKind.Sales or ReportKind.SalesDayWise => "PointOfSale",
        ReportKind.SalesByCustomer or ReportKind.CustomerOutstanding or ReportKind.CustomerReceipts => "Account",
        ReportKind.SalesByMedicine or ReportKind.MedicineReturns => "Pill",
        ReportKind.SalesByPaymentMode => "CreditCard",
        ReportKind.SalesCreditDue => "CashClock",
        ReportKind.Profit => "ChartLine",
        ReportKind.SaleReturns or ReportKind.PurchaseReturns => "AssignmentReturn",
        ReportKind.ScheduleRegister => "ClipboardText",
        ReportKind.Purchases or ReportKind.PurchasesBySupplier => "TruckDelivery",
        ReportKind.SupplierOutstanding or ReportKind.SupplierPayments => "Truck",
        ReportKind.PaymentVouchers or ReportKind.ReceiptVouchers => "Receipt",
        ReportKind.ExpenseRegister or ReportKind.ExpenseByAccount => "CashMinus",
        ReportKind.CashBookSummary => "BookOpenVariant",
        ReportKind.GstSummary => "FilePercent",
        ReportKind.Gstr1 => "FileExport",
        ReportKind.Gstr2B => "FileDocumentOutline",
        ReportKind.StockValuation or ReportKind.BatchStock => "PackageVariantClosed",
        ReportKind.Expiry => "CalendarAlert",
        ReportKind.LowStock => "AlertCircleOutline",
        ReportKind.SlowMovingStock => "TimerSand",
        _ => "ChartBar"
    };

    private List<NavigationItem> BuildSettingsChildren() =>
    [
        Leaf("Company", "Domain", typeof(SettingsViewModel), "settings", tabIndex: 0,
            requiredPermission: AppConstants.Permissions.SettingsMenuCompany),
        Leaf("Branches", "Store", typeof(SettingsViewModel), "settings", tabIndex: 1,
            requiredPermission: AppConstants.Permissions.SettingsMenuBranches),
        Leaf("Counters", "Monitor", typeof(SettingsViewModel), "settings", tabIndex: 2,
            requiredPermission: AppConstants.Permissions.SettingsMenuCounters),
        Leaf("Preferences", "Tune", typeof(SettingsViewModel), "settings", tabIndex: 3,
            requiredPermission: AppConstants.Permissions.SettingsMenuPreferences),
        Leaf("Medicine Mapping", "LinkVariant", typeof(SettingsViewModel), "settings", tabIndex: 4,
            requiredPermission: AppConstants.Permissions.SettingsMenuMedicineMapping),
        Leaf("New Medicine Mapping", "LinkPlus", typeof(SettingsViewModel), "settings", tabIndex: 5,
            requiredPermission: AppConstants.Permissions.SettingsMenuNewMedicineMapping),
        Leaf("MedWin Import", "DatabaseImport", typeof(SettingsViewModel), "settings", tabIndex: 6,
            requiredPermission: AppConstants.Permissions.SettingsMenuMedWinImport),
        Leaf("Roles & Permissions", "ShieldAccount", typeof(SettingsViewModel), "settings", tabIndex: 7,
            requiredPermission: AppConstants.Permissions.SettingsMenuRoles),
        Leaf("Users", "AccountMultiple", typeof(SettingsViewModel), "settings", tabIndex: 8,
            requiredPermission: AppConstants.Permissions.SettingsMenuUsers),
        Leaf("My Password", "LockReset", typeof(SettingsViewModel), "settings", tabIndex: 9,
            requiredPermission: AppConstants.Permissions.SettingsMenuPassword),
        Leaf("Appearance", "Palette", typeof(SettingsViewModel), "settings", tabIndex: 10,
            requiredPermission: AppConstants.Permissions.SettingsMenuAppearance),
        Leaf("Backup", "CloudUpload", typeof(SettingsViewModel), "settings", tabIndex: 11,
            requiredPermission: AppConstants.Permissions.SettingsMenuBackup),
        Leaf("Shop updates", "CloudDownload", typeof(SettingsViewModel), "settings", tabIndex: 12,
            requiredPermission: AppConstants.Permissions.SettingsMenuUpdates),
    ];

    private void AddModuleSubmenu(
        string groupLabel,
        string groupIcon,
        string module,
        Type viewModelType,
        List<NavigationItem> children)
    {
        var visible = children.Where(CanShow).ToList();
        if (visible.Count == 0) return;
        AddGroupOrLeaf(groupLabel, groupIcon, visible);
    }

    private void AddGroupOrLeaf(string groupLabel, string groupIcon, List<NavigationItem> children)
    {
        if (children.Count == 0) return;
        if (children.Count == 1)
        {
            TopMenu.Add(new NavMenuEntry(children[0].Label, children[0].IconKind, children[0]));
            return;
        }

        var group = new NavMenuEntry(groupLabel, groupIcon);
        foreach (var child in children)
            group.AddChild(new NavMenuEntry(child.Label, child.IconKind, child));
        TopMenu.Add(group);
    }

    private NavigationItem Leaf(
        string label,
        string icon,
        Type vm,
        string module,
        int? tabIndex = null,
        ReportKind? reportKind = null,
        string? requiredPermission = null)
    {
        var item = new NavigationItem(label, icon, vm, module, tabIndex, reportKind, requiredPermission);
        if (CanShow(item))
            MenuItems.Add(item);
        return item;
    }

    private bool CanAccessPermission(string permissionKey)
        => _currentUser.HasPermission(permissionKey);

    private bool CanShow(NavigationItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.RequiredPermission))
            return CanAccessPermission(item.RequiredPermission);

        if (item.TargetViewModel == typeof(SettingsViewModel))
            return _currentUser.CanAccessModule("settings") || _currentUser.CanAccessModule("users");
        return _currentUser.CanAccessModule(item.Module);
    }

    private void AddTopIfAllowed(NavigationItem item)
    {
        if (!CanShow(item)) return;
        TopMenu.Add(new NavMenuEntry(item.Label, item.IconKind, item));
    }
}
