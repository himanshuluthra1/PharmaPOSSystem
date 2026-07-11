using System.Collections.ObjectModel;

using System.Windows.Input;

using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PharmaPOS.Application.Common;
using PharmaPOS.Application.Features.Masters;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;
using PharmaPOS.WPF.Views;



namespace PharmaPOS.WPF.ViewModels.Settings;



public class MedicineMappingTabViewModel : ObservableObject

{

    private readonly IServiceScopeFactory _scopeFactory;

    private readonly IDialogService _dialog;

    private readonly HashSet<int> _pendingMedWinIds = new();

    private readonly SemaphoreSlim _oneMgLoadGate = new(1, 1);

    private readonly SemaphoreSlim _medWinLoadGate = new(1, 1);

    private CancellationTokenSource? _medWinSearchCts;

    private bool _suppressMedWinSelection;

    private string _oneMgSearchText = string.Empty;

    private string _medWinSearchText = string.Empty;

    private bool _includeMatched = true;

    private MedicineMappingListItemDto? _selectedOneMg;

    private MedicineMappingListItemDto? _selectedMedWin;
    private PendingMedicineMappingDto? _selectedPendingMapping;

    private bool _isOneMgSearching;

    private bool _isMedWinSearching;

    private bool _isApplyingMappings;

    private bool _isAutoMapping;

    private bool _loaded;

    private bool _suppressOneMgSearchReload;

    private string? _statusMessage;
    private CancellationTokenSource? _appliedMappingsFilterCts;
    private string _appliedMappingsFilter = string.Empty;
    private string? _appliedMappingsStatus;



    public MedicineMappingTabViewModel(IServiceScopeFactory scopeFactory, IDialogService dialog)

    {

        _scopeFactory = scopeFactory;

        _dialog = dialog;

        MapCommand = new RelayCommand(_ => QueueMapping(), _ => CanMap);

        AutoMapCommand = new AsyncRelayCommand(AutoMapAsync, () => CanAutoMap);

        ApplyMappingsCommand = new AsyncRelayCommand(ApplyPendingMappingsAsync, () => CanApplyMappings);

        ClearPendingCommand = new RelayCommand(_ => ClearPendingMappings(), _ => PendingMappings.Count > 0);
        ShowQueueCommand = new RelayCommand(_ => ShowPendingQueue());
        ShowAppliedMappingsCommand = new AsyncRelayCommand(ShowAppliedMappingsAsync);
        RemovePendingMappingCommand = new RelayCommand(RemovePendingMapping, _ => !IsApplyingMappings);
        RefreshMedWinCommand = new AsyncRelayCommand(() => LoadUnmatchedMedWinAsync());

        PendingMappings.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasPendingMappings));
            OnPropertyChanged(nameof(PendingQueueLabel));
        };

    }



    public ObservableCollection<MedicineMappingListItemDto> OneMgItems { get; } = new();

    public ObservableCollection<MedicineMappingListItemDto> MedWinItems { get; } = new();

    public ObservableCollection<PendingMedicineMappingDto> PendingMappings { get; } = new();
    public ObservableCollection<MedicineMedWinMappingDto> AppliedMappings { get; } = new();



    public string OneMgSearchText

    {

        get => _oneMgSearchText;

        set

        {

            if (!SetProperty(ref _oneMgSearchText, value)) return;

            if (!_suppressOneMgSearchReload && SelectedMedWin is not null)

                _ = LoadOneMgForSelectedMedWinAsync();

        }

    }



    public string MedWinSearchText

    {

        get => _medWinSearchText;

        set

        {

            if (SetProperty(ref _medWinSearchText, value))

                _ = DebouncedLoadUnmatchedMedWinAsync();

        }

    }



    public bool IncludeMatched

    {

        get => _includeMatched;

        set

        {

            if (SetProperty(ref _includeMatched, value) && SelectedMedWin is not null)

                _ = LoadOneMgForSelectedMedWinAsync();

        }

    }



    public MedicineMappingListItemDto? SelectedOneMg

    {

        get => _selectedOneMg;

        set

        {

            if (SetProperty(ref _selectedOneMg, value))

                NotifyMapCommandsChanged();

        }

    }



    public MedicineMappingListItemDto? SelectedMedWin

    {

        get => _selectedMedWin;

        set

        {

            if (!SetProperty(ref _selectedMedWin, value)) return;



            _suppressOneMgSearchReload = true;

            _oneMgSearchText = string.Empty;

            OnPropertyChanged(nameof(OneMgSearchText));

            _suppressOneMgSearchReload = false;



            NotifyMapCommandsChanged();

            if (!_suppressMedWinSelection)

                _ = LoadOneMgForSelectedMedWinAsync();

        }

    }



    public bool CanMap => !IsApplyingMappings

                          && !IsAutoMapping

                          && !IsOneMgSearching

                          && !IsMedWinSearching

                          && SelectedOneMg is not null

                          && SelectedMedWin is not null

                          && !SelectedMedWin.IsMatched

                          && !_pendingMedWinIds.Contains(SelectedMedWin.Id);



    public bool CanAutoMap => !IsApplyingMappings

                              && !IsAutoMapping

                              && !IsOneMgSearching

                              && !IsMedWinSearching;



    public bool CanApplyMappings => !IsApplyingMappings && PendingMappings.Count > 0;

    public bool HasPendingMappings => PendingMappings.Count > 0;

    public string PendingQueueLabel =>
        PendingMappings.Count > 0 ? $"View Queue ({PendingMappings.Count})" : "View Queue";



    public bool IsOneMgSearching

    {

        get => _isOneMgSearching;

        private set

        {

            if (SetProperty(ref _isOneMgSearching, value))

                NotifyMapCommandsChanged();

        }

    }



    public bool IsMedWinSearching

    {

        get => _isMedWinSearching;

        private set

        {

            if (SetProperty(ref _isMedWinSearching, value))

                NotifyMapCommandsChanged();

        }

    }



    public bool IsApplyingMappings

    {

        get => _isApplyingMappings;

        private set

        {

            if (SetProperty(ref _isApplyingMappings, value))

                NotifyMapCommandsChanged();

        }

    }



    public bool IsAutoMapping

    {

        get => _isAutoMapping;

        private set

        {

            if (SetProperty(ref _isAutoMapping, value))

                NotifyMapCommandsChanged();

        }

    }



    public string? StatusMessage

    {

        get => _statusMessage;

        private set => SetProperty(ref _statusMessage, value);

    }



    public ICommand MapCommand { get; }

    public ICommand AutoMapCommand { get; }

    public ICommand ApplyMappingsCommand { get; }

    public ICommand ClearPendingCommand { get; }

    public ICommand ShowQueueCommand { get; }

    public ICommand ShowAppliedMappingsCommand { get; }

    public ICommand RemovePendingMappingCommand { get; }

    public ICommand RefreshMedWinCommand { get; }

    public PendingMedicineMappingDto? SelectedPendingMapping
    {
        get => _selectedPendingMapping;
        set => SetProperty(ref _selectedPendingMapping, value);
    }

    public string AppliedMappingsFilter
    {
        get => _appliedMappingsFilter;
        set
        {
            if (SetProperty(ref _appliedMappingsFilter, value))
                _ = DebouncedLoadAppliedMappingsAsync();
        }
    }

    public string? AppliedMappingsStatus
    {
        get => _appliedMappingsStatus;
        private set => SetProperty(ref _appliedMappingsStatus, value);
    }



    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        _loaded = true;

        _ = RunMappingBackfillAsync();
        await LoadUnmatchedMedWinAsync();
    }

    private async Task RunMappingBackfillAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var backfill = scope.ServiceProvider.GetRequiredService<IMedicineMedWinMappingBackfillService>();
            await backfill.EnsureBackfilledAsync();
        }
        catch
        {
            // Non-fatal: mapping UI still works from legacy Notes until backfill succeeds.
        }
    }



    private async Task DebouncedLoadUnmatchedMedWinAsync()

    {

        _medWinSearchCts?.Cancel();

        _medWinSearchCts?.Dispose();

        _medWinSearchCts = new CancellationTokenSource();

        var token = _medWinSearchCts.Token;

        try

        {

            await Task.Delay(300, token);

            await LoadUnmatchedMedWinAsync(token);

        }

        catch (OperationCanceledException) { }

    }



    private async Task LoadUnmatchedMedWinAsync(CancellationToken ct = default)

    {

        await _medWinLoadGate.WaitAsync(ct);

        try

        {

            await LoadUnmatchedMedWinCoreAsync(ct);

        }

        finally

        {

            _medWinLoadGate.Release();

        }

    }



    private async Task LoadUnmatchedMedWinCoreAsync(CancellationToken ct = default)

    {

        var preserveId = SelectedMedWin?.Id;



        try

        {

            IsMedWinSearching = true;

            using var scope = _scopeFactory.CreateScope();

            var mapping = scope.ServiceProvider.GetRequiredService<IMedicineMappingService>();

            var items = await mapping.ListUnmatchedMedWinMedicinesAsync(MedWinSearchText, ct);

            if (ct.IsCancellationRequested) return;



            items = items.Where(i => !_pendingMedWinIds.Contains(i.Id)).ToList();



            _suppressMedWinSelection = true;

            try

            {

                MedWinItems.Clear();

                foreach (var item in items)

                    MedWinItems.Add(item);

            }

            finally

            {

                _suppressMedWinSelection = false;

            }



            var listHint = string.IsNullOrWhiteSpace(MedWinSearchText) && MedWinItems.Count >= 500

                ? " (first 500 — type to search)"

                : string.Empty;

            var pendingHint = PendingMappings.Count > 0

                ? $" {PendingMappings.Count} queued for apply."

                : string.Empty;



            if (preserveId is int id)

                RestoreMedWinSelection(id);

            else

            {

                _selectedMedWin = null;

                OnPropertyChanged(nameof(SelectedMedWin));

                OneMgItems.Clear();

                SelectedOneMg = null;

                StatusMessage = MedWinItems.Count > 0

                    ? $"{MedWinItems.Count} unmatched MedWin medicine(s){listHint}.{pendingHint}"

                    : pendingHint.Length > 0 ? pendingHint.Trim() : null;

            }

        }

        catch (Exception ex)

        {

            _dialog.ShowError($"MedWin list failed: {ex.Message}");

        }

        finally

        {

            IsMedWinSearching = false;

        }

    }



    private void RestoreMedWinSelection(int? preserveId)

    {

        if (preserveId is null)

        {

            if (SelectedMedWin is not null)

            {

                _selectedMedWin = null;

                OnPropertyChanged(nameof(SelectedMedWin));

                OneMgItems.Clear();

                SelectedOneMg = null;

                StatusMessage = null;

            }



            return;

        }



        var restored = MedWinItems.FirstOrDefault(i => i.Id == preserveId.Value);

        if (restored is null)

        {

            _selectedMedWin = null;

            OnPropertyChanged(nameof(SelectedMedWin));

            OneMgItems.Clear();

            SelectedOneMg = null;

            StatusMessage = null;

            return;

        }



        if (SelectedMedWin?.Id == restored.Id) return;



        _selectedMedWin = restored;

        OnPropertyChanged(nameof(SelectedMedWin));

        if (!_suppressMedWinSelection)

            _ = LoadOneMgForSelectedMedWinAsync();

    }



    private async Task LoadOneMgForSelectedMedWinAsync()

    {

        await _oneMgLoadGate.WaitAsync();

        try

        {

            var medWin = SelectedMedWin;

            if (medWin is null)

            {

                OneMgItems.Clear();

                SelectedOneMg = null;

                StatusMessage = PendingMappings.Count > 0

                    ? $"{PendingMappings.Count} mapping(s) queued — click Apply Mappings to save."

                    : null;

                return;

            }



            var brandPrefix = MedicineMappingHelper.GetFirstWordPrefix(medWin.Name);

            if (string.IsNullOrWhiteSpace(brandPrefix))

            {

                OneMgItems.Clear();

                SelectedOneMg = null;

                StatusMessage = "Could not derive a brand prefix from the selected MedWin name.";

                return;

            }



            var narrowTerm = OneMgSearchText;

            var includeMatched = IncludeMatched;



            IsOneMgSearching = true;

            using var scope = _scopeFactory.CreateScope();

            var mapping = scope.ServiceProvider.GetRequiredService<IMedicineMappingService>();

            var items = await mapping.SearchOneMgByBrandPrefixAsync(

                brandPrefix, narrowTerm, includeMatched);



            if (SelectedMedWin?.Id != medWin.Id)

                return;



            OneMgItems.Clear();

            foreach (var item in items)

                OneMgItems.Add(item);

            SelectedOneMg = OneMgItems.FirstOrDefault();

            StatusMessage = $"Showing {OneMgItems.Count} OneMG match(es) for brand prefix \"{brandPrefix}\".";

        }

        catch (Exception ex)

        {

            _dialog.ShowError($"OneMG suggestions failed: {ex.Message}");

        }

        finally

        {

            IsOneMgSearching = false;

            _oneMgLoadGate.Release();

        }

    }



    private void QueueMapping()

    {

        if (SelectedOneMg is null || SelectedMedWin is null) return;

        if (_pendingMedWinIds.Contains(SelectedMedWin.Id)) return;



        var medWin = SelectedMedWin;

        var oneMg = SelectedOneMg;



        PendingMappings.Add(new PendingMedicineMappingDto(

            medWin.Id, medWin.Name, medWin.ExternalId,

            oneMg.Id, oneMg.Name, oneMg.ExternalId, oneMg.PackInfo));

        _pendingMedWinIds.Add(medWin.Id);



        MedWinItems.Remove(medWin);

        _selectedMedWin = MedWinItems.FirstOrDefault();

        OnPropertyChanged(nameof(SelectedMedWin));

        OneMgItems.Clear();

        SelectedOneMg = null;



        if (SelectedMedWin is not null)

            _ = LoadOneMgForSelectedMedWinAsync();



        StatusMessage = $"{PendingMappings.Count} mapping(s) queued — click Apply Mappings to save to the database.";

        NotifyMapCommandsChanged();

    }



    private async Task AutoMapAsync()

    {

        if (!_dialog.Confirm(

                "Scan all unmatched MedWin medicines and queue confident OneMG matches?\n\n" +

                "Matches use brand prefix, strength, name, and salt rules. " +

                "Review the queue before clicking Apply Mappings.",

                "Auto Map"))

            return;



        try

        {

            IsAutoMapping = true;

            StatusMessage = "Auto-mapping unmatched medicines...";



            using var scope = _scopeFactory.CreateScope();

            var mapping = scope.ServiceProvider.GetRequiredService<IMedicineMappingService>();

            var result = await mapping.SuggestAutoMappingsAsync(IncludeMatched, _pendingMedWinIds);



            var queued = 0;

            foreach (var suggestion in result.Suggestions)

            {

                if (_pendingMedWinIds.Contains(suggestion.MedWinMedicineId))

                    continue;



                PendingMappings.Add(new PendingMedicineMappingDto(

                    suggestion.MedWinMedicineId,

                    suggestion.MedWinName,

                    suggestion.MedWinExternalId,

                    suggestion.OneMgMedicineId,

                    suggestion.OneMgName,

                    suggestion.OneMgExternalId,

                    suggestion.OneMgPackInfo));

                _pendingMedWinIds.Add(suggestion.MedWinMedicineId);



                var medWinItem = MedWinItems.FirstOrDefault(i => i.Id == suggestion.MedWinMedicineId);

                if (medWinItem is not null)

                    MedWinItems.Remove(medWinItem);



                queued++;

            }



            if (SelectedMedWin is not null && _pendingMedWinIds.Contains(SelectedMedWin.Id))

            {

                _selectedMedWin = MedWinItems.FirstOrDefault();

                OnPropertyChanged(nameof(SelectedMedWin));

                OneMgItems.Clear();

                SelectedOneMg = null;

            }



            if (SelectedMedWin is not null)

                _ = LoadOneMgForSelectedMedWinAsync();



            StatusMessage =

                $"Auto-mapped {queued} medicine(s) to the queue. " +

                $"{result.SkippedAmbiguous} ambiguous, {result.SkippedNoCandidates} had no OneMG match, " +

                $"{result.SkippedNoPrefix} had no brand prefix.";



            if (queued > 0)

            {

                _dialog.ShowInfo(

                    $"Queued {queued} mapping(s). Open View Queue to review, then click Apply Mappings to save.",

                    "Auto Map complete");

            }

            else

            {

                _dialog.ShowInfo(

                    "No confident matches were found. Try adjusting filters or map medicines manually.",

                    "Auto Map complete");

            }



            NotifyMapCommandsChanged();

        }

        catch (Exception ex)

        {

            _dialog.ShowError($"Auto Map failed: {ex.Message}");

        }

        finally

        {

            IsAutoMapping = false;

        }

    }



    private async Task ApplyPendingMappingsAsync()
    {
        if (PendingMappings.Count == 0) return;

        var mappingLines = PendingMappings
            .Select(p => $"• {p.MedWinName} (MedWin {p.MedWinExternalId}) → {p.OneMgName} (OneMG {p.OneMgExternalId})")
            .ToList();

        var confirmBody =
            $"Save {PendingMappings.Count} medicine mapping(s) to the database?\n\n" +
            string.Join("\n", mappingLines) +
            "\n\nStock, sales, and purchase history on each MedWin row will move to the linked OneMG row.";

        if (!_dialog.Confirm(confirmBody, "Apply mappings"))
            return;

        try
        {
            IsApplyingMappings = true;
            StatusMessage = $"Applying {PendingMappings.Count} mapping(s)...";

            var pairs = PendingMappings
                .Select(p => new MedicineMappingPair(p.OneMgMedicineId, p.MedWinMedicineId))
                .ToList();

            using var scope = _scopeFactory.CreateScope();
            var mapping = scope.ServiceProvider.GetRequiredService<IMedicineMappingService>();
            var result = await mapping.MapMedWinToOneMgBatchAsync(pairs);

            if (!result.IsSuccess)
            {
                var firstError = result.Errors.FirstOrDefault()?.Message ?? "Apply mappings failed.";
                _dialog.ShowError(
                    result.Errors.Count == 1
                        ? firstError
                        : $"{result.Errors.Count} mapping(s) failed. First error: {firstError}");
                StatusMessage = $"{PendingMappings.Count} mapping(s) still queued — fix issues and try again.";
                return;
            }

            PendingMappings.Clear();
            _pendingMedWinIds.Clear();
            StatusMessage = $"Applied {result.Succeeded} mapping(s) successfully.";
            NotifyMapCommandsChanged();
        }
        catch (Exception ex)
        {
            _dialog.ShowError($"Apply mappings failed: {ex.Message}");
            StatusMessage = $"{PendingMappings.Count} mapping(s) still queued.";
        }
        finally
        {
            IsApplyingMappings = false;
        }
    }



    private void ClearPendingMappings()
    {
        if (PendingMappings.Count == 0) return;

        foreach (var pending in PendingMappings.OrderBy(p => p.MedWinName).ToList())
            RestorePendingToMedWinList(pending);

        PendingMappings.Clear();
        _pendingMedWinIds.Clear();
        StatusMessage = "Queued mappings cleared.";
        NotifyMapCommandsChanged();
    }

    private void ShowPendingQueue()
    {
        if (PendingMappings.Count == 0)
        {
            _dialog.ShowInfo("No mappings are queued.", "Queued mappings");
            return;
        }

        var window = new PendingMappingsWindow(this) { Owner = System.Windows.Application.Current?.MainWindow };
        window.ShowDialog();
    }

    private async Task ShowAppliedMappingsAsync()
    {
        await LoadAppliedMappingsAsync();
        var window = new AppliedMappingsWindow
        {
            DataContext = this,
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.ShowDialog();
    }

    private async Task DebouncedLoadAppliedMappingsAsync()
    {
        _appliedMappingsFilterCts?.Cancel();
        _appliedMappingsFilterCts?.Dispose();
        _appliedMappingsFilterCts = new CancellationTokenSource();
        var token = _appliedMappingsFilterCts.Token;
        try
        {
            await Task.Delay(300, token);
            await LoadAppliedMappingsAsync(token);
        }
        catch (OperationCanceledException) { }
    }

    private async Task LoadAppliedMappingsAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var mapping = scope.ServiceProvider.GetRequiredService<IMedicineMappingService>();
        var rows = await mapping.ListAppliedMappingsAsync(AppliedMappingsFilter, ct: ct);
        if (ct.IsCancellationRequested) return;

        AppliedMappings.Clear();
        foreach (var row in rows)
            AppliedMappings.Add(row);

        AppliedMappingsStatus = rows.Count >= 500
            ? $"Showing first {rows.Count} mappings — narrow the filter to search."
            : $"{rows.Count} saved mapping(s).";
    }

    private void RemovePendingMapping(object? parameter)
    {
        if (parameter is not PendingMedicineMappingDto pending)
            return;

        RestorePendingToMedWinList(pending);
        PendingMappings.Remove(pending);
        _pendingMedWinIds.Remove(pending.MedWinMedicineId);
        SelectedPendingMapping = null;

        StatusMessage = PendingMappings.Count > 0
            ? $"{PendingMappings.Count} mapping(s) queued — click Apply Mappings to save to the database."
            : null;
        NotifyMapCommandsChanged();
    }

    private void RestorePendingToMedWinList(PendingMedicineMappingDto pending)
    {
        if (MedWinItems.Any(i => i.Id == pending.MedWinMedicineId))
            return;

        MedWinItems.Add(new MedicineMappingListItemDto(
            pending.MedWinMedicineId,
            pending.MedWinName,
            null,
            null,
            pending.MedWinExternalId,
            false));

        var sorted = MedWinItems.OrderBy(i => i.Name).ToList();
        MedWinItems.Clear();
        foreach (var item in sorted)
            MedWinItems.Add(item);
    }

    private void NotifyMapCommandsChanged()
    {
        OnPropertyChanged(nameof(CanMap));
        OnPropertyChanged(nameof(CanAutoMap));
        OnPropertyChanged(nameof(CanApplyMappings));
        OnPropertyChanged(nameof(HasPendingMappings));
        OnPropertyChanged(nameof(PendingQueueLabel));
        CommandManager.InvalidateRequerySuggested();
    }
}


