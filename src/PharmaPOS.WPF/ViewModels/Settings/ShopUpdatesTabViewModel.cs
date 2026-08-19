using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Win32;
using PharmaPOS.Shared.Constants;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Settings;

public sealed class ShopUpdateItem : ObservableObject
{
    private bool _isSelected;

    public required PosShopRow Shop { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string StoreCode => Shop.StoreCode;
    public string StoreId => Shop.StoreId;
    public string MachineName => Shop.MachineName ?? "—";
    public string Approved => Shop.IsApproved ? "Yes" : "Pending";
    public string AppVersion => string.IsNullOrWhiteSpace(Shop.AppVersion) ? "—" : Shop.AppVersion;
    public string LastSeen => Shop.LastSeenUtc is DateTime utc
        ? utc.ToLocalTime().ToString("dd-MMM HH:mm")
        : "Never";
    public string UpdateStatus
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Shop.PendingVersion)) return "—";
            return $"{Shop.PendingVersion} ({Shop.AssignmentStatus})";
        }
    }
}

public sealed class ShopUpdatesTabViewModel : ObservableObject
{
    private readonly IPosUpdateService _updates;
    private readonly IDialogService _dialog;
    private bool _isVendor;
    private bool _isBusy;
    private bool _loaded;
    private string? _statusMessage;
    private string _packageVersion = AppConstants.ApplicationVersion;
    private string _packageNotes = string.Empty;
    private string? _selectedPackagePath;
    private PosReleaseRow? _selectedRelease;

    public ShopUpdatesTabViewModel(IPosUpdateService updates, IDialogService dialog)
    {
        _updates = updates;
        _dialog = dialog;

        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        BrowsePackageCommand = new RelayCommand(_ => BrowsePackage(), _ => !IsBusy);
        PublishCommand = new AsyncRelayCommand(PublishAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(SelectedPackagePath));
        SendUpdateCommand = new AsyncRelayCommand(SendAsync, () => !IsBusy && SelectedRelease is not null);
        SelectAllCommand = new RelayCommand(_ => SetAllSelected(true), _ => Shops.Count > 0);
        SelectNoneCommand = new RelayCommand(_ => SetAllSelected(false), _ => Shops.Count > 0);
    }

    public ObservableCollection<ShopUpdateItem> Shops { get; } = new();
    public ObservableCollection<PosReleaseRow> Releases { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand BrowsePackageCommand { get; }
    public ICommand PublishCommand { get; }
    public ICommand SendUpdateCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand SelectNoneCommand { get; }

    public bool IsVendor
    {
        get => _isVendor;
        private set => SetProperty(ref _isVendor, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string PackageVersion
    {
        get => _packageVersion;
        set => SetProperty(ref _packageVersion, value);
    }

    public string PackageNotes
    {
        get => _packageNotes;
        set => SetProperty(ref _packageNotes, value);
    }

    public string? SelectedPackagePath
    {
        get => _selectedPackagePath;
        private set
        {
            if (SetProperty(ref _selectedPackagePath, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public PosReleaseRow? SelectedRelease
    {
        get => _selectedRelease;
        set
        {
            if (SetProperty(ref _selectedRelease, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        _loaded = true;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            IsVendor = await _updates.IsVendorConsoleAsync();
            if (!IsVendor)
            {
                StatusMessage = "This PC is not a vendor console. Shop updates stay on the software-provider store only.";
                return;
            }

            var shops = await _updates.ListShopsAsync();
            var selected = Shops.Where(s => s.IsSelected).Select(s => s.StoreId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Shops.Clear();
            foreach (var shop in shops)
            {
                Shops.Add(new ShopUpdateItem
                {
                    Shop = shop,
                    IsSelected = selected.Contains(shop.StoreId)
                });
            }

            var previousVersion = SelectedRelease?.Version;
            Releases.Clear();
            foreach (var release in await _updates.ListReleasesAsync())
                Releases.Add(release);
            SelectedRelease = Releases.FirstOrDefault(r => r.Version == previousVersion) ?? Releases.FirstOrDefault();

            StatusMessage = $"{Shops.Count} shop(s). This PC is {AppConstants.ApplicationVersion}.";
        }
        catch (Exception ex)
        {
            StatusMessage = null;
            _dialog.ShowError("Could not load shops from the server.\n\n" + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void BrowsePackage()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select PharmaPOS installer",
            Filter = "PharmaPOS setup|PharmaPOS-Setup-*.exe|Installer (*.exe)|*.exe",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() == true)
            SelectedPackagePath = dlg.FileName;
    }

    private async Task PublishAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedPackagePath)) return;
        IsBusy = true;
        try
        {
            var result = await _updates.PublishReleaseAsync(new PosPublishRequest
            {
                Version = PackageVersion,
                LocalFilePath = SelectedPackagePath,
                Notes = string.IsNullOrWhiteSpace(PackageNotes) ? null : PackageNotes.Trim()
            });
            if (!result.Success)
            {
                _dialog.ShowError(result.Message);
                return;
            }

            StatusMessage = result.Message;
            await LoadAsync();
            SelectedRelease = Releases.FirstOrDefault(r =>
                string.Equals(r.Version, PackageVersion.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _dialog.ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SendAsync()
    {
        if (SelectedRelease is null) return;
        var ids = Shops.Where(s => s.IsSelected && s.Shop.IsApproved).Select(s => s.StoreId).ToList();
        if (ids.Count == 0)
        {
            _dialog.ShowError("Tick one or more approved shops.");
            return;
        }

        if (!_dialog.Confirm(
                $"Send version {SelectedRelease.Version} to {ids.Count} shop(s)?\n\n" +
                "Each shop will be asked to download and install when PharmaPOS is open.",
                "Send update"))
            return;

        IsBusy = true;
        try
        {
            await _updates.AssignUpdateAsync(ids, SelectedRelease.Version);
            StatusMessage = $"Update {SelectedRelease.Version} sent to {ids.Count} shop(s).";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _dialog.ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetAllSelected(bool value)
    {
        foreach (var shop in Shops)
            shop.IsSelected = value;
    }
}
