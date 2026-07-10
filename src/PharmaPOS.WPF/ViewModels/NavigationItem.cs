using PharmaPOS.WPF.Mvvm;

namespace PharmaPOS.WPF.ViewModels;

/// <summary>A single entry in the shell's navigation rail.</summary>
public class NavigationItem : ObservableObject
{
    public NavigationItem(string label, string iconKind, Type targetViewModel, string module)
    {
        Label = label;
        IconKind = iconKind;
        TargetViewModel = targetViewModel;
        Module = module;
    }

    public string Label { get; }
    public string IconKind { get; }
    public Type TargetViewModel { get; }
    public string Module { get; }
}
