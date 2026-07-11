using System.Windows;
using PharmaPOS.WPF.ViewModels.Settings;

namespace PharmaPOS.WPF.Views;

public partial class PendingMappingsWindow : Window
{
    public PendingMappingsWindow(MedicineMappingTabViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
