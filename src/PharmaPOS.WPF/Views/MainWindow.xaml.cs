using System.Windows;
using System.Windows.Input;
using PharmaPOS.WPF.Services;
using PharmaPOS.WPF.ViewModels;

namespace PharmaPOS.WPF.Views;

/// <summary>The application shell hosting the navigation rail and module content.</summary>
public partial class MainWindow : Window
{
    private readonly IMedicineLedgerDialogService _medicineLedger;

    public MainWindow(IMedicineLedgerDialogService medicineLedger)
    {
        _medicineLedger = medicineLedger;
        InitializeComponent();
    }

    private async void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.NavigateToSales();
                e.Handled = true;
            }
            return;
        }

        // Ctrl+L → medicine stock ledger for the focused/selected medicine row (any module grid).
        if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (await _medicineLedger.TryShowForFocusedMedicineAsync())
                e.Handled = true;
        }
    }
}
