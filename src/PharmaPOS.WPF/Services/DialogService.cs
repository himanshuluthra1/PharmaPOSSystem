using System.Windows;
using PharmaPOS.WPF.Views;

namespace PharmaPOS.WPF.Services;

public sealed record ShortagePromptResult(decimal WantedQuantity, string? CustomerName);

public interface IDialogService
{
    void ShowInfo(string message, string title = "Information");
    void ShowError(string message, string title = "Error");
    bool Confirm(string message, string title = "Confirm");

    /// <summary>
    /// Asks for optional wanted quantity and customer name before recording a shortage.
    /// Returns null if the user cancels.
    /// </summary>
    ShortagePromptResult? PromptShortageDetails(
        string medicineName,
        decimal defaultWantedQuantity,
        string? defaultCustomerName = null,
        string? detailLine = null);
}

/// <summary>Simple dialog wrapper. Can later be swapped for Material dialog hosts.</summary>
public class DialogService : IDialogService
{
    public void ShowInfo(string message, string title = "Information")
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowError(string message, string title = "Error")
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public bool Confirm(string message, string title = "Confirm")
        => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public ShortagePromptResult? PromptShortageDetails(
        string medicineName,
        decimal defaultWantedQuantity,
        string? defaultCustomerName = null,
        string? detailLine = null)
    {
        var window = new ShortagePromptWindow(
            medicineName,
            defaultWantedQuantity,
            defaultCustomerName,
            detailLine)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        return window.ShowDialog() == true
            ? new ShortagePromptResult(window.WantedQuantity, window.CustomerName)
            : null;
    }
}
