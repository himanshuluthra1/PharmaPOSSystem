using System.Windows;
using System.Windows.Controls;

namespace PharmaPOS.WPF.Views;

public partial class InventoryView : UserControl
{
    public InventoryView()
    {
        InitializeComponent();
    }

    private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        StockKpiGrid.Columns = e.NewSize.Width < 560 ? 2 : 4;
    }
}
