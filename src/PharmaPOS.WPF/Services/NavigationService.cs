using Microsoft.Extensions.DependencyInjection;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Shared.Constants;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.ViewModels.Settings;

namespace PharmaPOS.WPF.Services;

public class NavigationService : INavigationService
{
    private readonly IServiceProvider _provider;
    private readonly ICurrentUserService _currentUser;
    private ObservableObject? _current;

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

        CurrentViewModel = (ObservableObject)_provider.GetRequiredService(viewModelType);
    }
}
