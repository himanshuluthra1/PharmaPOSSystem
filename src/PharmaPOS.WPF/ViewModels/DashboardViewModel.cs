using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Dashboard;
using PharmaPOS.WPF.Mvvm;

namespace PharmaPOS.WPF.ViewModels;

/// <summary>Loads and exposes the dashboard KPIs and charts.</summary>
public class DashboardViewModel : ObservableObject
{
    private readonly IDashboardService _dashboardService;
    private readonly ICurrentUserService _currentUser;

    private DashboardDto _data = new();
    private bool _isLoading;

    public DashboardViewModel(IDashboardService dashboardService, ICurrentUserService currentUser)
    {
        _dashboardService = dashboardService;
        _currentUser = currentUser;
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

    public ObservableCollection<TopMedicineDto> TopMedicines { get; } = new();
    public ObservableCollection<MonthlySalesDto> MonthlySales { get; } = new();

    public string Greeting => BuildGreeting();

    public ICommand RefreshCommand { get; }

    public async Task LoadAsync()
    {
        IsLoading = true;
        LoadError = null;
        try
        {
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
