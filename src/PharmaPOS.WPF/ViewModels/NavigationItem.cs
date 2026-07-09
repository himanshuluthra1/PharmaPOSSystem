using PharmaPOS.WPF.Mvvm;

namespace PharmaPOS.WPF.ViewModels;

/// <summary>A single entry in the shell's navigation rail.</summary>
public class NavigationItem : ObservableObject
{
    public NavigationItem(string label, string iconKind, Type targetViewModel, string? permissionKey = null)
    {
        Label = label;
        IconKind = iconKind;
        TargetViewModel = targetViewModel;
        PermissionKey = permissionKey;
    }

    public string Label { get; }
    public string IconKind { get; }
    public Type TargetViewModel { get; }
    public string? PermissionKey { get; }
}
