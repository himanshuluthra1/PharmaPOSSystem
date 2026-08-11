using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Counters;
using PharmaPOS.Domain.Enums;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Settings;

public class CountersTabViewModel : ObservableObject
{
    private readonly IBillingCounterService _counters;
    private readonly ICurrentUserService _currentUser;
    private readonly IDialogService _dialog;
    private BillingCounterListDto? _selected;
    private BillingCounterDetailDto _editor = new();
    private bool _isBusy;
    private bool _loaded;
    private string? _statusMessage;
    private string? _cashSummaryText;

    public CountersTabViewModel(
        IBillingCounterService counters,
        ICurrentUserService currentUser,
        IDialogService dialog)
    {
        _counters = counters;
        _currentUser = currentUser;
        _dialog = dialog;
        NewCommand = new RelayCommand(_ => BeginNew());
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        RefreshCashCommand = new AsyncRelayCommand(RefreshCashAsync, () => !IsBusy);
    }

    public ObservableCollection<BillingCounterListDto> Items { get; } = new();
    public Array EntityStatuses => Enum.GetValues(typeof(EntityStatus));

    public BillingCounterListDto? SelectedItem
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value) && value is not null)
                _ = LoadItemAsync(value.Id);
        }
    }

    public BillingCounterDetailDto Editor
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

    public string EditorTitle => Editor.Id > 0 ? $"Edit: {Editor.Name}" : "New Counter";
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

    public string? CashSummaryText
    {
        get => _cashSummaryText;
        private set => SetProperty(ref _cashSummaryText, value);
    }

    public ICommand NewCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand RefreshCashCommand { get; }

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        _loaded = true;
        await RefreshAsync();
        await RefreshCashAsync();
    }

    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var branchId = _currentUser.CurrentUser?.BranchId;
            await _counters.EnsureDefaultCountersAsync(branchId);
            var rows = await _counters.ListAsync(branchId);
            Items.Clear();
            foreach (var r in rows) Items.Add(r);
        }
        finally { IsBusy = false; }
    }

    private async Task RefreshCashAsync()
    {
        try
        {
            var rows = await _counters.GetCashSummaryAsync(_currentUser.CurrentUser?.BranchId, DateTime.Today);
            CashSummaryText = rows.Count == 0
                ? "No counters yet."
                : string.Join("\n", rows.Select(r =>
                    $"{r.CounterCode}: bills {r.BillCount}, cash ₹{r.CashCollected:N0}, drawer ₹{r.ExpectedCashInDrawer:N0}" +
                    (r.OperatorName is null ? "" : $" · {r.OperatorName}")));
        }
        catch (Exception ex)
        {
            CashSummaryText = ex.Message;
        }
    }

    private async Task LoadItemAsync(int id)
    {
        var detail = await _counters.GetAsync(id);
        if (detail is not null) Editor = detail;
    }

    private void BeginNew()
    {
        SelectedItem = null;
        Editor = new BillingCounterDetailDto
        {
            BranchId = _currentUser.CurrentUser?.BranchId,
            Status = EntityStatus.Active
        };
    }

    private async Task SaveAsync()
    {
        IsBusy = true;
        try
        {
            if (Editor.BranchId is null)
                Editor.BranchId = _currentUser.CurrentUser?.BranchId;

            var result = await _counters.SaveAsync(Editor);
            if (result.IsFailure)
            {
                _dialog.ShowError(result.Error ?? "Could not save counter.");
                return;
            }

            Editor.Id = result.Value;
            StatusMessage = "Counter saved.";
            await RefreshAsync();
            await RefreshCashAsync();
        }
        finally { IsBusy = false; }
    }
}
