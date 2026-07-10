using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Settings;
using PharmaPOS.Shared.Constants;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Settings;

public class PermissionRowViewModel : ObservableObject
{
    private bool _isGranted;

    public PermissionRowViewModel(string key, string name, string module, bool isGranted)
    {
        Key = key;
        Name = name;
        Module = module;
        _isGranted = isGranted;
    }

    public string Key { get; }
    public string Name { get; }
    public string Module { get; }

    public bool IsGranted
    {
        get => _isGranted;
        set => SetProperty(ref _isGranted, value);
    }
}

public class PermissionModuleGroup
{
    public PermissionModuleGroup(string module, IEnumerable<PermissionRowViewModel> permissions)
    {
        Module = module;
        Permissions = new ObservableCollection<PermissionRowViewModel>(permissions);
    }

    public string Module { get; }
    public ObservableCollection<PermissionRowViewModel> Permissions { get; }
}

public class RolePermissionsTabViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialog;
    private RoleListDto? _selectedRole;
    private bool _isBusy;
    private bool _loaded;
    private string? _statusMessage;

    public RolePermissionsTabViewModel(
        ISettingsService settings,
        ICurrentUserService currentUser,
        IDialogService dialog)
    {
        _settings = settings;
        _dialog = dialog;
        CanManage = currentUser.HasAnyPermission(
            AppConstants.Permissions.UsersRoles, AppConstants.Permissions.UsersManage);

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy && CanManage && SelectedRole is not null);
        ResetDefaultsCommand = new AsyncRelayCommand(ResetDefaultsAsync, () => !IsBusy && CanManage && SelectedRole is not null);
        RefreshCommand = new AsyncRelayCommand(LoadRolesAsync);
    }

    public bool CanManage { get; }

    public ObservableCollection<RoleListDto> Roles { get; } = new();
    public ObservableCollection<PermissionModuleGroup> ModuleGroups { get; } = new();

    public RoleListDto? SelectedRole
    {
        get => _selectedRole;
        set
        {
            if (SetProperty(ref _selectedRole, value) && value is not null)
                _ = LoadPermissionsAsync(value.Id);
        }
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

    public string EditorTitle => SelectedRole is null
        ? "Select a role"
        : $"Permissions: {SelectedRole.Name}";

    public ICommand SaveCommand { get; }
    public ICommand ResetDefaultsCommand { get; }
    public ICommand RefreshCommand { get; }

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        _loaded = true;
        await LoadRolesAsync();
    }

    private async Task LoadRolesAsync()
    {
        IsBusy = true;
        try
        {
            var roles = await _settings.ListRolesAsync();
            Roles.Clear();
            foreach (var r in roles) Roles.Add(r);

            if (SelectedRole is null && Roles.Count > 0)
                SelectedRole = Roles[0];
            else if (SelectedRole is not null)
                SelectedRole = Roles.FirstOrDefault(r => r.Id == SelectedRole.Id) ?? Roles.FirstOrDefault();
        }
        finally { IsBusy = false; }
    }

    private async Task LoadPermissionsAsync(int roleId)
    {
        IsBusy = true;
        try
        {
            var data = await _settings.GetRolePermissionsAsync(roleId);
            ModuleGroups.Clear();
            if (data is null) return;

            var rows = data.AllPermissions
                .Select(p => new PermissionRowViewModel(
                    p.Key, p.Name, p.Module ?? "Other",
                    data.GrantedPermissionKeys.Contains(p.Key)))
                .ToList();

            foreach (var group in rows.GroupBy(r => r.Module).OrderBy(g => g.Key))
                ModuleGroups.Add(new PermissionModuleGroup(group.Key, group));

            OnPropertyChanged(nameof(EditorTitle));
        }
        finally { IsBusy = false; }
    }

    private async Task SaveAsync()
    {
        if (SelectedRole is null) return;

        var keys = ModuleGroups
            .SelectMany(g => g.Permissions)
            .Where(p => p.IsGranted)
            .Select(p => p.Key)
            .ToList();

        IsBusy = true;
        try
        {
            var result = await _settings.SaveRolePermissionsAsync(SelectedRole.Id, keys);
            if (result.IsFailure)
            {
                _dialog.ShowError(result.Error ?? "Could not save role permissions.");
                return;
            }
            StatusMessage = "Role permissions saved. Users with this role must sign in again to apply changes.";
        }
        catch (Exception ex)
        {
            _dialog.ShowError(ex.Message);
        }
        finally { IsBusy = false; }
    }

    private async Task ResetDefaultsAsync()
    {
        if (SelectedRole is null) return;

        var defaults = RolePermissionDefaults.ForRole(SelectedRole.Name);
        foreach (var group in ModuleGroups)
        {
            foreach (var permission in group.Permissions)
                permission.IsGranted = defaults.Contains(permission.Key);
        }

        StatusMessage = "Defaults applied. Click Save to persist.";
        await Task.CompletedTask;
    }
}
