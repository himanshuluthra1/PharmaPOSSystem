using System.Windows;

namespace PharmaPOS.WPF.Views;

public partial class AppliedMappingsWindow : Window
{
    public AppliedMappingsWindow()
    {
        InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
