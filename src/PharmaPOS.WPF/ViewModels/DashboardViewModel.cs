using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Dashboard;
using PharmaPOS.Application.Features.ReportingSync;
using PharmaPOS.Application.Features.Settings;
using PharmaPOS.WPF.Mvvm;

namespace PharmaPOS.WPF.ViewModels;

/// <summary>Loads and exposes the dashboard KPIs and charts.</summary>
public class DashboardViewModel : ObservableObject
{
    private readonly IDashboardService _dashboardService;
    private readonly ISettingsService _settings;
    private readonly ICurrentUserService _currentUser;
    private readonly IStoreIdentityService _storeIdentity;

    private DashboardDto _data = new();
    private bool _isLoading;
    private bool _showTodaySales = true;
    private bool _showMonthlySales = true;

    public DashboardViewModel(
        IDashboardService dashboardService,
        ISettingsService settings,
        ICurrentUserService currentUser,
        IStoreIdentityService storeIdentity)
    {
        _dashboardService = dashboardService;
        _settings = settings;
        _currentUser = currentUser;
        _storeIdentity = storeIdentity;
        RefreshCommand = new AsyncRelayCommand(_ => LoadAsync());
        _ = LoadAsync();
    }

    public DashboardDto Data
    {
        get => _data;
        private set => SetProperty(ref _data, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool ShowTodaySales
    {
        get => _showTodaySales;
        private set => SetProperty(ref _showTodaySales, value);
    }

    public bool ShowMonthlySales
    {
        get => _showMonthlySales;
        private set => SetProperty(ref _showMonthlySales, value);
    }

    public ObservableCollection<TopMedicineDto> TopMedicines { get; } = new();
    public ObservableCollection<MonthlySalesDto> MonthlySales { get; } = new();

    public string Greeting => BuildGreeting();

    public string StoreCodeDisplay =>
        string.IsNullOrWhiteSpace(_storeIdentity.StoreCode)
            ? "Store code not set"
            : string.IsNullOrWhiteSpace(_storeIdentity.StoreId)
                ? $"Store code: {_storeIdentity.StoreCode}"
                : $"Store code: {_storeIdentity.StoreCode}  ·  Store ID: {_storeIdentity.StoreId}";

    public ICommand RefreshCommand { get; }

    public async Task LoadAsync()
    {
        IsLoading = true;
        LoadError = null;
        try
        {
            var prefs = await _settings.GetPreferencesAsync();
            ShowTodaySales = prefs.ShowDashboardTodaySales;
            ShowMonthlySales = prefs.ShowDashboardMonthlySales;

            var branchId = _currentUser.CurrentUser?.BranchId;
            var data = await _dashboardService.GetDashboardAsync(branchId);
            Data = data;

            TopMedicines.Clear();
            foreach (var m in data.TopSellingMedicines) TopMedicines.Add(m);

            MonthlySales.Clear();
            foreach (var m in data.MonthlySales) MonthlySales.Add(m);
        }
        catch (Exception ex)
        {
            // Dashboard is non-critical; show zeros but surface why so failures
            // aren't hidden.
            Data = new DashboardDto();
            var detail = ex.InnerException?.Message ?? ex.Message;
            LoadError = $"Could not load dashboard data: {detail}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private string? _loadError;
    public string? LoadError
    {
        get => _loadError;
        private set => SetProperty(ref _loadError, value);
    }

    private string BuildGreeting()
    {
        var name = _currentUser.CurrentUser?.FullName ?? "there";
        var hour = DateTime.Now.Hour;
        var part = hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";
        return $"{part}, {name}";
    }
}
