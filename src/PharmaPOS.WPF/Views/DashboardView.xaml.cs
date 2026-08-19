using System.Windows;
using System.Windows.Controls;

namespace PharmaPOS.WPF.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        var kpiCols = width < 560 ? 2 : 4;
        PrimaryKpiGrid.Columns = kpiCols;
        AlertKpiGrid.Columns = kpiCols;
        ListsGrid.Columns = width < 560 ? 1 : 2;
    }
}
