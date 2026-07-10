using System.Windows.Input;
using Microsoft.Win32;
using PharmaPOS.Application.Features.Settings;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Settings;

public class CompanyTabViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialog;
    private CompanyProfileDto _editor = new();
    private bool _isBusy;
    private bool _loaded;
    private string? _statusMessage;

    public CompanyTabViewModel(ISettingsService settings, IDialogService dialog)
    {
        _settings = settings;
        _dialog = dialog;
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
        BrowseLogoCommand = new RelayCommand(_ => BrowseLogo());
    }

    public CompanyProfileDto Editor
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
    public ICommand BrowseLogoCommand { get; }

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
            var profile = await _settings.GetCompanyProfileAsync();
            Editor = profile ?? new CompanyProfileDto();
        }
        finally { IsBusy = false; }
    }

    private async Task SaveAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _settings.SaveCompanyProfileAsync(Editor);
            if (result.IsFailure)
            {
                _dialog.ShowError(result.Error ?? "Could not save company profile.");
                return;
            }
            Editor.Id = result.Value;
            StatusMessage = "Company profile saved.";
        }
        finally { IsBusy = false; }
    }

    private void BrowseLogo()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*",
            Title = "Select company logo"
        };
        if (dlg.ShowDialog() == true)
        {
            Editor.LogoPath = dlg.FileName;
            OnPropertyChanged(nameof(Editor));
        }
    }
}
