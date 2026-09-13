using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PharmaPOS.Application.Features.SaleReturns;
using PharmaPOS.WPF.Services;
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
        if (Keyboard.Modifiers == ModifierKeys.Control
            && sender is DataGrid { SelectedItem: SaleReturnSearchResultDto row })
        {
            e.Handled = true;
            _ = InvoiceDrillDown.OpenSaleAsync(row.SaleId);
            return;
        }

        if (ViewModel?.LoadInvoiceCommand.CanExecute(null) == true)
            ViewModel.LoadInvoiceCommand.Execute(null);
    }

    private void SearchGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not DataGrid { SelectedItem: SaleReturnSearchResultDto row })
            return;

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            _ = InvoiceDrillDown.OpenSaleAsync(row.SaleId);
            return;
        }

        // Enter loads the invoice into the return workflow (same as double-click).
        if (ViewModel?.LoadInvoiceCommand.CanExecute(null) == true)
        {
            e.Handled = true;
            ViewModel.LoadInvoiceCommand.Execute(null);
        }
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
