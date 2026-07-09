using System.Windows;

namespace PharmaPOS.WPF.Services;

public interface IDialogService
{
    void ShowInfo(string message, string title = "Information");
    void ShowError(string message, string title = "Error");
    bool Confirm(string message, string title = "Confirm");
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
}
