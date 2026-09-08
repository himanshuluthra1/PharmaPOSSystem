using System.Collections.ObjectModel;
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

/// <summary>
/// Top-bar menu node: either a direct link (<see cref="Item"/>) or a group with
/// <see cref="Children"/> (submenu).
/// </summary>
public class NavMenuEntry : ObservableObject
{
    private bool _isActive;

    public NavMenuEntry(string label, string iconKind, NavigationItem? item = null)
    {
        Label = label;
        IconKind = iconKind;
        Item = item;
    }

    public string Label { get; }
    public string IconKind { get; }
    public NavigationItem? Item { get; }
    public ObservableCollection<NavMenuEntry> Children { get; } = new();
    public bool HasChildren => Children.Count > 0;
    public bool IsLeaf => Item is not null && !HasChildren;

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public void AddChild(NavMenuEntry child) => Children.Add(child);

    public bool Contains(NavigationItem? item)
    {
        if (item is null) return false;
        if (ReferenceEquals(Item, item)) return true;
        return Children.Any(c => c.Contains(item));
    }
}
