using System.Windows.Controls;
using System.Windows.Input;
using PharmaPOS.Application.Features.ExpiryReturns;
using PharmaPOS.WPF.Services;
using PharmaPOS.WPF.ViewModels.Purchases;

namespace PharmaPOS.WPF.Views;

public partial class ExpiryReturnView : UserControl
{
    public ExpiryReturnView()
    {
        InitializeComponent();
    }

    private void BatchesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => InvoiceDrillDown.HandleDoubleClick(e, sender, OpenBatchPurchaseAsync);

    private void BatchesGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not DataGrid { SelectedItem: ExpiryClaimLineViewModel line }) return;
        InvoiceDrillDown.TryHandleKey(e, line, OpenBatchPurchaseAsync);
    }

    private void ClaimsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => InvoiceDrillDown.HandleDoubleClick(e, sender, OpenClaimSettlementPurchaseAsync);

    private void ClaimsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not DataGrid { SelectedItem: ExpiryClaimListRowDto row }) return;
        InvoiceDrillDown.TryHandleKey(e, row, OpenClaimSettlementPurchaseAsync);
    }

    private void ClaimLinesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => InvoiceDrillDown.HandleDoubleClick(e, sender, OpenClaimLinePurchaseAsync);

    private void ClaimLinesGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not DataGrid { SelectedItem: ExpiryClaimEditableLineViewModel line }) return;
        InvoiceDrillDown.TryHandleKey(e, line, OpenClaimLinePurchaseAsync);
    }

    private static Task OpenBatchPurchaseAsync(object item)
    {
        if (item is not ExpiryClaimLineViewModel line) return Task.CompletedTask;
        return InvoiceDrillDown.OpenPurchaseAsync(line.Source.PurchaseId ?? 0);
    }

    private static Task OpenClaimSettlementPurchaseAsync(object item)
    {
        if (item is not ExpiryClaimListRowDto row) return Task.CompletedTask;
        return InvoiceDrillDown.OpenPurchaseAsync(row.SettledAgainstPurchaseId ?? 0);
    }

    private static Task OpenClaimLinePurchaseAsync(object item)
    {
        if (item is not ExpiryClaimEditableLineViewModel line) return Task.CompletedTask;
        return InvoiceDrillDown.OpenPurchaseAsync(line.PurchaseId ?? 0);
    }
}
