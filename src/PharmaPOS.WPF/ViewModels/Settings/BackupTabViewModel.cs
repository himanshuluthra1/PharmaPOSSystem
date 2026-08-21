using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using Microsoft.Win32;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;
using PharmaPOS.WPF.Views;

namespace PharmaPOS.WPF.ViewModels.Settings;

public sealed class BackupFrequencyOption
{
    public BackupFrequencyOption(string label, int minutes)
    {
        Label = label;
        Minutes = minutes;
    }

    public string Label { get; }
    public int Minutes { get; }
}

public sealed class BackupTabViewModel : ObservableObject
{
    private readonly IBackupSettingsService _settings;
    private readonly IDatabaseBackupService _backup;
    private readonly IGoogleDriveBackupService _drive;
    private readonly IDialogService _dialog;

    private bool _loaded;
    private bool _isBusy;
    private bool _autoEnabled;
    private BackupFrequencyOption? _selectedFrequency;
    private string _googleClientId = string.Empty;
    private string _googleClientSecret = string.Empty;
    private string? _googleAccountEmail;
    private string? _statusMessage;
    private string _statusText = string.Empty;

    public BackupTabViewModel(
        IBackupSettingsService settings,
        IDatabaseBackupService backup,
        IGoogleDriveBackupService drive,
        IDialogService dialog)
    {
        _settings = settings;
        _backup = backup;
        _drive = drive;
        _dialog = dialog;

        Frequencies =
        [
            new BackupFrequencyOption("Every 15 minutes", 15),
            new BackupFrequencyOption("Every hour", 60),
            new BackupFrequencyOption("Every 6 hours", 360),
            new BackupFrequencyOption("Once a day", 1440)
        ];
        _selectedFrequency = Frequencies[3];

        ManualBackupCommand = new AsyncRelayCommand(ManualBackupAsync, () => !IsBusy);
        RestoreFromFileCommand = new AsyncRelayCommand(RestoreFromFileAsync, () => !IsBusy);
        RestoreFromDriveCommand = new AsyncRelayCommand(RestoreFromDriveAsync, () => !IsBusy && IsGoogleConnected);
        ConnectGoogleCommand = new AsyncRelayCommand(ConnectGoogleAsync, () => !IsBusy && !IsGoogleConnected);
        DisconnectGoogleCommand = new RelayCommand(_ => DisconnectGoogle(), _ => !IsBusy && IsGoogleConnected);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, () => !IsBusy);
    }

    public ObservableCollection<BackupFrequencyOption> Frequencies { get; }

    public ICommand ManualBackupCommand { get; }
    public ICommand RestoreFromFileCommand { get; }
    public ICommand RestoreFromDriveCommand { get; }
    public ICommand ConnectGoogleCommand { get; }
    public ICommand DisconnectGoogleCommand { get; }
    public ICommand SaveSettingsCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool AutoEnabled
    {
        get => _autoEnabled;
        set
        {
            if (!SetProperty(ref _autoEnabled, value)) return;
            PersistScheduleIfLoaded();
        }
    }

    public BackupFrequencyOption? SelectedFrequency
    {
        get => _selectedFrequency;
        set
        {
            if (!SetProperty(ref _selectedFrequency, value)) return;
            PersistScheduleIfLoaded();
        }
    }

    public string GoogleClientId
    {
        get => _googleClientId;
        set => SetProperty(ref _googleClientId, value);
    }

    public string GoogleClientSecret
    {
        get => _googleClientSecret;
        set => SetProperty(ref _googleClientSecret, value);
    }

    public string? GoogleAccountEmail
    {
        get => _googleAccountEmail;
        private set
        {
            if (!SetProperty(ref _googleAccountEmail, value)) return;
            OnPropertyChanged(nameof(IsGoogleConnected));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsGoogleConnected =>
        _drive.HasSavedToken || !string.IsNullOrWhiteSpace(GoogleAccountEmail);

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public Task EnsureLoadedAsync()
    {
        if (_loaded) return Task.CompletedTask;
        Apply(_settings.Load());
        _loaded = true;
        return Task.CompletedTask;
    }

    private void Apply(BackupSettings settings)
    {
        AutoEnabled = settings.AutoEnabled;
        GoogleClientId = settings.GoogleClientId;
        GoogleClientSecret = settings.GoogleClientSecret;
        GoogleAccountEmail = settings.GoogleAccountEmail;
        SelectedFrequency = Frequencies.FirstOrDefault(f => f.Minutes == settings.IntervalMinutes)
                            ?? Frequencies[3];
        StatusText = BuildStatus(settings);
        OnPropertyChanged(nameof(IsGoogleConnected));
    }

    private static string BuildStatus(BackupSettings settings)
    {
        var last = settings.LastAutoBackupUtc is DateTime utc
            ? $"Last auto backup: {utc.ToLocalTime():dd-MMM-yyyy HH:mm}."
            : "No automatic backup yet.";
        if (string.IsNullOrWhiteSpace(settings.LastStatus))
            return last;
        return $"{last} {settings.LastStatus}";
    }

    private async Task ManualBackupAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save database backup",
            Filter = "SQL Server backup (*.bak)|*.bak",
            FileName = _backup.SuggestFileName()
        };
        if (dialog.ShowDialog() != true)
            return;

        IsBusy = true;
        StatusMessage = null;
        try
        {
            var path = await _backup.BackupToFileAsync(dialog.FileName);
            StatusMessage = $"Backup saved to {path}";
            _dialog.ShowInfo($"Backup saved to:\n{path}", "Backup");
        }
        catch (Exception ex)
        {
            _dialog.ShowError($"Backup failed: {ex.Message}", "Backup");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RestoreFromFileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Restore database backup",
            Filter = "SQL Server backup (*.bak)|*.bak",
            InitialDirectory = Directory.Exists(_backup.AutoBackupFolder)
                ? _backup.AutoBackupFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog() != true)
            return;

        await RestoreAndRestartAsync(dialog.FileName);
    }

    private async Task RestoreFromDriveAsync()
    {
        IsBusy = true;
        StatusMessage = null;
        IReadOnlyList<DriveBackupFile> files;
        try
        {
            files = await _drive.ListBackupFilesAsync(GoogleClientId, GoogleClientSecret);
        }
        catch (Exception ex)
        {
            IsBusy = false;
            _dialog.ShowError($"Could not list Google Drive backups: {ex.Message}", "Restore");
            return;
        }
        finally
        {
            IsBusy = false;
        }

        if (files.Count == 0)
        {
            _dialog.ShowError("No .bak files were found in the PharmaPOS Backups folder on Google Drive.", "Restore");
            return;
        }

        var picker = new RestoreBackupPickerWindow(files)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        if (picker.ShowDialog() != true || picker.SelectedFile is null)
            return;

        if (!_dialog.Confirm(
                "Restore replaces ALL current shop data with this Google Drive backup.\n\n" +
                "PharmaPOS will close and reopen after restore. Continue?",
                "Restore backup"))
            return;

        var localPath = Path.Combine(_backup.AutoBackupFolder, picker.SelectedFile.Name);
        IsBusy = true;
        try
        {
            await _drive.DownloadFileAsync(
                picker.SelectedFile.Id, localPath, GoogleClientId, GoogleClientSecret);
        }
        catch (Exception ex)
        {
            IsBusy = false;
            _dialog.ShowError($"Could not download backup: {ex.Message}", "Restore");
            return;
        }

        await RestoreAndRestartAsync(localPath, alreadyConfirmed: true);
    }

    private async Task RestoreAndRestartAsync(string bakPath, bool alreadyConfirmed = false)
    {
        if (!alreadyConfirmed && !_dialog.Confirm(
                "Restore replaces ALL current shop data with this backup.\n\n" +
                "PharmaPOS will close and reopen after restore. Continue?",
                "Restore backup"))
        {
            IsBusy = false;
            return;
        }

        IsBusy = true;
        StatusMessage = null;
        try
        {
            await _backup.RestoreFromFileAsync(bakPath);
            StatusMessage = "Restore completed. PharmaPOS will restart.";
            _dialog.ShowInfo("Restore completed. PharmaPOS will restart now.", "Restore");
            RestartApp();
        }
        catch (Exception ex)
        {
            _dialog.ShowError($"Restore failed: {ex.Message}", "Restore");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static void RestartApp()
    {
        var exe = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(exe))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory
            });
        }

        System.Windows.Application.Current?.Shutdown();
    }

    private async Task ConnectGoogleAsync()
    {
        IsBusy = true;
        StatusMessage = null;
        try
        {
            await SaveSettingsAsync();
            var email = await _drive.ConnectAsync(GoogleClientId, GoogleClientSecret);
            GoogleAccountEmail = email;
            await SaveSettingsAsync();
            StatusMessage = "Google Drive connected. Automatic backups will upload to the PharmaPOS Backups folder.";
        }
        catch (Exception ex)
        {
            _dialog.ShowError($"Could not connect Google Drive: {ex.Message}", "Google Drive");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void DisconnectGoogle()
    {
        _drive.Disconnect();
        GoogleAccountEmail = null;
        var current = BuildCurrent();
        current.GoogleAccountEmail = null;
        _settings.Save(current);
        StatusMessage = "Google Drive disconnected. Automatic backups will stay on this computer.";
        OnPropertyChanged(nameof(IsGoogleConnected));
        CommandManager.InvalidateRequerySuggested();
    }

    private Task SaveSettingsAsync()
    {
        _settings.Save(BuildCurrent());
        StatusMessage = "Backup settings saved.";
        return Task.CompletedTask;
    }

    private void PersistScheduleIfLoaded()
    {
        if (!_loaded) return;
        _settings.Save(BuildCurrent());
    }

    private BackupSettings BuildCurrent()
    {
        var existing = _settings.Current;
        return new BackupSettings
        {
            AutoEnabled = AutoEnabled,
            IntervalMinutes = SelectedFrequency?.Minutes ?? 1440,
            GoogleClientId = GoogleClientId.Trim(),
            GoogleClientSecret = GoogleClientSecret.Trim(),
            GoogleAccountEmail = GoogleAccountEmail,
            LastAutoBackupUtc = existing.LastAutoBackupUtc,
            LastStatus = existing.LastStatus
        };
    }
}
