using System.Windows;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.Views;

public partial class BillSharePromptWindow : Window
{
    public BillSharePromptWindow(
        string invoiceNumber,
        string displayPhone,
        bool enableWhatsApp,
        bool enableSms)
    {
        InitializeComponent();
        Title = "Send bill to customer";
        InvoiceText.Text = $"Invoice {invoiceNumber}";
        PhoneText.Text = $"Mobile: {displayPhone}";
        HintText.Text = "WhatsApp: uploads the printable PDF to your VPS (if configured) and opens chat with bill text + download link. SMS can include the same link.";

        WhatsAppButton.Visibility = enableWhatsApp ? Visibility.Visible : Visibility.Collapsed;
        SmsButton.Visibility = enableSms ? Visibility.Visible : Visibility.Collapsed;
        BothButton.Visibility = enableWhatsApp && enableSms ? Visibility.Visible : Visibility.Collapsed;
    }

    public BillShareChannel SelectedChannel { get; private set; } = BillShareChannel.None;

    private void WhatsApp_Click(object sender, RoutedEventArgs e)
    {
        SelectedChannel = BillShareChannel.WhatsApp;
        DialogResult = true;
    }

    private void Sms_Click(object sender, RoutedEventArgs e)
    {
        SelectedChannel = BillShareChannel.Sms;
        DialogResult = true;
    }

    private void Both_Click(object sender, RoutedEventArgs e)
    {
        SelectedChannel = BillShareChannel.Both;
        DialogResult = true;
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        SelectedChannel = BillShareChannel.None;
        DialogResult = false;
    }
}
