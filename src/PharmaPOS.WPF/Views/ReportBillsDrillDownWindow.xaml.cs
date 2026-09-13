using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PharmaPOS.Application.Features.Reports;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.Views;

public partial class ReportBillsDrillDownWindow : Window
{
    private readonly Func<ReportBillListRowDto, Task> _openBillAsync;

    public ReportBillsDrillDownWindow(
        string title,
        string subtitle,
        IReadOnlyList<ReportBillListRowDto> bills,
        Func<ReportBillListRowDto, Task> openBillAsync)
    {
        InitializeComponent();
        _openBillAsync = openBillAsync;

        Title = title;
        TitleText.Text = title;
        SubtitleText.Text = subtitle;

        BillsGrid.ItemsSource = new ObservableCollection<ReportBillListRowDto>(bills);
        if (bills.Count > 0)
            BillsGrid.SelectedIndex = 0;
    }

    private void BillsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => InvoiceDrillDown.HandleDoubleClick(e, sender, OpenSelectedAsync);

    private void BillsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not DataGrid { SelectedItem: ReportBillListRowDto row }) return;
        InvoiceDrillDown.TryHandleKey(e, row, OpenSelectedAsync);
    }

    private Task OpenSelectedAsync(object item)
    {
        if (item is not ReportBillListRowDto row) return Task.CompletedTask;
        return _openBillAsync(row);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
