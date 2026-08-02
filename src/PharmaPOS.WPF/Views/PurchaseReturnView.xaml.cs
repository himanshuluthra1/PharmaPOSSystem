using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        if (ViewModel?.LoadPurchaseCommand.CanExecute(null) == true)
            ViewModel.LoadPurchaseCommand.Execute(null);
    }

    private void ReturnRecordsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel?.HasSelectedReturn != true) return;
        ReceiptNumberBox.Focus();
        ReceiptNumberBox.SelectAll();
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
