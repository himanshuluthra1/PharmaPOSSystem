using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PharmaPOS.WPF.Services;

/// <summary>
/// Restores DataGrid column widths on load and persists them after the user resizes columns.
/// </summary>
public sealed class DataGridLayoutTracker : IDisposable
{
    private readonly DataGrid _grid;
    private readonly string _viewKey;
    private readonly IUiLayoutService _layout;
    private readonly List<(DataGridColumn Column, DependencyPropertyDescriptor Descriptor)> _hooks = new();
    private bool _applying;
    private bool _disposed;

    public DataGridLayoutTracker(DataGrid grid, string viewKey, IUiLayoutService layout)
    {
        _grid = grid;
        _viewKey = viewKey;
        _layout = layout;
        _grid.Loaded += OnLoaded;
        _grid.Unloaded += OnUnloaded;
        if (_grid.IsLoaded)
        {
            ApplySavedWidths();
            AttachColumnHooks();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplySavedWidths();
        AttachColumnHooks();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CaptureAndSave();
        DetachColumnHooks();
    }

    private void ApplySavedWidths()
    {
        var saved = _layout.GetGridColumnWidths(_viewKey);
        if (saved.Count == 0) return;

        _applying = true;
        try
        {
            foreach (var column in _grid.Columns)
            {
                var key = ColumnKey(column, _grid.Columns.IndexOf(column));
                if (!saved.TryGetValue(key, out var width) || width < 24) continue;
                column.Width = new DataGridLength(width);
            }
        }
        finally
        {
            _applying = false;
        }
    }

    private void AttachColumnHooks()
    {
        DetachColumnHooks();
        foreach (var column in _grid.Columns)
        {
            var descriptor = DependencyPropertyDescriptor.FromProperty(
                DataGridColumn.WidthProperty, typeof(DataGridColumn));
            if (descriptor is null) continue;
            descriptor.AddValueChanged(column, OnColumnWidthChanged);
            _hooks.Add((column, descriptor));
        }
    }

    private void DetachColumnHooks()
    {
        foreach (var (column, descriptor) in _hooks)
            descriptor.RemoveValueChanged(column, OnColumnWidthChanged);
        _hooks.Clear();
    }

    private void OnColumnWidthChanged(object? sender, EventArgs e)
    {
        if (_applying || _disposed) return;
        // Defer so Absolute size is settled after star→pixel conversion during drag.
        _grid.Dispatcher.BeginInvoke(DispatcherPriority.Background, CaptureAndSave);
    }

    private void CaptureAndSave()
    {
        if (_disposed || _applying) return;

        var widths = new Dictionary<string, double>(StringComparer.Ordinal);
        for (var i = 0; i < _grid.Columns.Count; i++)
        {
            var column = _grid.Columns[i];
            var px = column.ActualWidth;
            if (px < 24) continue;
            widths[ColumnKey(column, i)] = px;
        }

        if (widths.Count > 0)
            _layout.SetGridColumnWidths(_viewKey, widths);
    }

    public static string ColumnKey(DataGridColumn column, int index)
    {
        var header = column.Header?.ToString()?.Trim();
        return string.IsNullOrEmpty(header) ? $"Col{index}" : header;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CaptureAndSave();
        DetachColumnHooks();
        _grid.Loaded -= OnLoaded;
        _grid.Unloaded -= OnUnloaded;
    }
}
