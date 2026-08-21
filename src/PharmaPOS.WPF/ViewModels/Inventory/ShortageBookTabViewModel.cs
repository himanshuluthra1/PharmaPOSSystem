using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.ShortageBook;
using PharmaPOS.Domain.Enums;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Inventory;

public sealed class ShortageBookTabViewModel : ObservableObject
{
    private readonly IShortageBookService _shortageBook;
    private readonly IMedicinePickerService _picker;
    private readonly IDialogService _dialog;
    private readonly int? _branchId;
    private readonly string? _recordedBy;

    private string _searchText = string.Empty;
    private ShortageStatusFilterOption _selectedFilter;
    private ShortageBookListItemDto? _selectedItem;
    private bool _isBusy;
    private string? _statusMessage;
    private CancellationTokenSource? _searchCts;

    public ShortageBookTabViewModel(
        IShortageBookService shortageBook,
        IMedicinePickerService picker,
        ICurrentUserService currentUser,
        IDialogService dialog)
    {
        _shortageBook = shortageBook;
        _picker = picker;
        _dialog = dialog;
        _branchId = currentUser.CurrentUser?.BranchId;
        _recordedBy = currentUser.CurrentUser?.FullName ?? currentUser.CurrentUser?.Username;

        FilterOptions =
        [
            new(null, "All"),
            new(ShortageStatus.Open, "Open"),
            new(ShortageStatus.Ordered, "Ordered"),
            new(ShortageStatus.Fulfilled, "Fulfilled"),
            new(ShortageStatus.Cancelled, "Cancelled")
        ];
        _selectedFilter = FilterOptions[1];

        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync(), _ => !IsBusy);
        RecordManualCommand = new AsyncRelayCommand(_ => RecordManualAsync(), _ => !IsBusy);
        CancelEntryCommand = new AsyncRelayCommand(
            _ => CancelSelectedAsync(),
            _ => !IsBusy && SelectedItem is { Status: ShortageStatus.Open or ShortageStatus.Ordered });

        _ = RefreshAsync();
    }

    public IReadOnlyList<ShortageStatusFilterOption> FilterOptions { get; }
    public ObservableCollection<ShortageBookListItemDto> Items { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                _ = DebouncedRefreshAsync();
        }
    }

    public ShortageStatusFilterOption SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (SetProperty(ref _selectedFilter, value))
                _ = RefreshAsync();
        }
    }

    public ShortageBookListItemDto? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
                CommandManager.InvalidateRequerySuggested();
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

    public ICommand RefreshCommand { get; }
    public ICommand RecordManualCommand { get; }
    public ICommand CancelEntryCommand { get; }

    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var rows = await _shortageBook.ListAsync(
                new ShortageBookFilter(SelectedFilter.Status, SearchText, 300),
                _branchId);
            Items.Clear();
            foreach (var row in rows)
                Items.Add(row);

            StatusMessage = rows.Count == 0
                ? "No shortage entries."
                : $"{rows.Count} shortage entr{(rows.Count == 1 ? "y" : "ies")}.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
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
            await Task.Delay(280, token);
            await RefreshAsync();
        }
        catch (OperationCanceledException) { }
    }

    private async Task RecordManualAsync()
    {
        var medicine = await _picker.PickMedicineLookupAsync();
        if (medicine is null) return;

        var onHand = await _shortageBook.GetOnHandQuantityAsync(medicine.Id, _branchId);
        var requested = Math.Max(1m, onHand > 0 ? onHand + 1 : 1m);

        if (!_dialog.Confirm(
                $"Record shortage for \"{medicine.Name}\"?\n\nOn hand: {onHand:0.##}\nRequested (lost sale): {requested:0.##}",
                "Shortage book"))
            return;

        IsBusy = true;
        try
        {
            var result = await _shortageBook.RecordAsync(
                new RecordShortageRequest(medicine.Id, requested, onHand, ShortageSource.Manual),
                _branchId,
                _recordedBy);

            if (result.IsFailure)
            {
                _dialog.ShowError(result.Error ?? "Could not record shortage.");
                return;
            }

            StatusMessage = $"Recorded shortage for {medicine.Name}.";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _dialog.ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CancelSelectedAsync()
    {
        if (SelectedItem is null) return;
        if (!_dialog.Confirm($"Cancel shortage for \"{SelectedItem.MedicineName}\"?", "Shortage book"))
            return;

        IsBusy = true;
        try
        {
            var result = await _shortageBook.CancelAsync(SelectedItem.Id, _branchId);
            if (result.IsFailure)
            {
                _dialog.ShowError(result.Error ?? "Could not cancel.");
                return;
            }

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _dialog.ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public sealed record ShortageStatusFilterOption(ShortageStatus? Status, string Label)
{
    public override string ToString() => Label;
}
