using Microsoft.Extensions.DependencyInjection;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.ViewModels.Sales;
using PharmaPOS.WPF.ViewModels.Settings;

namespace PharmaPOS.WPF.Services;

public class NavigationService : INavigationService
{
    private readonly IServiceProvider _provider;
    private readonly ICurrentUserService _currentUser;
    private ObservableObject? _current;
    private readonly Dictionary<Type, ObservableObject> _sessionCache = new();

    public NavigationService(IServiceProvider provider, ICurrentUserService currentUser)
    {
        _provider = provider;
        _currentUser = currentUser;
    }

    public ObservableObject? CurrentViewModel
    {
        get => _current;
        private set
        {
            _current = value;
            CurrentChanged?.Invoke();
        }
    }

    public event Action? CurrentChanged;

    public void NavigateTo<TViewModel>() where TViewModel : ObservableObject
        => NavigateTo(typeof(TViewModel));

    public void NavigateTo(Type viewModelType)
    {
        if (viewModelType == typeof(SettingsViewModel))
        {
            if (!_currentUser.CanAccessModule("settings") && !_currentUser.CanAccessModule("users"))
                return;
        }
        else
        {
            var module = ModulePermissions.For(viewModelType);
            if (module is not null && !_currentUser.CanAccessModule(module))
                return;
        }

        if (ShouldCache(viewModelType))
        {
            if (!_sessionCache.TryGetValue(viewModelType, out var cached))
            {
                cached = (ObservableObject)_provider.GetRequiredService(viewModelType);
                _sessionCache[viewModelType] = cached;
            }

            CurrentViewModel = cached;
            return;
        }

        CurrentViewModel = (ObservableObject)_provider.GetRequiredService(viewModelType);
    }

    public void ClearSessionCache() => _sessionCache.Clear();

    private static bool ShouldCache(Type viewModelType)
        => viewModelType == typeof(SalesViewModel);
}
