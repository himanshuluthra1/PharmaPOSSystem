using System.IO;
using System.Text.Json;

namespace PharmaPOS.WPF.Services;

public interface IBillShareSettingsService
{
    BillShareSettings Current { get; }
    void Load();
    void Save(BillShareSettings settings);
    bool IsVpsUploadConfigured { get; }
    bool IsWhatsAppApiConfigured { get; }
}

public sealed class BillShareSettingsService : IBillShareSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _gate = new();
    private readonly string _filePath;
    private BillShareSettings _current = new();

    public BillShareSettingsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PharmaPOS");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "bill-share-settings.json");
        Load();
    }

    public BillShareSettings Current
    {
        get { lock (_gate) return Clone(_current); }
    }

    public bool IsVpsUploadConfigured
    {
        get
        {
            lock (_gate)
            {
                return _current.EnableVpsUpload
                       && !string.IsNullOrWhiteSpace(_current.PublicBaseUrl)
                       && !string.IsNullOrWhiteSpace(_current.SftpHost)
                       && !string.IsNullOrWhiteSpace(_current.SftpUsername)
                       && !string.IsNullOrWhiteSpace(_current.SftpRemoteDirectory);
            }
        }
    }

    public bool IsWhatsAppApiConfigured
    {
        get
        {
            lock (_gate)
            {
                return _current.EnableWhatsApp
                       && _current.EnableWhatsAppApi
                       && !string.IsNullOrWhiteSpace(_current.WhatsAppAccessToken)
                       && !string.IsNullOrWhiteSpace(_current.WhatsAppPhoneNumberId);
            }
        }
    }

    public void Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_filePath))
            {
                _current = new BillShareSettings();
                return;
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                _current = JsonSerializer.Deserialize<BillShareSettings>(json, JsonOptions)
                           ?? new BillShareSettings();
                if (_current.SftpPort <= 0) _current.SftpPort = 22;
                if (string.IsNullOrWhiteSpace(_current.WhatsAppApiVersion))
                    _current.WhatsAppApiVersion = "v21.0";
                if (string.IsNullOrWhiteSpace(_current.WhatsAppBillTemplateLanguage))
                    _current.WhatsAppBillTemplateLanguage = "en";

                // Old settings files omit enableTinyUrl (deserializes as false) — default ON.
                if (json.IndexOf("enableTinyUrl", StringComparison.OrdinalIgnoreCase) < 0)
                    _current.EnableTinyUrl = true;

                // Old files omit WhatsAppApiDesktopFallback — default ON.
                if (json.IndexOf("whatsAppApiDesktopFallback", StringComparison.OrdinalIgnoreCase) < 0)
                    _current.WhatsAppApiDesktopFallback = true;
            }
            catch
            {
                _current = new BillShareSettings();
            }
        }
    }

    public void Save(BillShareSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            _current = Clone(settings);
            if (_current.SftpPort <= 0) _current.SftpPort = 22;
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_current, JsonOptions));
        }
    }

    private static BillShareSettings Clone(BillShareSettings s) => new()
    {
        EnableWhatsApp = s.EnableWhatsApp,
        EnableSms = s.EnableSms,
        AskAfterSave = s.AskAfterSave,
        EnableVpsUpload = s.EnableVpsUpload,
        PublicBaseUrl = s.PublicBaseUrl?.Trim() ?? string.Empty,
        SftpHost = s.SftpHost?.Trim() ?? string.Empty,
        SftpPort = s.SftpPort <= 0 ? 22 : s.SftpPort,
        SftpUsername = s.SftpUsername?.Trim() ?? string.Empty,
        SftpPassword = s.SftpPassword ?? string.Empty,
        SftpRemoteDirectory = string.IsNullOrWhiteSpace(s.SftpRemoteDirectory)
            ? "/var/www/html/bills"
            : s.SftpRemoteDirectory.Trim(),
        EnableTinyUrl = s.EnableTinyUrl,
        EnableWhatsAppApi = s.EnableWhatsAppApi,
        WhatsAppAccessToken = s.WhatsAppAccessToken?.Trim() ?? string.Empty,
        WhatsAppPhoneNumberId = s.WhatsAppPhoneNumberId?.Trim() ?? string.Empty,
        WhatsAppApiVersion = string.IsNullOrWhiteSpace(s.WhatsAppApiVersion)
            ? "v21.0"
            : s.WhatsAppApiVersion.Trim(),
        WhatsAppBillTemplateName = s.WhatsAppBillTemplateName?.Trim() ?? string.Empty,
        WhatsAppBillTemplateLanguage = string.IsNullOrWhiteSpace(s.WhatsAppBillTemplateLanguage)
            ? "en"
            : s.WhatsAppBillTemplateLanguage.Trim(),
        WhatsAppApiDesktopFallback = s.WhatsAppApiDesktopFallback
    };
}
