using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PharmaPOS.WPF.ViewModels.Purchases;

namespace PharmaPOS.WPF.Views;

public partial class PurchaseOrderView : UserControl
{
    public PurchaseOrderView()
    {
        InitializeComponent();
    }

    private PurchaseOrderViewModel? ViewModel => DataContext as PurchaseOrderViewModel;

    private void SupplierBox_PreviewKeyDown(object sender, KeyEventArgs e)
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

    private void SupplierBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // Delay dismiss so ListBox click can select first.
        Dispatcher.BeginInvoke(() =>
        {
            if (ViewModel is not null && !SupplierBox.IsKeyboardFocusWithin)
                ViewModel.DismissSupplierSuggestions();
        });
    }

    private void SupplierList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.ConfirmSupplierSelection();
    }
}
