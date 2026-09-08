using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.ReportingSync;
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
            if (SetProperty(ref _selectedItem, value) && value is not null)
            {
                _navigation.NavigateTo(value.TargetViewModel);
                OnPropertyChanged(nameof(ActiveShortcuts));
                OnPropertyChanged(nameof(HasActiveShortcuts));
                RefreshMenuActiveStates();
            }
        }
    }

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
        var currentType = _navigation.CurrentViewModel?.GetType();
        if (currentType is null) return;
        var match = MenuItems.FirstOrDefault(m => m.TargetViewModel == currentType);
        if (match is null || ReferenceEquals(_selectedItem, match)) return;
        _selectedItem = match;
        OnPropertyChanged(nameof(SelectedItem));
        OnPropertyChanged(nameof(ActiveShortcuts));
        OnPropertyChanged(nameof(HasActiveShortcuts));
        RefreshMenuActiveStates();
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
        var inventory = Leaf("Inventory", "PackageVariantClosed", typeof(InventoryViewModel), "inventory");
        var masters = Leaf("Masters", "DatabaseCog", typeof(MastersViewModel), "masters");
        var accounting = Leaf("Accounting", "Calculator", typeof(AccountingViewModel), "accounting");
        var reports = Leaf("Reports", "ChartBar", typeof(ReportsViewModel), "reports");
        var settings = Leaf("Settings", "Cog", typeof(SettingsViewModel), "settings");

        AddTopIfAllowed(dashboard);
        AddTopIfAllowed(sales);

        var purchaseChildren = new[] { purchase, purchaseOrder, purchaseReturn, expiry }
            .Where(CanShow)
            .ToList();
        if (purchaseChildren.Count == 1)
        {
            // Single purchase screen → show as a top-level item (no empty submenu).
            TopMenu.Add(new NavMenuEntry(purchaseChildren[0].Label, purchaseChildren[0].IconKind, purchaseChildren[0]));
        }
        else if (purchaseChildren.Count > 1)
        {
            var group = new NavMenuEntry("Purchases", "TruckDelivery");
            foreach (var child in purchaseChildren)
                group.AddChild(new NavMenuEntry(child.Label, child.IconKind, child));
            TopMenu.Add(group);
        }

        AddTopIfAllowed(inventory);
        AddTopIfAllowed(masters);
        AddTopIfAllowed(accounting);
        AddTopIfAllowed(reports);

        if (CanShow(settings))
            TopMenu.Add(new NavMenuEntry(settings.Label, settings.IconKind, settings));
    }

    private NavigationItem Leaf(string label, string icon, Type vm, string module)
    {
        var item = new NavigationItem(label, icon, vm, module);
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
