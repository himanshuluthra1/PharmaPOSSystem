using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using PharmaPOS.Application.Features.Masters;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Settings;

public sealed class NewMedicineMappingRowViewModel : ObservableObject
{
    private readonly Action<NewMedicineMappingRowViewModel> _onSuggestChanged;
    private string _suggestText = string.Empty;
    private int? _mappedCatalogueMedicineId;
    private string? _mappedCatalogueName;
    private bool _isVerified;
    private bool _isDirty;
    private bool _suppressSuggest;
    private int _suggestionIndex = -1;

    public NewMedicineMappingRowViewModel(
        NewMedicineMappingRowDto dto,
        Action<NewMedicineMappingRowViewModel> onSuggestChanged)
    {
        MedicineId = dto.MedicineId;
        Name = dto.Name;
        GenericName = dto.GenericName;
        _isVerified = dto.IsVerified;
        _mappedCatalogueMedicineId = dto.MappedCatalogueMedicineId;
        _mappedCatalogueName = dto.MappedCatalogueName;
        _suggestText = dto.MappedCatalogueName ?? string.Empty;
        _onSuggestChanged = onSuggestChanged;
        SelectSuggestionCommand = new RelayCommand(
            p =>
            {
                if (p is NewMedicineMappingSuggestionDto s)
                    SelectSuggestion(s);
            });
    }

    public int MedicineId { get; }
    public string Name { get; }
    public string? GenericName { get; }

    public ObservableCollection<NewMedicineMappingSuggestionDto> Suggestions { get; } = new();

    public ICommand SelectSuggestionCommand { get; }

    public bool IsVerified
    {
        get => _isVerified;
        set => SetProperty(ref _isVerified, value);
    }

    public int? MappedCatalogueMedicineId
    {
        get => _mappedCatalogueMedicineId;
        private set => SetProperty(ref _mappedCatalogueMedicineId, value);
    }

    public string? MappedCatalogueName
    {
        get => _mappedCatalogueName;
        private set => SetProperty(ref _mappedCatalogueName, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetProperty(ref _isDirty, value))
            {
                OnPropertyChanged(nameof(CanSaveRow));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool CanSaveRow => IsDirty && MappedCatalogueMedicineId is > 0;

    public string SuggestText
    {
        get => _suggestText;
        set
        {
            if (!SetProperty(ref _suggestText, value)) return;
            if (_suppressSuggest) return;

            // Typing clears a prior selection until user picks again.
            if (MappedCatalogueMedicineId is not null
                && !string.Equals(value?.Trim(), MappedCatalogueName, StringComparison.OrdinalIgnoreCase))
            {
                MappedCatalogueMedicineId = null;
                MappedCatalogueName = null;
                IsDirty = true;
            }

            _onSuggestChanged(this);
            OnPropertyChanged(nameof(ShowSuggestions));
        }
    }

    public int SuggestionIndex
    {
        get => _suggestionIndex;
        set => SetProperty(ref _suggestionIndex, value);
    }

    public bool ShowSuggestions => Suggestions.Count > 0;

    public void SetSuggestions(IReadOnlyList<NewMedicineMappingSuggestionDto> items)
    {
        Suggestions.Clear();
        foreach (var item in items)
            Suggestions.Add(item);
        SuggestionIndex = items.Count > 0 ? 0 : -1;
        OnPropertyChanged(nameof(ShowSuggestions));
    }

    public void ClearSuggestions()
    {
        if (Suggestions.Count == 0) return;
        Suggestions.Clear();
        SuggestionIndex = -1;
        OnPropertyChanged(nameof(ShowSuggestions));
    }

    public void SelectSuggestion(NewMedicineMappingSuggestionDto suggestion)
    {
        _suppressSuggest = true;
        MappedCatalogueMedicineId = suggestion.MedicineId;
        MappedCatalogueName = suggestion.Name;
        SuggestText = suggestion.Name;
        _suppressSuggest = false;
        IsDirty = true;
        ClearSuggestions();
        OnPropertyChanged(nameof(CanSaveRow));
    }

    public void MoveSuggestion(int delta)
    {
        if (Suggestions.Count == 0)
        {
            SuggestionIndex = -1;
            return;
        }

        if (SuggestionIndex < 0)
            SuggestionIndex = 0;
        else
            SuggestionIndex = Math.Clamp(SuggestionIndex + delta, 0, Suggestions.Count - 1);
    }

    public void ConfirmSuggestion()
    {
        if (SuggestionIndex >= 0 && SuggestionIndex < Suggestions.Count)
            SelectSuggestion(Suggestions[SuggestionIndex]);
    }

    public void MarkSaved()
    {
        IsVerified = true;
        IsDirty = false;
    }
}

public sealed class NewMedicineMappingTabViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDialogService _dialog;
    private readonly Dictionary<int, CancellationTokenSource> _rowSuggestCts = new();
    private readonly SemaphoreSlim _suggestGate = new(1, 1);

    private bool _includeVerified;
    private bool _loaded;
    private bool _isBusy;
    private string _searchText = string.Empty;
    private string? _statusMessage;
    private CancellationTokenSource? _searchCts;

    public NewMedicineMappingTabViewModel(IServiceScopeFactory scopeFactory, IDialogService dialog)
    {
        _scopeFactory = scopeFactory;
        _dialog = dialog;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy && Rows.Any(r => r.CanSaveRow));
    }

    public ObservableCollection<NewMedicineMappingRowViewModel> Rows { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand SaveCommand { get; }

    public bool IncludeVerified
    {
        get => _includeVerified;
        set
        {
            if (SetProperty(ref _includeVerified, value) && _loaded)
                _ = RefreshAsync();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value) || !_loaded) return;
            _ = DebouncedRefreshAsync();
        }
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

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        _loaded = true;
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        IsBusy = true;
        StatusMessage = null;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<INewMedicineMappingService>();
            var list = await svc.ListRowsAsync(IncludeVerified, SearchText);
            Rows.Clear();
            foreach (var dto in list)
                Rows.Add(new NewMedicineMappingRowViewModel(dto, OnRowSuggestChanged));
            StatusMessage = $"{Rows.Count} medicine(s) with blank ImagePath.";
        }
        catch (Exception ex)
        {
            _dialog.ShowError(ex.Message, "New Medicine Mapping");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DebouncedRefreshAsync()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        try
        {
            await Task.Delay(300, token);
            if (!token.IsCancellationRequested)
                await RefreshAsync();
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
    }

    private void OnRowSuggestChanged(NewMedicineMappingRowViewModel row)
        => _ = SuggestForRowAsync(row);

    private async Task SuggestForRowAsync(NewMedicineMappingRowViewModel row)
    {
        if (_rowSuggestCts.TryGetValue(row.MedicineId, out var existing))
        {
            existing.Cancel();
            existing.Dispose();
        }

        var cts = new CancellationTokenSource();
        _rowSuggestCts[row.MedicineId] = cts;
        var token = cts.Token;
        var term = (row.SuggestText ?? string.Empty).Trim();

        if (term.Length < 3)
        {
            row.ClearSuggestions();
            return;
        }

        try
        {
            await Task.Delay(250, token);
            if (token.IsCancellationRequested) return;

            await _suggestGate.WaitAsync(token);
            try
            {
                if (token.IsCancellationRequested) return;
                using var scope = _scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<INewMedicineMappingService>();
                var items = await svc.SuggestCatalogueAsync(term, ct: token);
                if (token.IsCancellationRequested) return;
                row.SetSuggestions(items);
            }
            finally
            {
                _suggestGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private async Task SaveAsync()
    {
        var dirty = Rows.Where(r => r.CanSaveRow).ToList();
        if (dirty.Count == 0)
        {
            _dialog.ShowInfo("Nothing to save. Pick a catalogue match for at least one row.", "New Medicine Mapping");
            return;
        }

        IsBusy = true;
        try
        {
            var items = dirty
                .Select(r => new NewMedicineMappingSaveItem(r.MedicineId, r.MappedCatalogueMedicineId!.Value))
                .ToList();

            using var scope = _scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<INewMedicineMappingService>();
            var result = await svc.SaveAsync(items);
            if (!result.IsSuccess)
            {
                _dialog.ShowError(result.Error ?? "Save failed.", "New Medicine Mapping");
                return;
            }

            foreach (var row in dirty)
                row.MarkSaved();

            StatusMessage = $"Saved {dirty.Count} mapping(s).";
            if (!IncludeVerified)
                await RefreshAsync();
        }
        catch (Exception ex)
        {
            _dialog.ShowError(ex.Message, "New Medicine Mapping");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
