using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace PharmaPOS.WPF.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void MedWinImportLog_TargetUpdated(object sender, DataTransferEventArgs e)
    {
        MedWinImportLogScroll?.Dispatcher.BeginInvoke(
            () => MedWinImportLogScroll.ScrollToEnd(),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }
}
