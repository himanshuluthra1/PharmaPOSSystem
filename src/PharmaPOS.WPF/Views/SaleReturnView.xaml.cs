using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PharmaPOS.WPF.ViewModels.Sales;

namespace PharmaPOS.WPF.Views;

public partial class SaleReturnView : UserControl
{
    public SaleReturnView()
    {
        InitializeComponent();
    }

    private SaleReturnViewModel? ViewModel => DataContext as SaleReturnViewModel;

    private void SearchGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel?.LoadInvoiceCommand.CanExecute(null) == true)
            ViewModel.LoadInvoiceCommand.Execute(null);
    }

    private void SaleReturnView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is null) return;

        switch (e.Key)
        {
            case Key.F3:
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
                break;
            case Key.Enter when SearchBox.IsKeyboardFocusWithin:
                if (ViewModel.LoadInvoiceCommand.CanExecute(null))
                    ViewModel.LoadInvoiceCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.F9:
                if (ViewModel.ProcessReturnCommand.CanExecute(null))
                    ViewModel.ProcessReturnCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}
