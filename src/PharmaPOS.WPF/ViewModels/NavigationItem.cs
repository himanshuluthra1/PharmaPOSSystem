using System.Collections.ObjectModel;
using PharmaPOS.Application.Features.Reports;
using PharmaPOS.WPF.Mvvm;

namespace PharmaPOS.WPF.ViewModels;

/// <summary>A single entry in the shell's navigation rail / top menu.</summary>
public class NavigationItem : ObservableObject
{
    public NavigationItem(
        string label,
        string iconKind,
        Type targetViewModel,
        string module,
        int? tabIndex = null,
        ReportKind? reportKind = null,
        string? requiredPermission = null)
    {
        Label = label;
        IconKind = iconKind;
        TargetViewModel = targetViewModel;
        Module = module;
        TabIndex = tabIndex;
        ReportKind = reportKind;
        RequiredPermission = requiredPermission;
    }

    public string Label { get; }
    public string IconKind { get; }
    public Type TargetViewModel { get; }
    public string Module { get; }

    /// <summary>Optional tab index within a multi-tab module (Inventory, Masters, Accounting, Settings).</summary>
    public int? TabIndex { get; }

    /// <summary>Optional report kind when targeting the Reports module.</summary>
    public ReportKind? ReportKind { get; }

    /// <summary>Permission key required to show this menu leaf (<c>{module}.manage</c> still grants all).</summary>
    public string? RequiredPermission { get; }
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
