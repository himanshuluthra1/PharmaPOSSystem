using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
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

    private NavigationItem? _selectedItem;
    private bool _isDarkMode;

    public MainViewModel(
        INavigationService navigation,
        ICurrentUserService currentUser,
        IThemeService themeService)
    {
        _navigation = navigation;
        _currentUser = currentUser;
        _themeService = themeService;

        _navigation.CurrentChanged += () => OnPropertyChanged(nameof(CurrentViewModel));

        NavigateCommand = new RelayCommand(p => Navigate(p as NavigationItem));
        ToggleThemeCommand = new RelayCommand(_ => ToggleTheme());

        BuildMenu();
        SelectedItem = MenuItems.FirstOrDefault();
    }

    /// <summary>Raised when the user chooses to sign out; the host returns to login.</summary>
    public event Action? LogoutRequested;

    public ObservableCollection<NavigationItem> MenuItems { get; } = new();

    public ObservableObject? CurrentViewModel => _navigation.CurrentViewModel;

    public string UserName => _currentUser.CurrentUser?.FullName ?? "Guest";
    public string RoleName => _currentUser.CurrentUser?.RoleName ?? string.Empty;
    public string BranchName => _currentUser.CurrentUser?.BranchName ?? "Head Office";

    public NavigationItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value) && value is not null)
                _navigation.NavigateTo(value.TargetViewModel);
        }
    }

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

    private void ToggleTheme() => IsDarkMode = !IsDarkMode;

    /// <summary>Navigate to the Sales module (F2 shortcut).</summary>
    public void NavigateToSales()
    {
        var sales = MenuItems.FirstOrDefault(m => m.TargetViewModel == typeof(SalesViewModel));
        if (sales is not null) SelectedItem = sales;
    }

    private void BuildMenu()
    {
        var all = new[]
        {
            new NavigationItem("Dashboard", "ViewDashboard", typeof(DashboardViewModel), "dashboard"),
            new NavigationItem("Sales (F2)", "PointOfSale", typeof(SalesViewModel), "sales"),
            new NavigationItem("Purchase", "TruckDelivery", typeof(PurchaseViewModel), "purchase"),
            new NavigationItem("Inventory", "PackageVariantClosed", typeof(InventoryViewModel), "inventory"),
            new NavigationItem("Masters", "DatabaseCog", typeof(MastersViewModel), "masters"),
            new NavigationItem("Accounting", "Calculator", typeof(AccountingViewModel), "accounting"),
            new NavigationItem("Reports", "ChartBar", typeof(ReportsViewModel), "reports"),
            new NavigationItem("Settings", "Cog", typeof(SettingsViewModel), "settings"),
        };

        foreach (var item in all)
        {
            if (item.TargetViewModel == typeof(SettingsViewModel))
            {
                if (_currentUser.CanAccessModule("settings") || _currentUser.CanAccessModule("users"))
                    MenuItems.Add(item);
                continue;
            }

            if (_currentUser.CanAccessModule(item.Module))
                MenuItems.Add(item);
        }
    }
}
