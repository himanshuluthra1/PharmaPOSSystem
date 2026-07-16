using System.Windows;
using System.Windows.Input;
using PharmaPOS.WPF.ViewModels.Sales;

namespace PharmaPOS.WPF.Views;

public partial class SaleReturnDialogWindow : Window
{
    private readonly SaleReturnViewModel _viewModel;

    public SaleReturnDialogWindow(SaleReturnViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.ReturnCompleted += OnReturnCompleted;
        Closed += (_, _) => viewModel.ReturnCompleted -= OnReturnCompleted;
    }

    private void OnReturnCompleted()
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F9 && _viewModel.ProcessReturnCommand.CanExecute(null))
        {
            _viewModel.ProcessReturnCommand.Execute(null);
            e.Handled = true;
        }
    }
}
