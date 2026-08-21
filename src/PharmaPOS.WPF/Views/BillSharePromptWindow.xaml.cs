using System.Windows;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.Views;

public partial class BillSharePromptWindow : Window
{
    public BillSharePromptWindow(
        string invoiceNumber,
        string? customerPhone,
        bool enableWhatsApp,
        bool enableSms)
        : this(
            title: "Send bill to customer",
            headline: $"Invoice {invoiceNumber}",
            hint: "WhatsApp / SMS the bill. Enter the customer mobile if it is blank.",
            customerPhone: customerPhone,
            enableWhatsApp: enableWhatsApp,
            enableSms: enableSms)
    {
    }

    public BillSharePromptWindow(
        string title,
        string headline,
        string hint,
        string? customerPhone,
        bool enableWhatsApp,
        bool enableSms)
    {
        InitializeComponent();
        Title = title;
        InvoiceText.Text = headline;
        HintText.Text = hint;
        PhoneBox.Text = customerPhone ?? string.Empty;

        WhatsAppButton.Visibility = enableWhatsApp ? Visibility.Visible : Visibility.Collapsed;
        SmsButton.Visibility = enableSms ? Visibility.Visible : Visibility.Collapsed;
        BothButton.Visibility = enableWhatsApp && enableSms ? Visibility.Visible : Visibility.Collapsed;

        Loaded += (_, _) =>
        {
            PhoneBox.Focus();
            PhoneBox.CaretIndex = PhoneBox.Text.Length;
        };
    }

    public BillShareChannel SelectedChannel { get; private set; } = BillShareChannel.None;

    public string EnteredPhone => PhoneBox.Text ?? string.Empty;

    private void WhatsApp_Click(object sender, RoutedEventArgs e) => Accept(BillShareChannel.WhatsApp);

    private void Sms_Click(object sender, RoutedEventArgs e) => Accept(BillShareChannel.Sms);

    private void Both_Click(object sender, RoutedEventArgs e) => Accept(BillShareChannel.Both);

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        SelectedChannel = BillShareChannel.None;
        DialogResult = false;
    }

    private void Accept(BillShareChannel channel)
    {
        if (string.IsNullOrWhiteSpace(BillShareService.NormalizePhone(EnteredPhone)))
        {
            MessageBox.Show(
                "Enter a valid 10-digit mobile number.",
                "Mobile required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            PhoneBox.Focus();
            return;
        }

        SelectedChannel = channel;
        DialogResult = true;
    }
}
