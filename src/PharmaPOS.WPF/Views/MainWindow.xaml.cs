using System.ComponentModel;
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
        Closing += MainWindow_Closing;
    }

    /// <summary>
    /// Tags that skip the exit confirmation (logout re-opens login; force-close is for updates).
    /// </summary>
    public static bool IsSilentCloseTag(object? tag)
        => tag is string s && (s is "logout" or "force-close");

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (IsSilentCloseTag(Tag))
            return;

        var result = MessageBox.Show(
            this,
            "Are you sure you want to close PharmaPOS?\n\nUnsaved bills or forms will be lost.",
            "Exit PharmaPOS",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes)
            e.Cancel = true;
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
