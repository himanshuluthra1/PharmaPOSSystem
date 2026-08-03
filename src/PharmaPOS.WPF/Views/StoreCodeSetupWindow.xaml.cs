using System.Windows;
using PharmaPOS.Application.Features.ReportingSync;

namespace PharmaPOS.WPF.Views;

public partial class StoreCodeSetupWindow : Window
{
    private readonly IStoreIdentityService _storeIdentity;

    public StoreCodeSetupWindow(IStoreIdentityService storeIdentity)
    {
        InitializeComponent();
        _storeIdentity = storeIdentity;
        MachineIdBox.Text = _storeIdentity.MachineId;
        StatusText.Text = "Enter your store code, then click Activate. A request row is created on the server only after Activate.";
        StoreCodeBox.Focus();
    }

    private async void Activate_Click(object sender, RoutedEventArgs e)
    {
        ActivateButton.IsEnabled = false;
        StatusText.Foreground = System.Windows.Media.Brushes.Gray;
        StatusText.Text = "Contacting activation server…";
        try
        {
            var result = await _storeIdentity.ActivateAsync(StoreCodeBox.Text);
            if (result.Success)
            {
                MessageBox.Show(result.Message, "Store activated", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
                return;
            }

            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xC6, 0x28, 0x28));
            StatusText.Text = result.Message;
            MessageBox.Show(result.Message, "Activation", MessageBoxButton.OK,
                result.Message.Contains("saved on the server", StringComparison.OrdinalIgnoreCase)
                    ? MessageBoxImage.Information
                    : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            MessageBox.Show(ex.Message, "Activation failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ActivateButton.IsEnabled = true;
        }
    }

    private void CopyMachineId_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_storeIdentity.MachineId);
            StatusText.Foreground = System.Windows.Media.Brushes.Gray;
            StatusText.Text = "Machine ID copied. Send it to your software provider for approval.";
        }
        catch
        {
            StatusText.Text = "Could not copy to clipboard.";
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
    }
}
