using System.Windows;
using System.Windows.Input;
using PharmaPOS.Application.Features.Sales;

namespace PharmaPOS.WPF.Views;

public partial class MedicineDetailPopupWindow : Window
{
    public MedicineDetailPopupWindow(SaleMedicineDetailDto detail)
    {
        InitializeComponent();
        DataContext = detail;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.F4)
        {
            e.Handled = true;
            Close();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
