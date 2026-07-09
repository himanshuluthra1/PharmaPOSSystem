using System.Windows.Input;
using PharmaPOS.Application.Features.Authentication;
using PharmaPOS.WPF.Mvvm;

namespace PharmaPOS.WPF.ViewModels;

/// <summary>Backs the login window: validates credentials and signals success.</summary>
public class LoginViewModel : ObservableObject
{
    private readonly IAuthService _authService;

    private string _username = "admin";
    private string _errorMessage = string.Empty;
    private bool _isBusy;
    private bool _rememberMe;

    public LoginViewModel(IAuthService authService)
    {
        _authService = authService;
        LoginCommand = new AsyncRelayCommand(LoginAsync, _ => !IsBusy);
    }

    /// <summary>Raised when authentication succeeds; the host closes the window.</summary>
    public event Action? LoginSucceeded;

    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    public bool RememberMe
    {
        get => _rememberMe;
        set => SetProperty(ref _rememberMe, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public ICommand LoginCommand { get; }

    /// <summary>Executes the login using the password from the secured PasswordBox.</summary>
    public async Task ExecuteLoginAsync(string password)
    {
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            var result = await _authService.LoginAsync(new LoginRequest(Username, password, RememberMe));
            if (result.IsSuccess)
                LoginSucceeded?.Invoke();
            else
                ErrorMessage = result.Error ?? "Login failed.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unable to sign in: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task LoginAsync(object? parameter)
    {
        // The password arrives as a parameter from the view (PasswordBox is not bindable).
        var password = parameter as string ?? string.Empty;
        return ExecuteLoginAsync(password);
    }
}
