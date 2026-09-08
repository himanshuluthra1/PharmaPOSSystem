using System.Globalization;
using System.Windows;

namespace PharmaPOS.WPF.Views;

public partial class ShortagePromptWindow : Window
{
    private readonly decimal _defaultQuantity;

    public ShortagePromptWindow(
        string medicineName,
        decimal defaultQuantity,
        string? defaultCustomerName = null,
        string? detailLine = null)
    {
        InitializeComponent();
        _defaultQuantity = defaultQuantity > 0 ? defaultQuantity : 1m;

        HeadlineText.Text = $"Record shortage for \"{medicineName}\"?";
        DetailText.Text = string.IsNullOrWhiteSpace(detailLine)
            ? "Wanted quantity and customer name are optional."
            : detailLine;

        QuantityBox.Text = _defaultQuantity.ToString("0.##", CultureInfo.CurrentCulture);
        CustomerBox.Text = defaultCustomerName?.Trim() ?? string.Empty;

        Loaded += (_, _) =>
        {
            QuantityBox.Focus();
            QuantityBox.SelectAll();
        };
    }

    public decimal WantedQuantity { get; private set; }
    public string? CustomerName { get; private set; }

    private void Record_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        var qtyText = QuantityBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(qtyText))
        {
            WantedQuantity = _defaultQuantity;
        }
        else if (!decimal.TryParse(qtyText, NumberStyles.Number, CultureInfo.CurrentCulture, out var qty)
                 && !decimal.TryParse(qtyText, NumberStyles.Number, CultureInfo.InvariantCulture, out qty))
        {
            ShowError("Enter a valid wanted quantity, or leave it blank.");
            QuantityBox.Focus();
            return;
        }
        else if (qty <= 0)
        {
            ShowError("Wanted quantity must be greater than zero.");
            QuantityBox.Focus();
            return;
        }
        else
        {
            WantedQuantity = qty;
        }

        var name = CustomerBox.Text?.Trim();
        CustomerName = string.IsNullOrWhiteSpace(name) ? null : name;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
