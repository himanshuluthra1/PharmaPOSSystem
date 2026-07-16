using System.Windows;
using System.Windows.Input;
using PharmaPOS.WPF.ViewModels;

namespace PharmaPOS.WPF.Views;

/// <summary>The application shell hosting the navigation rail and module content.</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.NavigateToSales();
                e.Handled = true;
            }
        }
    }
}
