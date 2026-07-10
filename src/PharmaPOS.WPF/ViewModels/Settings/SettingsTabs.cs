using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Authentication;
using PharmaPOS.Application.Features.Settings;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Shared.Constants;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Settings;

public class UsersTabViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialog;
    private string _searchText = string.Empty;
    private UserListDto? _selected;
    private UserDetailDto _editor = new();
    private string _newPassword = string.Empty;
    private string _resetPassword = string.Empty;
    private bool _isBusy;
    private bool _loaded;
    private string? _statusMessage;

    public UsersTabViewModel(
        ISettingsService settings,
        ICurrentUserService currentUser,
        IDialogService dialog)
    {
        _settings = settings;
        _dialog = dialog;
        CanManageUsers = currentUser.HasAnyPermission(
            AppConstants.Permissions.UsersEdit, AppConstants.Permissions.UsersManage);

        NewCommand = new RelayCommand(_ => BeginNew(), _ => CanManageUsers);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy && CanManageUsers);
        ResetPasswordCommand = new AsyncRelayCommand(ResetPasswordAsync, () => !IsBusy && CanManageUsers && Editor.Id > 0);
        RefreshCommand = new AsyncRelayCommand(SearchAsync);
    }

    public bool CanManageUsers { get; }

    public ObservableCollection<UserListDto> Items { get; } = new();
    public ObservableCollection<RoleListDto> Roles { get; } = new();
    public ObservableCollection<BranchListDto> Branches { get; } = new();
    public Array EntityStatuses => Enum.GetValues(typeof(EntityStatus));

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                _ = SearchAsync();
        }
    }

    public UserListDto? SelectedItem
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value) && value is not null)
                _ = LoadItemAsync(value.Id);
        }
    }

    public UserDetailDto Editor
    {
        get => _editor;
        private set
        {
            if (SetProperty(ref _editor, value))
            {
                OnPropertyChanged(nameof(EditorTitle));
                OnPropertyChanged(nameof(IsNewRecord));
            }
        }
    }

    public string NewPassword
    {
        get => _newPassword;
        set => SetProperty(ref _newPassword, value);
    }

    public string ResetPassword
    {
        get => _resetPassword;
        set => SetProperty(ref _resetPassword, value);
    }

    public string EditorTitle => Editor.Id > 0 ? $"Edit: {Editor.FullName}" : "New User";
    public bool IsNewRecord => Editor.Id == 0;

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public ICommand NewCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ResetPasswordCommand { get; }
    public ICommand RefreshCommand { get; }

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        _loaded = true;
        await LoadLookupsAsync();
        await SearchAsync();
    }

    private async Task LoadLookupsAsync()
    {
        var roles = await _settings.ListRolesAsync();
        Roles.Clear();
        foreach (var r in roles) Roles.Add(r);

        var branches = await _settings.ListBranchesAsync();
        Branches.Clear();
        foreach (var b in branches) Branches.Add(b);
    }

    private async Task SearchAsync()
    {
        IsBusy = true;
        try
        {
            var rows = await _settings.ListUsersAsync(SearchText);
            Items.Clear();
            foreach (var r in rows) Items.Add(r);
        }
        finally { IsBusy = false; }
    }

    private async Task LoadItemAsync(int id)
    {
        var detail = await _settings.GetUserAsync(id);
        if (detail is not null)
        {
            Editor = detail;
            NewPassword = string.Empty;
            ResetPassword = string.Empty;
        }
    }

    private void BeginNew()
    {
        SelectedItem = null;
        Editor = new UserDetailDto();
        NewPassword = string.Empty;
        ResetPassword = string.Empty;
    }

    private async Task SaveAsync()
    {
        IsBusy = true;
        try
        {
            var password = IsNewRecord ? NewPassword : (string.IsNullOrWhiteSpace(NewPassword) ? null : NewPassword);
            var result = await _settings.SaveUserAsync(Editor, password);
            if (result.IsFailure)
            {
                _dialog.ShowError(result.Error ?? "Could not save user.");
                return;
            }
            Editor.Id = result.Value;
            NewPassword = string.Empty;
            StatusMessage = "User saved.";
            await SearchAsync();
        }
        finally { IsBusy = false; }
    }

    private async Task ResetPasswordAsync()
    {
        if (string.IsNullOrWhiteSpace(ResetPassword))
        {
            _dialog.ShowError("Enter a new password to reset.");
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _settings.ResetUserPasswordAsync(Editor.Id, ResetPassword);
            if (result.IsFailure)
            {
                _dialog.ShowError(result.Error ?? "Could not reset password.");
                return;
            }
            ResetPassword = string.Empty;
            StatusMessage = "Password reset. User must change password on next login.";
        }
        finally { IsBusy = false; }
    }
}

public class ChangePasswordTabViewModel : ObservableObject
{
    private readonly IAuthService _auth;
    private readonly ICurrentUserService _currentUser;
    private readonly IDialogService _dialog;
    private string _currentPassword = string.Empty;
    private string _newPassword = string.Empty;
    private string _confirmPassword = string.Empty;
    private bool _isBusy;
    private string? _statusMessage;

    public ChangePasswordTabViewModel(
        IAuthService auth,
        ICurrentUserService currentUser,
        IDialogService dialog)
    {
        _auth = auth;
        _currentUser = currentUser;
        _dialog = dialog;
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    public string CurrentPassword
    {
        get => _currentPassword;
        set => SetProperty(ref _currentPassword, value);
    }

    public string NewPassword
    {
        get => _newPassword;
        set => SetProperty(ref _newPassword, value);
    }

    public string ConfirmPassword
    {
        get => _confirmPassword;
        set => SetProperty(ref _confirmPassword, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string AccountName => _currentUser.CurrentUser?.FullName ?? "Current user";

    public ICommand SaveCommand { get; }

    private async Task SaveAsync()
    {
        if (NewPassword != ConfirmPassword)
        {
            _dialog.ShowError("New password and confirmation do not match.");
            return;
        }

        var userId = _currentUser.CurrentUser?.UserId;
        if (userId is null)
        {
            _dialog.ShowError("Not signed in.");
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _auth.ChangePasswordAsync(new ChangePasswordRequest(
                userId.Value, CurrentPassword, NewPassword));

            if (result.IsFailure)
            {
                _dialog.ShowError(result.Error ?? "Could not change password.");
                return;
            }

            CurrentPassword = string.Empty;
            NewPassword = string.Empty;
            ConfirmPassword = string.Empty;
            StatusMessage = "Password changed successfully.";
        }
        finally { IsBusy = false; }
    }
}

public class AppearanceTabViewModel : ObservableObject
{
    private readonly IThemeService _theme;
    private bool _isDarkMode;

    public AppearanceTabViewModel(IThemeService theme)
    {
        _theme = theme;
        _isDarkMode = theme.IsDarkMode;
    }

    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            if (SetProperty(ref _isDarkMode, value))
                _theme.SetDarkMode(value);
        }
    }
}
