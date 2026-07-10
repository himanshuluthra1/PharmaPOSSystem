using PharmaPOS.WPF.Mvvm;

namespace PharmaPOS.WPF.ViewModels;

/// <summary>
/// Base for module screens that are scaffolded but not yet implemented. Each
/// derived type describes the module and the features planned for it, so the
/// shell navigation is fully wired ahead of feature work.
/// </summary>
public abstract class PlaceholderViewModel : ObservableObject
{
    public abstract string Title { get; }
    public abstract string Description { get; }
    public abstract string[] PlannedFeatures { get; }
}
