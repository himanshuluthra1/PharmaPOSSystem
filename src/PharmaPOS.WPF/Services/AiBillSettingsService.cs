using System.IO;
using System.Text.Json;

namespace PharmaPOS.WPF.Services;

public interface IAiBillSettingsService
{
    AiBillSettings Current { get; }
    void Load();
    void Save(AiBillSettings settings);
    bool IsGeminiReady { get; }
}

public sealed class AiBillSettingsService : IAiBillSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _gate = new();
    private readonly string _filePath;
    private AiBillSettings _current = new();

    public AiBillSettingsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PharmaPOS");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "ai-settings.json");
        Load();
    }

    public AiBillSettings Current
    {
        get { lock (_gate) return Clone(_current); }
    }

    public bool IsGeminiReady
    {
        get
        {
            lock (_gate)
                return _current.UseGemini && !string.IsNullOrWhiteSpace(_current.ApiKey);
        }
    }

    public void Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_filePath))
            {
                _current = new AiBillSettings();
                return;
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                _current = JsonSerializer.Deserialize<AiBillSettings>(json, JsonOptions) ?? new AiBillSettings();
                if (string.IsNullOrWhiteSpace(_current.Model))
                    _current.Model = "gemini-flash-lite-latest";
                _current.Model = MigrateModelId(_current.Model);
            }
            catch
            {
                _current = new AiBillSettings();
            }
        }
    }

    public void Save(AiBillSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            _current = Clone(settings);
            if (string.IsNullOrWhiteSpace(_current.Model))
                _current.Model = "gemini-flash-lite-latest";
            _current.Model = MigrateModelId(_current.Model);
            var json = JsonSerializer.Serialize(_current, JsonOptions);
            File.WriteAllText(_filePath, json);
        }
    }

    private static string MigrateModelId(string model)
    {
        // Google retired these ids for new API keys (404).
        return model.Trim() switch
        {
            "gemini-2.5-flash-lite" => "gemini-flash-lite-latest",
            "gemini-2.5-flash" => "gemini-flash-latest",
            "gemini-1.5-flash" => "gemini-flash-latest",
            "gemini-1.5-flash-latest" => "gemini-flash-latest",
            _ => model.Trim()
        };
    }

    private static AiBillSettings Clone(AiBillSettings s) => new()
    {
        UseGemini = s.UseGemini,
        ApiKey = s.ApiKey ?? string.Empty,
        Model = string.IsNullOrWhiteSpace(s.Model) ? "gemini-flash-lite-latest" : MigrateModelId(s.Model)
    };
}
