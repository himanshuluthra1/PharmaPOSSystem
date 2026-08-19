using System.IO;
using System.Text.Json;

namespace PharmaPOS.WPF.Services;

public interface IBackupSettingsService
{
    BackupSettings Current { get; }
    BackupSettings Load();
    void Save(BackupSettings settings);
}

public sealed class BackupSettingsService : IBackupSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _gate = new();
    private readonly string _filePath;
    private BackupSettings _current = new();

    public BackupSettingsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PharmaPOS");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "backup-settings.json");
        Load();
    }

    public BackupSettings Current
    {
        get
        {
            lock (_gate) return Clone(_current);
        }
    }

    public BackupSettings Load()
    {
        lock (_gate)
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    _current = JsonSerializer.Deserialize<BackupSettings>(json, JsonOptions) ?? new BackupSettings();
                }
            }
            catch
            {
                _current = new BackupSettings();
            }

            if (_current.IntervalMinutes <= 0)
                _current.IntervalMinutes = 1440;

            return Clone(_current);
        }
    }

    public void Save(BackupSettings settings)
    {
        lock (_gate)
        {
            _current = Clone(settings);
            if (_current.IntervalMinutes <= 0)
                _current.IntervalMinutes = 1440;
            var json = JsonSerializer.Serialize(_current, JsonOptions);
            File.WriteAllText(_filePath, json);
        }
    }

    private static BackupSettings Clone(BackupSettings source) => new()
    {
        AutoEnabled = source.AutoEnabled,
        IntervalMinutes = source.IntervalMinutes,
        GoogleClientId = source.GoogleClientId ?? string.Empty,
        GoogleClientSecret = source.GoogleClientSecret ?? string.Empty,
        GoogleAccountEmail = source.GoogleAccountEmail,
        LastAutoBackupUtc = source.LastAutoBackupUtc,
        LastStatus = source.LastStatus
    };
}
