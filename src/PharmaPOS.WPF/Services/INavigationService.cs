using PharmaPOS.WPF.Mvvm;

namespace PharmaPOS.WPF.Services;

/// <summary>
/// Coordinates in-shell navigation by resolving view models from DI and exposing
/// the currently active one for the shell's content host to bind to.
/// </summary>
public interface INavigationService
{
    ObservableObject? CurrentViewModel { get; }
    event Action? CurrentChanged;

    void NavigateTo<TViewModel>() where TViewModel : ObservableObject;
    void NavigateTo(Type viewModelType);
}
