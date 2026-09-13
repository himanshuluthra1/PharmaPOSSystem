using System.Collections.ObjectModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Authentication;
using PharmaPOS.Application.Features.Counters;
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
        IBillingCounterService counters,
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
        IPosUpdateService posUpdates,
        IBackupSettingsService backupSettings,
        IDatabaseBackupService databaseBackup,
        IGoogleDriveBackupService googleDrive,
        IConfiguration configuration,
        IFinancialYearContext financialYear,
        IDateTimeProvider clock,
        IDialogService dialog)
    {
        var user = currentUser;

        CanManageCompany = user.HasAnyPermission(
            AppConstants.Permissions.SettingsMenuCompany,
            AppConstants.Permissions.SettingsCompany,
            AppConstants.Permissions.SettingsManage);
        CanManageBranches = user.HasAnyPermission(
            AppConstants.Permissions.SettingsMenuBranches,
            AppConstants.Permissions.SettingsBranches,
            AppConstants.Permissions.SettingsManage);
        CanManageCounters = user.HasAnyPermission(
            AppConstants.Permissions.SettingsMenuCounters,
            AppConstants.Permissions.SettingsMenuBranches,
            AppConstants.Permissions.SettingsBranches,
            AppConstants.Permissions.SettingsManage);
        CanManagePreferences = user.HasAnyPermission(
            AppConstants.Permissions.SettingsMenuPreferences,
            AppConstants.Permissions.SettingsPreferences,
            AppConstants.Permissions.SettingsManage);
        CanManageUsers = user.HasAnyPermission(
            AppConstants.Permissions.SettingsMenuUsers,
            AppConstants.Permissions.UsersEdit,
            AppConstants.Permissions.UsersManage);
        CanManageRoles = user.HasAnyPermission(
            AppConstants.Permissions.SettingsMenuRoles,
            AppConstants.Permissions.UsersRoles,
            AppConstants.Permissions.UsersManage);
        CanManageMedicineMapping = user.HasAnyPermission(
            AppConstants.Permissions.SettingsMenuMedicineMapping,
            AppConstants.Permissions.SettingsManage);
        CanManageNewMedicineMapping = user.HasAnyPermission(
            AppConstants.Permissions.SettingsMenuNewMedicineMapping,
            AppConstants.Permissions.SettingsManage);
        CanManageMedWinImport = user.HasAnyPermission(
            AppConstants.Permissions.SettingsMenuMedWinImport,
            AppConstants.Permissions.SettingsManage);
        CanManagePassword = user.HasAnyPermission(
            AppConstants.Permissions.SettingsMenuPassword,
            AppConstants.Permissions.SettingsManage)
            || user.CanAccessModule("settings")
            || user.CanAccessModule("users");
        CanManageAppearance = user.HasAnyPermission(
            AppConstants.Permissions.SettingsMenuAppearance,
            AppConstants.Permissions.SettingsManage)
            || user.CanAccessModule("settings")
            || user.CanAccessModule("users");
        CanManageBackup = user.HasAnyPermission(
            AppConstants.Permissions.SettingsMenuBackup,
            AppConstants.Permissions.SettingsPreferences,
            AppConstants.Permissions.SettingsManage);
        CanManageUpdates = user.HasAnyPermission(
            AppConstants.Permissions.SettingsMenuUpdates,
            AppConstants.Permissions.SettingsPreferences,
            AppConstants.Permissions.SettingsManage)
            || user.CanAccessModule("settings");
        CanAccessSettings = user.CanAccessModule("settings") || user.CanAccessModule("users");

        Company = new CompanyTabViewModel(settings, dialog);
        Branches = new BranchesTabViewModel(settings, dialog);
        Counters = new CountersTabViewModel(counters, currentUser, dialog);
        Preferences = new PreferencesTabViewModel(
            settings, layout, aiSettings, billShareSettings, mySqlSyncSettings, mySqlPublisher, storeIdentity,
            financialYear, clock, dialog);
        MedicineMapping = new MedicineMappingTabViewModel(scopeFactory, dialog, aiSettings, geminiMedicineMatcher);
        NewMedicineMapping = new NewMedicineMappingTabViewModel(scopeFactory, dialog);
        MedWinImport = new MedWinImportTabViewModel(configuration, dialog);
        RolePermissions = new RolePermissionsTabViewModel(settings, currentUser, dialog);
        Users = new UsersTabViewModel(settings, currentUser, dialog);
        ChangePassword = new ChangePasswordTabViewModel(auth, currentUser, dialog);
        Appearance = new AppearanceTabViewModel(theme);
        ShopUpdates = new ShopUpdatesTabViewModel(posUpdates, dialog);
        Backup = new BackupTabViewModel(backupSettings, databaseBackup, googleDrive, dialog);

        // Side nav lists only allowed sections — avoids TabControl header overflow / missing tabs.
        if (CanManageCompany) Sections.Add(new SettingsSection("Company", 0));
        if (CanManageBranches) Sections.Add(new SettingsSection("Branches", 1));
        if (CanManageCounters) Sections.Add(new SettingsSection("Counters", 2));
        if (CanManagePreferences) Sections.Add(new SettingsSection("Preferences", 3));
        if (CanManageMedicineMapping) Sections.Add(new SettingsSection("Medicine Mapping", 4));
        if (CanManageNewMedicineMapping) Sections.Add(new SettingsSection("New Medicine Mapping", 5));
        if (CanManageMedWinImport) Sections.Add(new SettingsSection("MedWin Import", 6));
        if (CanManageRoles) Sections.Add(new SettingsSection("Roles & Permissions", 7));
        if (CanManageUsers) Sections.Add(new SettingsSection("Users", 8));
        if (CanManagePassword) Sections.Add(new SettingsSection("My Password", 9));
        if (CanManageAppearance) Sections.Add(new SettingsSection("Appearance", 10));
        if (CanManageBackup) Sections.Add(new SettingsSection("Backup", 11));
        if (CanManageUpdates) Sections.Add(new SettingsSection("Shop updates", 12));

        if (Sections.Count == 0)
            Sections.Add(new SettingsSection("My Password", 9));

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
    public bool CanManageCounters { get; }
    public bool CanManagePreferences { get; }
    public bool CanManageRoles { get; }
    public bool CanManageUsers { get; }
    public bool CanManageMedicineMapping { get; }
    public bool CanManageNewMedicineMapping { get; }
    public bool CanManageMedWinImport { get; }
    public bool CanManagePassword { get; }
    public bool CanManageAppearance { get; }
    public bool CanManageBackup { get; }
    public bool CanManageUpdates { get; }

    public CompanyTabViewModel Company { get; }
    public BranchesTabViewModel Branches { get; }
    public CountersTabViewModel Counters { get; }
    public PreferencesTabViewModel Preferences { get; }
    public MedicineMappingTabViewModel MedicineMapping { get; }
    public NewMedicineMappingTabViewModel NewMedicineMapping { get; }
    public MedWinImportTabViewModel MedWinImport { get; }
    public RolePermissionsTabViewModel RolePermissions { get; }
    public UsersTabViewModel Users { get; }
    public ChangePasswordTabViewModel ChangePassword { get; }
    public AppearanceTabViewModel Appearance { get; }
    public ShopUpdatesTabViewModel ShopUpdates { get; }
    public BackupTabViewModel Backup { get; }

    public int SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (!SetProperty(ref _selectedTab, value)) return;
            _ = LoadSelectedTabAsync();
        }
    }

    /// <summary>Select a settings section by its TabControl index (used by top-menu submenus).</summary>
    public void SelectTab(int tabIndex)
    {
        var section = Sections.FirstOrDefault(s => s.TabIndex == tabIndex);
        if (section is not null)
            SelectedSection = section;
        else
            SelectedTab = tabIndex;
    }

    private async Task LoadSelectedTabAsync()
    {
        switch (SelectedTab)
        {
            case 0 when CanManageCompany: await Company.EnsureLoadedAsync(); break;
            case 1 when CanManageBranches: await Branches.EnsureLoadedAsync(); break;
            case 2 when CanManageBranches: await Counters.EnsureLoadedAsync(); break;
            case 3 when CanManagePreferences: await Preferences.EnsureLoadedAsync(); break;
            case 4 when CanManageMedicineMapping: await MedicineMapping.EnsureLoadedAsync(); break;
            case 5 when CanManageMedicineMapping: await NewMedicineMapping.EnsureLoadedAsync(); break;
            case 7 when CanManageRoles: await RolePermissions.EnsureLoadedAsync(); break;
            case 8 when CanManageUsers: await Users.EnsureLoadedAsync(); break;
            case 11 when CanManagePreferences: await Backup.EnsureLoadedAsync(); break;
            case 12: await ShopUpdates.EnsureLoadedAsync(); break;
        }
    }
}
