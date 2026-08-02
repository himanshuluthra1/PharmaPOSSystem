using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using PharmaPOS.Application.Features.Inventory;

namespace PharmaPOS.WPF.Views;

public partial class MedicineLedgerPopupWindow : Window
{
    public MedicineLedgerPopupWindow(string medicineName, IReadOnlyList<StockLedgerRowDto> rows)
    {
        InitializeComponent();
        TitleText.Text = $"Medicine ledger — {medicineName}";
        Title = $"Medicine ledger — {medicineName}";
        SubtitleText.Text = rows.Count == 0
            ? "No stock movements found for this medicine at the current branch."
            : $"{rows.Count} movement(s). Newest first.";
        LedgerGrid.ItemsSource = new ObservableCollection<StockLedgerRowDto>(rows);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape
            || (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control))
        {
            e.Handled = true;
            Close();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
