using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Authentication;
using PharmaPOS.Application.Features.Settings;
using PharmaPOS.Shared.Constants;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Settings;

/// <summary>Shell view model for the Settings module.</summary>
public class SettingsViewModel : ObservableObject
{
    private int _selectedTab;

    public SettingsViewModel(
        ISettingsService settings,
        IAuthService auth,
        ICurrentUserService currentUser,
        IThemeService theme,
        IDialogService dialog)
    {
        var user = currentUser;

        CanManageCompany = user.HasAnyPermission(
            AppConstants.Permissions.SettingsCompany, AppConstants.Permissions.SettingsManage);
        CanManageBranches = user.HasAnyPermission(
            AppConstants.Permissions.SettingsBranches, AppConstants.Permissions.SettingsManage);
        CanManagePreferences = user.HasAnyPermission(
            AppConstants.Permissions.SettingsPreferences, AppConstants.Permissions.SettingsManage);
        CanManageUsers = user.HasAnyPermission(
            AppConstants.Permissions.UsersEdit, AppConstants.Permissions.UsersManage);
        CanManageRoles = user.HasAnyPermission(
            AppConstants.Permissions.UsersRoles, AppConstants.Permissions.UsersManage);
        CanAccessSettings = user.CanAccessModule("settings") || user.CanAccessModule("users");

        Company = new CompanyTabViewModel(settings, dialog);
        Branches = new BranchesTabViewModel(settings, dialog);
        Preferences = new PreferencesTabViewModel(settings, dialog);
        RolePermissions = new RolePermissionsTabViewModel(settings, currentUser, dialog);
        Users = new UsersTabViewModel(settings, currentUser, dialog);
        ChangePassword = new ChangePasswordTabViewModel(auth, currentUser, dialog);
        Appearance = new AppearanceTabViewModel(theme);

        _selectedTab = CanManageCompany ? 0
            : CanManageBranches ? 1
            : CanManagePreferences ? 2
            : CanManageRoles ? 3
            : CanManageUsers ? 4
            : 5;

        if (CanManageCompany)
            _ = Company.EnsureLoadedAsync();
        else if (CanManageRoles)
            _ = RolePermissions.EnsureLoadedAsync();
        else if (CanManageUsers)
            _ = Users.EnsureLoadedAsync();
    }

    public bool CanAccessSettings { get; }
    public bool CanManageCompany { get; }
    public bool CanManageBranches { get; }
    public bool CanManagePreferences { get; }
    public bool CanManageRoles { get; }
    public bool CanManageUsers { get; }

    public CompanyTabViewModel Company { get; }
    public BranchesTabViewModel Branches { get; }
    public PreferencesTabViewModel Preferences { get; }
    public RolePermissionsTabViewModel RolePermissions { get; }
    public UsersTabViewModel Users { get; }
    public ChangePasswordTabViewModel ChangePassword { get; }
    public AppearanceTabViewModel Appearance { get; }

    public int SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (!SetProperty(ref _selectedTab, value)) return;
            _ = LoadSelectedTabAsync();
        }
    }

    private async Task LoadSelectedTabAsync()
    {
        switch (SelectedTab)
        {
            case 0 when CanManageCompany: await Company.EnsureLoadedAsync(); break;
            case 1 when CanManageBranches: await Branches.EnsureLoadedAsync(); break;
            case 2 when CanManagePreferences: await Preferences.EnsureLoadedAsync(); break;
            case 3 when CanManageRoles: await RolePermissions.EnsureLoadedAsync(); break;
            case 4 when CanManageUsers: await Users.EnsureLoadedAsync(); break;
        }
    }
}
