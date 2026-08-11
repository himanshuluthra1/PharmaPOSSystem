using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Counters;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels;

public sealed class CounterSelectViewModel : ObservableObject
{
    private readonly IBillingCounterService _counters;
    private readonly ICurrentUserService _currentUser;
    private readonly ICounterContextService _counterContext;
    private readonly IDialogService _dialog;

    private CounterPickDto? _selected;
    private decimal _openingFloat;
    private bool _isBusy;
    private string? _errorMessage;
    private bool _hasResumedSession;
    private bool _isSwitchMode;

    public CounterSelectViewModel(
        IBillingCounterService counters,
        ICurrentUserService currentUser,
        ICounterContextService counterContext,
        IDialogService dialog)
    {
        _counters = counters;
        _currentUser = currentUser;
        _counterContext = counterContext;
        _dialog = dialog;

        OpenCommand = new AsyncRelayCommand(OpenAsync, () => !IsBusy && SelectedCounter is not null);
        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
    }

    public ObservableCollection<CounterPickDto> Counters { get; } = new();

    public event Action? CounterSelected;

    /// <summary>When true, show the full list even if a session is already open (change counter).</summary>
    public bool IsSwitchMode
    {
        get => _isSwitchMode;
        set
        {
            if (SetProperty(ref _isSwitchMode, value))
            {
                OnPropertyChanged(nameof(WindowTitle));
                OnPropertyChanged(nameof(InstructionText));
                OnPropertyChanged(nameof(ConfirmButtonText));
            }
        }
    }

    public string WindowTitle => IsSwitchMode ? "Change billing counter" : "Select billing counter";

    public string InstructionText => IsSwitchMode
        ? $"Signed in as {OperatorName}. Pick the correct counter. Your previous counter session will be closed."
        : $"Signed in as {OperatorName}. Choose your counter so cash stays separate.";

    public string ConfirmButtonText => IsSwitchMode ? "Switch to this counter" : "Open counter";

    public bool HasResumedSession
    {
        get => _hasResumedSession;
        private set => SetProperty(ref _hasResumedSession, value);
    }

    public CounterPickDto? SelectedCounter
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public decimal OpeningFloat
    {
        get => _openingFloat;
        set => SetProperty(ref _openingFloat, value);
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

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string OperatorName => _currentUser.CurrentUser?.FullName ?? "Operator";

    public ICommand OpenCommand { get; }
    public ICommand RefreshCommand { get; }

    public Task InitializeAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        HasResumedSession = false;
        try
        {
            var user = _currentUser.CurrentUser
                ?? throw new InvalidOperationException("Not signed in.");

            if (!IsSwitchMode)
            {
                var existing = await _counters.GetOpenSessionForUserAsync(user.UserId);
                if (existing is not null)
                {
                    _counterContext.SetActiveSession(existing);
                    HasResumedSession = true;
                    return;
                }
            }

            var list = await _counters.ListForPickerAsync(user.BranchId);
            Counters.Clear();
            foreach (var c in list)
                Counters.Add(c);

            var currentId = _counterContext.ActiveCounterId;
            SelectedCounter = list.FirstOrDefault(c => c.Id == currentId)
                              ?? list.FirstOrDefault(c => c.IsDefault)
                              ?? list.FirstOrDefault(c => c.OpenSessionUserId == user.UserId)
                              ?? list.FirstOrDefault();

            if (IsSwitchMode && _counterContext.ActiveSession is { } active)
                OpeningFloat = active.OpeningFloat;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OpenAsync()
    {
        if (SelectedCounter is null) return;
        var user = _currentUser.CurrentUser;
        if (user is null) return;

        if (SelectedCounter.HasOpenSession && SelectedCounter.OpenSessionUserId != user.UserId)
        {
            ErrorMessage = $"Counter {SelectedCounter.Code} is open by {SelectedCounter.OpenOperatorName}.";
            return;
        }

        // Same counter already active — just keep it.
        if (IsSwitchMode
            && _counterContext.ActiveCounterId == SelectedCounter.Id
            && _counterContext.HasActiveCounter)
        {
            CounterSelected?.Invoke();
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _counters.OpenSessionAsync(SelectedCounter.Id, user.UserId, OpeningFloat);
            if (result.IsFailure || result.Value is null)
            {
                ErrorMessage = result.Error ?? "Could not open counter.";
                return;
            }

            _counterContext.SetActiveSession(result.Value);
            CounterSelected?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _dialog.ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
