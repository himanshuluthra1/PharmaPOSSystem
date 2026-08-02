using System.Windows.Input;
using PharmaPOS.Application.Features.Settings;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Settings;

public class PreferencesTabViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IUiLayoutService _layout;
    private readonly IAiBillSettingsService _aiSettings;
    private readonly IBillShareSettingsService _billShareSettings;
    private readonly IDialogService _dialog;
    private AppPreferencesDto _editor = new();
    private bool _isBusy;
    private bool _loaded;
    private string? _statusMessage;
    private double _salesSidePanelWidth = 250;
    private double _purchaseSidePanelWidth = 240;
    private bool _useGemini;
    private string _geminiApiKey = string.Empty;
    private string _geminiModel = "gemini-flash-lite-latest";
    private bool _enableWhatsAppShare = true;
    private bool _enableSmsShare = true;
    private bool _askShareAfterSave = true;
    private bool _enableVpsUpload;
    private string _publicBaseUrl = string.Empty;
    private string _sftpHost = string.Empty;
    private int _sftpPort = 22;
    private string _sftpUsername = string.Empty;
    private string _sftpPassword = string.Empty;
    private string _sftpRemoteDirectory = "/var/www/bills";
    private bool _enableTinyUrl = true;

    public PreferencesTabViewModel(
        ISettingsService settings,
        IUiLayoutService layout,
        IAiBillSettingsService aiSettings,
        IBillShareSettingsService billShareSettings,
        IDialogService dialog)
    {
        _settings = settings;
        _layout = layout;
        _aiSettings = aiSettings;
        _billShareSettings = billShareSettings;
        _dialog = dialog;
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
        ResetLayoutCommand = new RelayCommand(ResetLayout);
    }

    public AppPreferencesDto Editor
    {
        get => _editor;
        private set => SetProperty(ref _editor, value);
    }

    public double SalesSidePanelWidth
    {
        get => _salesSidePanelWidth;
        set => SetProperty(ref _salesSidePanelWidth, Math.Clamp(value, 200, 480));
    }

    public double PurchaseSidePanelWidth
    {
        get => _purchaseSidePanelWidth;
        set => SetProperty(ref _purchaseSidePanelWidth, Math.Clamp(value, 200, 480));
    }

    public bool UseGemini
    {
        get => _useGemini;
        set => SetProperty(ref _useGemini, value);
    }

    public string GeminiApiKey
    {
        get => _geminiApiKey;
        set => SetProperty(ref _geminiApiKey, value ?? string.Empty);
    }

    public string GeminiModel
    {
        get => _geminiModel;
        set => SetProperty(ref _geminiModel, string.IsNullOrWhiteSpace(value) ? "gemini-flash-lite-latest" : value.Trim());
    }

    public bool EnableWhatsAppShare
    {
        get => _enableWhatsAppShare;
        set => SetProperty(ref _enableWhatsAppShare, value);
    }

    public bool EnableSmsShare
    {
        get => _enableSmsShare;
        set => SetProperty(ref _enableSmsShare, value);
    }

    public bool AskShareAfterSave
    {
        get => _askShareAfterSave;
        set => SetProperty(ref _askShareAfterSave, value);
    }

    public bool EnableVpsUpload
    {
        get => _enableVpsUpload;
        set => SetProperty(ref _enableVpsUpload, value);
    }

    public string PublicBaseUrl
    {
        get => _publicBaseUrl;
        set => SetProperty(ref _publicBaseUrl, value ?? string.Empty);
    }

    public string SftpHost
    {
        get => _sftpHost;
        set => SetProperty(ref _sftpHost, value ?? string.Empty);
    }

    public int SftpPort
    {
        get => _sftpPort;
        set => SetProperty(ref _sftpPort, value <= 0 ? 22 : value);
    }

    public string SftpUsername
    {
        get => _sftpUsername;
        set => SetProperty(ref _sftpUsername, value ?? string.Empty);
    }

    public string SftpPassword
    {
        get => _sftpPassword;
        set => SetProperty(ref _sftpPassword, value ?? string.Empty);
    }

    public string SftpRemoteDirectory
    {
        get => _sftpRemoteDirectory;
        set => SetProperty(ref _sftpRemoteDirectory,
            string.IsNullOrWhiteSpace(value) ? "/var/www/html/bills" : value.Trim());
    }

    public bool EnableTinyUrl
    {
        get => _enableTinyUrl;
        set => SetProperty(ref _enableTinyUrl, value);
    }

    public IReadOnlyList<string> GeminiModelOptions { get; } =
    [
        "gemini-flash-lite-latest",
        "gemini-flash-latest",
        "gemini-3.1-flash-lite",
        "gemini-3-flash-preview"
    ];

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

    public ICommand SaveCommand { get; }
    public ICommand ResetLayoutCommand { get; }

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        _loaded = true;
        IsBusy = true;
        try
        {
            Editor = await _settings.GetPreferencesAsync();
            LoadLayoutEditor();
            LoadAiEditor();
            LoadBillShareEditor();
        }
        finally { IsBusy = false; }
    }

    private void LoadLayoutEditor()
    {
        _layout.Load();
        SalesSidePanelWidth = _layout.GetSidePanelWidth(UiLayoutService.SalesKey);
        PurchaseSidePanelWidth = _layout.GetSidePanelWidth(UiLayoutService.PurchaseKey);
    }

    private void LoadAiEditor()
    {
        _aiSettings.Load();
        var ai = _aiSettings.Current;
        UseGemini = ai.UseGemini;
        GeminiApiKey = ai.ApiKey;
        GeminiModel = ai.Model;
    }

    private void LoadBillShareEditor()
    {
        _billShareSettings.Load();
        var s = _billShareSettings.Current;
        EnableWhatsAppShare = s.EnableWhatsApp;
        EnableSmsShare = s.EnableSms;
        AskShareAfterSave = s.AskAfterSave;
        EnableVpsUpload = s.EnableVpsUpload;
        PublicBaseUrl = s.PublicBaseUrl;
        SftpHost = s.SftpHost;
        SftpPort = s.SftpPort;
        SftpUsername = s.SftpUsername;
        SftpPassword = s.SftpPassword;
        SftpRemoteDirectory = s.SftpRemoteDirectory;
        EnableTinyUrl = s.EnableTinyUrl;
    }

    private async Task SaveAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _settings.SavePreferencesAsync(Editor);
            if (result.IsFailure)
            {
                _dialog.ShowError(result.Error ?? "Could not save preferences.");
                return;
            }

            _layout.SetSidePanelWidth(UiLayoutService.SalesKey, SalesSidePanelWidth);
            _layout.SetSidePanelWidth(UiLayoutService.PurchaseKey, PurchaseSidePanelWidth);
            _layout.Save();

            _aiSettings.Save(new AiBillSettings
            {
                // If a key is present, treat Gemini as intended even if the checkbox was missed.
                UseGemini = UseGemini || !string.IsNullOrWhiteSpace(GeminiApiKey),
                ApiKey = GeminiApiKey.Trim(),
                Model = GeminiModel
            });
            UseGemini = _aiSettings.Current.UseGemini;

            _billShareSettings.Save(new BillShareSettings
            {
                EnableWhatsApp = EnableWhatsAppShare,
                EnableSms = EnableSmsShare,
                AskAfterSave = AskShareAfterSave,
                EnableVpsUpload = EnableVpsUpload,
                PublicBaseUrl = PublicBaseUrl.Trim(),
                SftpHost = SftpHost.Trim(),
                SftpPort = SftpPort,
                SftpUsername = SftpUsername.Trim(),
                SftpPassword = SftpPassword,
                SftpRemoteDirectory = SftpRemoteDirectory.Trim(),
                EnableTinyUrl = EnableTinyUrl
            });

            StatusMessage = EnableVpsUpload && _billShareSettings.IsVpsUploadConfigured
                ? "Preferences saved. Bill PDFs will upload to your VPS and a short link will be shared on WhatsApp."
                : "Preferences saved.";
        }
        finally { IsBusy = false; }
    }

    private void ResetLayout()
    {
        if (!_dialog.Confirm(
                "Reset Sales and Purchase panel widths and grid column widths to defaults on this PC?",
                "Reset layout"))
            return;

        _layout.ResetToDefaults();
        LoadLayoutEditor();
        StatusMessage = "Layout reset to defaults. Re-open Sales/Purchase to apply.";
    }
}
