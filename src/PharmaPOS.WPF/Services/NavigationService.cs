using Microsoft.Extensions.DependencyInjection;
using PharmaPOS.WPF.Mvvm;

namespace PharmaPOS.WPF.Services;

public class NavigationService : INavigationService
{
    private readonly IServiceProvider _provider;
    private ObservableObject? _current;

    public NavigationService(IServiceProvider provider) => _provider = provider;

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
        => CurrentViewModel = _provider.GetRequiredService<TViewModel>();

    public void NavigateTo(Type viewModelType)
        => CurrentViewModel = (ObservableObject)_provider.GetRequiredService(viewModelType);
}
