using System.Windows;
using System.Windows.Input;
using PharmaPOS.WPF.ViewModels.Sales;

namespace PharmaPOS.WPF.Views;

public partial class LastSaleRefillWindow : Window
{
    public LastSaleRefillWindow(LastSaleRefillViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += () =>
        {
            DialogResult = viewModel.Confirmed;
            Close();
        };
        Loaded += (_, _) =>
        {
            SearchBox.Focus();
            SearchBox.CaretIndex = SearchBox.Text.Length;
            if (!string.IsNullOrWhiteSpace(viewModel.SearchText)
                && viewModel.SearchCommand.CanExecute(null))
                viewModel.SearchCommand.Execute(null);
        };
    }

    public LastSaleRefillViewModel ViewModel => (LastSaleRefillViewModel)DataContext;

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
        }
    }

    private void MatchList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Selection change already loads refill details.
    }
}
