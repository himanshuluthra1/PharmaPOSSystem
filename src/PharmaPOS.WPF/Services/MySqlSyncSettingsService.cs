using System.IO;
using System.Text.Json;
using PharmaPOS.Application.Features.ReportingSync;

namespace PharmaPOS.WPF.Services;

public interface IMySqlSyncSettingsService
{
    MySqlSyncSettings Current { get; }
    void Load();
    void Save(MySqlSyncSettings settings);
    void UpdateStatus(DateTime? lastSuccessAtUtc, string? lastError);
    bool IsConfigured { get; }
}

public sealed class MySqlSyncSettingsService : IMySqlSyncSettingsService, IReportingSyncGate
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _gate = new();
    private readonly string _filePath;
    private MySqlSyncSettings _current = new();

    public MySqlSyncSettingsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PharmaPOS");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "mysql-sync-settings.json");
        Load();
    }

    public MySqlSyncSettings Current
    {
        get { lock (_gate) return Clone(_current); }
    }

    public bool IsConfigured
    {
        get
        {
            lock (_gate)
            {
                return !string.IsNullOrWhiteSpace(_current.Host)
                       && !string.IsNullOrWhiteSpace(_current.Database)
                       && !string.IsNullOrWhiteSpace(_current.Username)
                       && _current.Port > 0;
            }
        }
    }

    bool IReportingSyncGate.IsEnabled
    {
        get
        {
            lock (_gate) return _current.Enabled && IsConfiguredUnlocked();
        }
    }

    string? IReportingSyncGate.StoreCodeOverride
    {
        get
        {
            lock (_gate)
            {
                return string.IsNullOrWhiteSpace(_current.StoreCodeOverride)
                    ? null
                    : _current.StoreCodeOverride;
            }
        }
    }

    public void Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_filePath))
            {
                _current = new MySqlSyncSettings();
                return;
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                _current = JsonSerializer.Deserialize<MySqlSyncSettings>(json, JsonOptions)
                           ?? new MySqlSyncSettings();
                if (_current.Port <= 0) _current.Port = 3306;
                if (string.IsNullOrWhiteSpace(_current.Database))
                    _current.Database = "pharmapos_reporting";
            }
            catch
            {
                _current = new MySqlSyncSettings();
            }
        }
    }

    public void Save(MySqlSyncSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            _current = Clone(settings);
            PersistUnlocked();
        }
    }

    public void UpdateStatus(DateTime? lastSuccessAtUtc, string? lastError)
    {
        lock (_gate)
        {
            if (lastSuccessAtUtc.HasValue)
                _current.LastSuccessAtUtc = lastSuccessAtUtc;
            _current.LastError = string.IsNullOrWhiteSpace(lastError) ? null : lastError.Trim();
            PersistUnlocked();
        }
    }

    private void PersistUnlocked()
    {
        if (_current.Port <= 0) _current.Port = 3306;
        File.WriteAllText(_filePath, JsonSerializer.Serialize(_current, JsonOptions));
    }

    private bool IsConfiguredUnlocked() =>
        !string.IsNullOrWhiteSpace(_current.Host)
        && !string.IsNullOrWhiteSpace(_current.Database)
        && !string.IsNullOrWhiteSpace(_current.Username)
        && _current.Port > 0;

    private static MySqlSyncSettings Clone(MySqlSyncSettings s) => new()
    {
        Enabled = s.Enabled,
        Host = s.Host?.Trim() ?? string.Empty,
        Port = s.Port <= 0 ? 3306 : s.Port,
        Database = string.IsNullOrWhiteSpace(s.Database) ? "pharmapos_reporting" : s.Database.Trim(),
        Username = s.Username?.Trim() ?? string.Empty,
        Password = s.Password ?? string.Empty,
        UseSsl = s.UseSsl,
        StoreCodeOverride = string.IsNullOrWhiteSpace(s.StoreCodeOverride)
            ? null
            : s.StoreCodeOverride.Trim().ToUpperInvariant(),
        LastSuccessAtUtc = s.LastSuccessAtUtc,
        LastError = s.LastError
    };
}
