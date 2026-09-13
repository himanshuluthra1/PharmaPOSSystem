using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PharmaPOS.Application.Features.PurchaseReturns;
using PharmaPOS.WPF.Services;
using PharmaPOS.WPF.ViewModels.Purchases;

namespace PharmaPOS.WPF.Views;

public partial class PurchaseReturnView : UserControl
{
    public PurchaseReturnView()
    {
        InitializeComponent();
    }

    private PurchaseReturnViewModel? ViewModel => DataContext as PurchaseReturnViewModel;

    private void SearchGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control
            && sender is DataGrid { SelectedItem: PurchaseReturnSearchResultDto row })
        {
            e.Handled = true;
            _ = InvoiceDrillDown.OpenPurchaseAsync(row.PurchaseId);
            return;
        }

        if (ViewModel?.LoadPurchaseCommand.CanExecute(null) == true)
            ViewModel.LoadPurchaseCommand.Execute(null);
    }

    private void SearchGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not DataGrid { SelectedItem: PurchaseReturnSearchResultDto row })
            return;

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            _ = InvoiceDrillDown.OpenPurchaseAsync(row.PurchaseId);
            return;
        }

        if (ViewModel?.LoadPurchaseCommand.CanExecute(null) == true)
        {
            e.Handled = true;
            ViewModel.LoadPurchaseCommand.Execute(null);
        }
    }

    private void ReturnRecordsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => InvoiceDrillDown.HandleDoubleClick(e, sender, OpenReturnRecordPurchaseAsync);

    private void ReturnRecordsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not DataGrid { SelectedItem: PurchaseReturnListRowDto row }) return;
        InvoiceDrillDown.TryHandleKey(e, row, OpenReturnRecordPurchaseAsync);
    }

    private static Task OpenReturnRecordPurchaseAsync(object item)
    {
        if (item is not PurchaseReturnListRowDto row) return Task.CompletedTask;
        var purchaseId = row.PurchaseId ?? row.SettledAgainstPurchaseId ?? 0;
        return InvoiceDrillDown.OpenPurchaseAsync(purchaseId);
    }

    private void DirectSupplierBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is null) return;

        switch (e.Key)
        {
            case Key.Down:
                ViewModel.MoveSupplierSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                ViewModel.MoveSupplierSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                ViewModel.ConfirmSupplierSelection();
                e.Handled = true;
                break;
            case Key.Escape:
                ViewModel.DismissSupplierSuggestions();
                e.Handled = true;
                break;
        }
    }

    private void DirectSupplierList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.ConfirmSupplierSelection();
    }

    private void PurchaseReturnView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is null) return;

        switch (e.Key)
        {
            case Key.F3:
                if (MainTabs.SelectedIndex == 0)
                {
                    SearchBox.Focus();
                    SearchBox.SelectAll();
                }
                else if (MainTabs.SelectedIndex == 1)
                {
                    DirectSupplierBox.Focus();
                    DirectSupplierBox.SelectAll();
                }
                e.Handled = true;
                break;
            case Key.Enter when SearchBox.IsKeyboardFocusWithin:
                if (ViewModel.SearchCommand.CanExecute(null))
                    ViewModel.SearchCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.F9:
                if (MainTabs.SelectedIndex == 1)
                {
                    if (ViewModel.ProcessDirectReturnCommand.CanExecute(null))
                        ViewModel.ProcessDirectReturnCommand.Execute(null);
                }
                else if (ViewModel.ProcessReturnCommand.CanExecute(null))
                {
                    ViewModel.ProcessReturnCommand.Execute(null);
                }
                e.Handled = true;
                break;
        }
    }
}
