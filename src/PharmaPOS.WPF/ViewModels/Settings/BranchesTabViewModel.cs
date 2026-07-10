using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Features.Settings;
using PharmaPOS.Domain.Enums;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Settings;

public class BranchesTabViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialog;
    private BranchListDto? _selected;
    private BranchDetailDto _editor = new();
    private bool _isBusy;
    private bool _loaded;
    private string? _statusMessage;

    public BranchesTabViewModel(ISettingsService settings, IDialogService dialog)
    {
        _settings = settings;
        _dialog = dialog;
        NewCommand = new RelayCommand(_ => BeginNew());
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
    }

    public ObservableCollection<BranchListDto> Items { get; } = new();
    public Array EntityStatuses => Enum.GetValues(typeof(EntityStatus));

    public BranchListDto? SelectedItem
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value) && value is not null)
                _ = LoadItemAsync(value.Id);
        }
    }

    public BranchDetailDto Editor
    {
        get => _editor;
        private set
        {
            if (SetProperty(ref _editor, value))
            {
                OnPropertyChanged(nameof(EditorTitle));
                OnPropertyChanged(nameof(IsNewRecord));
            }
        }
    }

    public string EditorTitle => Editor.Id > 0 ? $"Edit: {Editor.Name}" : "New Branch";
    public bool IsNewRecord => Editor.Id == 0;

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

    public ICommand NewCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand RefreshCommand { get; }

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        _loaded = true;
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var rows = await _settings.ListBranchesAsync();
            Items.Clear();
            foreach (var r in rows) Items.Add(r);
        }
        finally { IsBusy = false; }
    }

    private async Task LoadItemAsync(int id)
    {
        var detail = await _settings.GetBranchAsync(id);
        if (detail is not null) Editor = detail;
    }

    private void BeginNew()
    {
        SelectedItem = null;
        Editor = new BranchDetailDto();
    }

    private async Task SaveAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _settings.SaveBranchAsync(Editor);
            if (result.IsFailure)
            {
                _dialog.ShowError(result.Error ?? "Could not save branch.");
                return;
            }
            Editor.Id = result.Value;
            StatusMessage = "Branch saved.";
            await RefreshAsync();
        }
        finally { IsBusy = false; }
    }
}
