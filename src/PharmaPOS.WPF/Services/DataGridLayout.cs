using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;

namespace PharmaPOS.WPF.Services;

/// <summary>
/// Attaches <see cref="DataGridLayoutTracker"/> to DataGrids so column drag-widths
/// persist per view. Call <see cref="Register"/> once at startup.
/// </summary>
public static class DataGridLayout
{
    public static readonly DependencyProperty PersistKeyProperty =
        DependencyProperty.RegisterAttached(
            "PersistKey",
            typeof(string),
            typeof(DataGridLayout),
            new PropertyMetadata(null));

    private static readonly DependencyProperty TrackerProperty =
        DependencyProperty.RegisterAttached(
            "Tracker",
            typeof(DataGridLayoutTracker),
            typeof(DataGridLayout),
            new PropertyMetadata(null));

    public static void SetPersistKey(DependencyObject element, string? value) =>
        element.SetValue(PersistKeyProperty, value);

    public static string? GetPersistKey(DependencyObject element) =>
        (string?)element.GetValue(PersistKeyProperty);

    public static void Register()
    {
        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnDataGridLoaded));
    }

    private static void OnDataGridLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (grid.GetValue(TrackerProperty) is DataGridLayoutTracker) return;

        var layout = App.Services?.GetService<IUiLayoutService>();
        if (layout is null) return;

        var key = GetPersistKey(grid);
        if (string.IsNullOrWhiteSpace(key))
            key = BuildKey(grid);

        var tracker = new DataGridLayoutTracker(grid, key, layout);
        grid.SetValue(TrackerProperty, tracker);

        // Allow re-attach after the visual tree unloads (module navigation).
        void OnUnloaded(object? _, RoutedEventArgs __)
        {
            grid.Unloaded -= OnUnloaded;
            if (grid.GetValue(TrackerProperty) is DataGridLayoutTracker existing)
            {
                existing.Dispose();
                grid.ClearValue(TrackerProperty);
            }
        }

        grid.Unloaded += OnUnloaded;
    }

    private static string BuildKey(DataGrid grid)
    {
        var host = FindAncestor<UserControl>(grid)
                   ?? (FrameworkElement?)FindAncestor<Window>(grid);
        var hostName = host?.GetType().Name ?? "Grid";
        if (!string.IsNullOrWhiteSpace(grid.Name))
            return $"{hostName}.{grid.Name}";

        // Unnamed grids: stable key from column headers so each screen stays distinct.
        var headers = string.Join("|", grid.Columns.Select(c => c.Header?.ToString()?.Trim() ?? ""));
        if (string.IsNullOrEmpty(headers))
            return hostName;

        var hash = headers.GetHashCode(StringComparison.Ordinal).ToString("X8");
        return $"{hostName}.{hash}";
    }

    private static T? FindAncestor<T>(DependencyObject? start) where T : class
    {
        for (var current = start; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match) return match;
        }
        return null;
    }
}
