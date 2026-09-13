using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;

namespace PharmaPOS.WPF.Services;

/// <summary>
/// Shared Enter / double-click handlers for opening invoice drill-down from DataGrids.
/// </summary>
public static class InvoiceDrillDown
{
    public static IInvoiceViewerDialogService Viewer
        => App.Services.GetRequiredService<IInvoiceViewerDialogService>();

    public static Task OpenSaleAsync(int saleId)
        => saleId > 0 ? Viewer.ShowSaleAsync(saleId) : Task.CompletedTask;

    public static Task OpenPurchaseAsync(int purchaseId)
        => purchaseId > 0 ? Viewer.ShowPurchaseAsync(purchaseId) : Task.CompletedTask;

    public static bool TryHandleKey(KeyEventArgs e, object? selectedItem, Func<object, Task> openAsync)
    {
        if (e.Key != Key.Enter || selectedItem is null) return false;
        e.Handled = true;
        _ = openAsync(selectedItem);
        return true;
    }

    public static void HandleDoubleClick(MouseButtonEventArgs e, object? source, Func<object, Task> openAsync)
    {
        if (source is not DataGrid { SelectedItem: { } item }) return;
        e.Handled = true;
        _ = openAsync(item);
    }
}
