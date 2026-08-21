using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PharmaPOS.Application.Features.Reports;
using PharmaPOS.WPF.ViewModels.Reports;

namespace PharmaPOS.WPF.Views;

public partial class ReportsView : UserControl
{
    public ReportsView()
    {
        InitializeComponent();
    }

    private ReportsViewModel? ViewModel => DataContext as ReportsViewModel;

    private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        GenericKpiGrid.Columns = width < 560 ? 2 : 4;
        var threeCols = width < 560 ? 2 : 3;
        StockKpiGrid.Columns = threeCols;
        GstKpiGrid.Columns = threeCols;
    }

    private void SalesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: SalesReportRowDto row })
            ViewModel?.OpenSaleRowCommand.Execute(row);
    }

    private void PurchaseGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: PurchaseReportRowDto row })
            ViewModel?.OpenPurchaseRowCommand.Execute(row);
    }

    private void SalesGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (sender is DataGrid { SelectedItem: SalesReportRowDto row })
        {
            ViewModel?.OpenSaleRowCommand.Execute(row);
            e.Handled = true;
        }
    }

    private void PurchaseGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (sender is DataGrid { SelectedItem: PurchaseReportRowDto row })
        {
            ViewModel?.OpenPurchaseRowCommand.Execute(row);
            e.Handled = true;
        }
    }

    private void ScheduleGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: ScheduleRegisterRowDto row })
            ViewModel?.OpenScheduleRowCommand.Execute(row);
    }

    private void ScheduleGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (sender is DataGrid { SelectedItem: ScheduleRegisterRowDto row })
        {
            ViewModel?.OpenScheduleRowCommand.Execute(row);
            e.Handled = true;
        }
    }
}
