using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.ReportingSync;
using PharmaPOS.Application.Features.Reports;
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
    private readonly INavigationService _navigation;
    private readonly ICurrentUserService _currentUser;
    private readonly IThemeService _themeService;
    private readonly IStoreIdentityService _storeIdentity;

    private NavigationItem? _selectedItem;
    private bool _isDarkMode;
    private bool _suppressNavSync;

    public MainViewModel(
        INavigationService navigation,
        ICurrentUserService currentUser,
        IThemeService themeService,
        IStoreIdentityService storeIdentity)
    {
        _navigation = navigation;
        _currentUser = currentUser;
        _themeService = themeService;
        _storeIdentity = storeIdentity;

        _navigation.CurrentChanged += () =>
        {
            OnPropertyChanged(nameof(CurrentViewModel));
            OnPropertyChanged(nameof(ActiveSales));
            OnPropertyChanged(nameof(ShowSalesCounterBar));
            SyncSelectedItemToCurrentView();
        };

        NavigateCommand = new RelayCommand(p => Navigate(p as NavigationItem));
        ToggleThemeCommand = new RelayCommand(_ => ToggleTheme());

        BuildMenu();
        SelectedItem = MenuItems.FirstOrDefault();
        RefreshMenuActiveStates();
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

    /// <summary>Shortcuts for the currently selected module (shown in the green app bar).</summary>
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

    private static IReadOnlyList<ShortcutHint> GetShortcutsForModule(string? module) => module switch
    {
        "sales" =>
        [
            new("F2", "Sales"),
            new("Ctrl+T", "New customer"),
            new("Ctrl+1-4", "Switch bill"),
            new("Ctrl+W", "Close bill"),
            new("Esc", "Clear bill"),
            new("F3", "Customer / Save"),
            new("F4", "Medicine details"),
            new("Ctrl+L", "Medicine ledger"),
            new("F5", "Substitute"),
            new("F6", "Refill / last sale"),
            new("F8", "Sale return"),
            new("F9", "Save / Print"),
            new("Enter", "Search item")
        ],
        "purchase" =>
        [
            new("F2", "Sales"),
            new("Esc", "New purchase"),
            new("F9", "Save"),
            new("Ctrl+L", "Medicine ledger"),
            new("Enter", "Search item / barcode"),
            new("Scan bill", "OCR supplier invoice")
        ],
        "inventory" =>
        [
            new("F2", "Sales"),
            new("Ctrl+L", "Medicine ledger")
        ],
        "masters" =>
        [
            new("F2", "Sales"),
            new("Ctrl+L", "Medicine ledger")
        ],
        "accounting" =>
        [
            new("F2", "Sales")
        ],
        "reports" =>
        [
            new("F2", "Sales"),
            new("Enter", "Open bill")
        ],
        "settings" =>
        [
            new("F2", "Sales")
        ],
        "dashboard" =>
        [
            new("F2", "Sales")
        ],
        _ =>
        [
            new("F2", "Sales")
        ]
    };

    private static readonly IReadOnlyList<ShortcutHint> PurchaseReturnShortcuts =
    [
        new("F2", "Sales"),
        new("F3", "Search bill"),
        new("F9", "Process return"),
        new("Enter", "Search")
    ];

    private void BuildMenu()
    {
        var dashboard = Leaf("Dashboard", "ViewDashboard", typeof(DashboardViewModel), "dashboard");
        var sales = Leaf("Sales (F2)", "PointOfSale", typeof(SalesViewModel), "sales");
        var purchase = Leaf("Purchase invoice", "TruckDelivery", typeof(PurchaseViewModel), "purchase");
        var purchaseOrder = Leaf("Purchase order", "ClipboardText", typeof(PurchaseOrderViewModel), "purchase");
        var purchaseReturn = Leaf("Purchase return", "AssignmentReturn", typeof(PurchaseReturnViewModel), "purchase");
        var expiry = Leaf("Expiry to company", "CalendarRemove", typeof(ExpiryReturnViewModel), "purchase");

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

        AddModuleSubmenu(
            "Reports", "ChartBar", "reports", typeof(ReportsViewModel),
            BuildReportsChildren());

        AddModuleSubmenu(
            "Settings", "Cog", "settings", typeof(SettingsViewModel),
            BuildSettingsChildren());
    }

    private List<NavigationItem> BuildInventoryChildren()
    {
        var canAdjust = _currentUser.HasAnyPermission(
            AppConstants.Permissions.InventoryAdjust, AppConstants.Permissions.InventoryManage);
        var canTransfer = _currentUser.HasAnyPermission(
            AppConstants.Permissions.InventoryTransfer, AppConstants.Permissions.InventoryManage);

        var items = new List<NavigationItem>
        {
            Leaf("On Hand", "PackageVariant", typeof(InventoryViewModel), "inventory", tabIndex: 0),
            Leaf("Ledger", "BookOpenPageVariant", typeof(InventoryViewModel), "inventory", tabIndex: 1),
        };
        if (canAdjust)
            items.Add(Leaf("Adjustment", "SwapHorizontal", typeof(InventoryViewModel), "inventory", tabIndex: 2));
        if (canTransfer)
        {
            items.Add(Leaf("Transfer", "TruckFast", typeof(InventoryViewModel), "inventory", tabIndex: 3));
            items.Add(Leaf("Recent Transfer", "History", typeof(InventoryViewModel), "inventory", tabIndex: 4));
        }
        items.Add(Leaf("Shortage book", "ClipboardAlert", typeof(InventoryViewModel), "inventory", tabIndex: 5));
        return items;
    }

    private List<NavigationItem> BuildMastersChildren() =>
    [
        Leaf("Suppliers", "Truck", typeof(MastersViewModel), "masters", tabIndex: 0),
        Leaf("Customers", "AccountGroup", typeof(MastersViewModel), "masters", tabIndex: 1),
        Leaf("Doctors", "Doctor", typeof(MastersViewModel), "masters", tabIndex: 2),
        Leaf("Manufacturers", "Factory", typeof(MastersViewModel), "masters", tabIndex: 3),
        Leaf("Employees", "BadgeAccountHorizontal", typeof(MastersViewModel), "masters", tabIndex: 4),
        Leaf("Medicines", "Pill", typeof(MastersViewModel), "masters", tabIndex: 5),
    ];

    private List<NavigationItem> BuildAccountingChildren()
    {
        var canVouchers = _currentUser.HasAnyPermission(
            AppConstants.Permissions.AccountingVouchers, AppConstants.Permissions.AccountingManage);
        var canJournal = _currentUser.HasAnyPermission(
            AppConstants.Permissions.AccountingJournal, AppConstants.Permissions.AccountingView,
            AppConstants.Permissions.AccountingManage);

        var items = new List<NavigationItem>
        {
            Leaf("Parties", "AccountCash", typeof(AccountingViewModel), "accounting", tabIndex: 0),
            Leaf("Customer Dues", "CashMultiple", typeof(AccountingViewModel), "accounting", tabIndex: 1),
        };
        if (canVouchers)
            items.Add(Leaf("Vouchers", "Receipt", typeof(AccountingViewModel), "accounting", tabIndex: 2));
        items.Add(Leaf("Cash Book", "BookOpenVariant", typeof(AccountingViewModel), "accounting", tabIndex: 3));
        if (canJournal)
            items.Add(Leaf("Journal", "NotebookOutline", typeof(AccountingViewModel), "accounting", tabIndex: 4));
        return items;
    }

    private List<NavigationItem> BuildReportsChildren() =>
    [
        Leaf("Sales Report", "PointOfSale", typeof(ReportsViewModel), "reports", reportKind: ReportKind.Sales),
        Leaf("Purchase Report", "TruckDelivery", typeof(ReportsViewModel), "reports", reportKind: ReportKind.Purchases),
        Leaf("GST Summary", "FilePercent", typeof(ReportsViewModel), "reports", reportKind: ReportKind.GstSummary),
        Leaf("GSTR-1 export", "FileExport", typeof(ReportsViewModel), "reports", reportKind: ReportKind.Gstr1),
        Leaf("GSTR-2B worksheet", "FileDocumentOutline", typeof(ReportsViewModel), "reports", reportKind: ReportKind.Gstr2B),
        Leaf("Gross Profit", "ChartLine", typeof(ReportsViewModel), "reports", reportKind: ReportKind.Profit),
        Leaf("Sales by Medicine", "Pill", typeof(ReportsViewModel), "reports", reportKind: ReportKind.SalesByMedicine),
        Leaf("Schedule H / H1 Register", "ClipboardText", typeof(ReportsViewModel), "reports", reportKind: ReportKind.ScheduleRegister),
        Leaf("Stock Valuation", "PackageVariantClosed", typeof(ReportsViewModel), "reports", reportKind: ReportKind.StockValuation),
        Leaf("Expiry Report", "CalendarAlert", typeof(ReportsViewModel), "reports", reportKind: ReportKind.Expiry),
        Leaf("Low Stock", "AlertCircleOutline", typeof(ReportsViewModel), "reports", reportKind: ReportKind.LowStock),
        Leaf("Sale Returns", "AssignmentReturn", typeof(ReportsViewModel), "reports", reportKind: ReportKind.SaleReturns),
        Leaf("Medicine-wise Returns", "BackupRestore", typeof(ReportsViewModel), "reports", reportKind: ReportKind.MedicineReturns),
    ];

    private List<NavigationItem> BuildSettingsChildren()
    {
        var canCompany = _currentUser.HasAnyPermission(
            AppConstants.Permissions.SettingsCompany, AppConstants.Permissions.SettingsManage);
        var canBranches = _currentUser.HasAnyPermission(
            AppConstants.Permissions.SettingsBranches, AppConstants.Permissions.SettingsManage);
        var canPreferences = _currentUser.HasAnyPermission(
            AppConstants.Permissions.SettingsPreferences, AppConstants.Permissions.SettingsManage);
        var canUsers = _currentUser.HasAnyPermission(
            AppConstants.Permissions.UsersEdit, AppConstants.Permissions.UsersManage);
        var canRoles = _currentUser.HasAnyPermission(
            AppConstants.Permissions.UsersRoles, AppConstants.Permissions.UsersManage);
        var canAccessSettings = _currentUser.CanAccessModule("settings");
        var canMedicineMapping = _currentUser.HasAnyPermission(AppConstants.Permissions.SettingsManage)
            || canAccessSettings;
        var canMedWin = canMedicineMapping;

        var items = new List<NavigationItem>();
        if (canCompany)
            items.Add(Leaf("Company", "Domain", typeof(SettingsViewModel), "settings", tabIndex: 0));
        if (canBranches)
        {
            items.Add(Leaf("Branches", "Store", typeof(SettingsViewModel), "settings", tabIndex: 1));
            items.Add(Leaf("Counters", "Monitor", typeof(SettingsViewModel), "settings", tabIndex: 2));
        }
        if (canPreferences)
            items.Add(Leaf("Preferences", "Tune", typeof(SettingsViewModel), "settings", tabIndex: 3));
        if (canMedicineMapping)
            items.Add(Leaf("Medicine Mapping", "LinkVariant", typeof(SettingsViewModel), "settings", tabIndex: 4));
        if (canMedWin)
            items.Add(Leaf("MedWin Import", "DatabaseImport", typeof(SettingsViewModel), "settings", tabIndex: 5));
        if (canRoles)
            items.Add(Leaf("Roles & Permissions", "ShieldAccount", typeof(SettingsViewModel), "settings", tabIndex: 6));
        if (canUsers)
            items.Add(Leaf("Users", "AccountMultiple", typeof(SettingsViewModel), "settings", tabIndex: 7));
        items.Add(Leaf("My Password", "LockReset", typeof(SettingsViewModel), "settings", tabIndex: 8));
        items.Add(Leaf("Appearance", "Palette", typeof(SettingsViewModel), "settings", tabIndex: 9));
        if (canPreferences)
            items.Add(Leaf("Backup", "CloudUpload", typeof(SettingsViewModel), "settings", tabIndex: 10));
        return items;
    }

    private void AddModuleSubmenu(
        string groupLabel,
        string groupIcon,
        string module,
        Type viewModelType,
        List<NavigationItem> children)
    {
        // Settings may be visible via users module even when settings module is locked.
        var allowed = viewModelType == typeof(SettingsViewModel)
            ? CanShow(new NavigationItem(groupLabel, groupIcon, viewModelType, module))
            : _currentUser.CanAccessModule(module);
        if (!allowed) return;

        var visible = children.Where(CanShow).ToList();
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
        ReportKind? reportKind = null)
    {
        var item = new NavigationItem(label, icon, vm, module, tabIndex, reportKind);
        if (CanShow(item))
            MenuItems.Add(item);
        return item;
    }

    private bool CanShow(NavigationItem item)
    {
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
