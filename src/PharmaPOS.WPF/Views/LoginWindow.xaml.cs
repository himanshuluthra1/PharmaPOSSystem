using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PharmaPOS.WPF.ViewModels;

namespace PharmaPOS.WPF.Views;

/// <summary>
/// Login window. The password is read directly from the (non-bindable)
/// <see cref="PasswordBox"/> and forwarded to the view model on submit.
/// </summary>
public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => UsernameBox.Focus();
    }

    private LoginViewModel? ViewModel => DataContext as LoginViewModel;

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
            await ViewModel.ExecuteLoginAsync(PasswordBox.Password);
    }

    private async void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ViewModel is not null)
            await ViewModel.ExecuteLoginAsync(PasswordBox.Password);
    }

    private void ForgotPassword_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show(
            "Please contact your system administrator to reset your password.",
            "Forgot Password", MessageBoxButton.OK, MessageBoxImage.Information);
}
