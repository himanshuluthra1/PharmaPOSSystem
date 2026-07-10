using System.Windows.Input;
using PharmaPOS.Application.Features.Settings;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Settings;

public class PreferencesTabViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialog;
    private AppPreferencesDto _editor = new();
    private bool _isBusy;
    private bool _loaded;
    private string? _statusMessage;

    public PreferencesTabViewModel(ISettingsService settings, IDialogService dialog)
    {
        _settings = settings;
        _dialog = dialog;
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    public AppPreferencesDto Editor
    {
        get => _editor;
        private set => SetProperty(ref _editor, value);
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

    public ICommand SaveCommand { get; }

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        _loaded = true;
        IsBusy = true;
        try
        {
            Editor = await _settings.GetPreferencesAsync();
        }
        finally { IsBusy = false; }
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
            StatusMessage = "Preferences saved.";
        }
        finally { IsBusy = false; }
    }
}
