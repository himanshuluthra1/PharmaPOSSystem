using System.Collections.ObjectModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Authentication;
using PharmaPOS.Application.Features.ReportingSync;
using PharmaPOS.Application.Features.Settings;
using PharmaPOS.Shared.Constants;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Settings;

/// <summary>One row in the Settings side navigation (maps to a TabControl index).</summary>
public sealed class SettingsSection
{
    public SettingsSection(string title, int tabIndex)
    {
        Title = title;
        TabIndex = tabIndex;
    }

    public string Title { get; }
    public int TabIndex { get; }
}

/// <summary>Shell view model for the Settings module.</summary>
public class SettingsViewModel : ObservableObject
{
    private int _selectedTab;
    private SettingsSection? _selectedSection;

    public SettingsViewModel(
        ISettingsService settings,
        IServiceScopeFactory scopeFactory,
        IAuthService auth,
        ICurrentUserService currentUser,
        IThemeService theme,
        IUiLayoutService layout,
        IAiBillSettingsService aiSettings,
        IGeminiMedicineMappingMatcher geminiMedicineMatcher,
        IBillShareSettingsService billShareSettings,
        IMySqlSyncSettingsService mySqlSyncSettings,
        IMySqlReportingPublisher mySqlPublisher,
        IStoreIdentityService storeIdentity,
        IConfiguration configuration,
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
        // Medicine mapping + MedWin import: full settings access, OR any settings.* grant
        // (so the tabs are not missing when the role has company/preferences but not settings.manage).
        var canAccessSettingsModule = user.CanAccessModule("settings");
        CanManageMedicineMapping = user.HasAnyPermission(AppConstants.Permissions.SettingsManage)
            || canAccessSettingsModule;
        CanManageMedWinImport = user.HasAnyPermission(AppConstants.Permissions.SettingsManage)
            || canAccessSettingsModule;
        CanAccessSettings = canAccessSettingsModule || user.CanAccessModule("users");

        Company = new CompanyTabViewModel(settings, dialog);
        Branches = new BranchesTabViewModel(settings, dialog);
        Preferences = new PreferencesTabViewModel(
            settings, layout, aiSettings, billShareSettings, mySqlSyncSettings, mySqlPublisher, storeIdentity, dialog);
        MedicineMapping = new MedicineMappingTabViewModel(scopeFactory, dialog, aiSettings, geminiMedicineMatcher);
        MedWinImport = new MedWinImportTabViewModel(configuration, dialog);
        RolePermissions = new RolePermissionsTabViewModel(settings, currentUser, dialog);
        Users = new UsersTabViewModel(settings, currentUser, dialog);
        ChangePassword = new ChangePasswordTabViewModel(auth, currentUser, dialog);
        Appearance = new AppearanceTabViewModel(theme);

        // Side nav lists only allowed sections — avoids TabControl header overflow / missing tabs.
        if (CanManageCompany) Sections.Add(new SettingsSection("Company", 0));
        if (CanManageBranches) Sections.Add(new SettingsSection("Branches", 1));
        if (CanManagePreferences) Sections.Add(new SettingsSection("Preferences", 2));
        if (CanManageMedicineMapping) Sections.Add(new SettingsSection("Medicine Mapping", 3));
        if (CanManageMedWinImport) Sections.Add(new SettingsSection("MedWin Import", 4));
        if (CanManageRoles) Sections.Add(new SettingsSection("Roles & Permissions", 5));
        if (CanManageUsers) Sections.Add(new SettingsSection("Users", 6));
        Sections.Add(new SettingsSection("My Password", 7));
        Sections.Add(new SettingsSection("Appearance", 8));

        _selectedSection = Sections[0];
        _selectedTab = _selectedSection.TabIndex;

        if (CanManageCompany)
            _ = Company.EnsureLoadedAsync();
        else if (CanManageRoles)
            _ = RolePermissions.EnsureLoadedAsync();
        else if (CanManageUsers)
            _ = Users.EnsureLoadedAsync();
    }

    public ObservableCollection<SettingsSection> Sections { get; } = new();

    public SettingsSection? SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (!SetProperty(ref _selectedSection, value) || value is null) return;
            SelectedTab = value.TabIndex;
        }
    }

    public bool CanAccessSettings { get; }
    public bool CanManageCompany { get; }
    public bool CanManageBranches { get; }
    public bool CanManagePreferences { get; }
    public bool CanManageRoles { get; }
    public bool CanManageUsers { get; }
    public bool CanManageMedicineMapping { get; }
    public bool CanManageMedWinImport { get; }

    public CompanyTabViewModel Company { get; }
    public BranchesTabViewModel Branches { get; }
    public PreferencesTabViewModel Preferences { get; }
    public MedicineMappingTabViewModel MedicineMapping { get; }
    public MedWinImportTabViewModel MedWinImport { get; }
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
            case 3 when CanManageMedicineMapping: await MedicineMapping.EnsureLoadedAsync(); break;
            case 5 when CanManageRoles: await RolePermissions.EnsureLoadedAsync(); break;
            case 6 when CanManageUsers: await Users.EnsureLoadedAsync(); break;
        }
    }
}
