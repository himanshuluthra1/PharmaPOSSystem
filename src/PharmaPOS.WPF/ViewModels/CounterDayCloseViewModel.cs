using System.Windows.Input;
using PharmaPOS.Application.Features.Counters;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels;

public sealed class CounterDayCloseViewModel : ObservableObject
{
    private readonly IBillingCounterService _counters;
    private readonly IDialogService _dialog;
    private readonly IInvoicePrintService _print;

    private int _sessionId;
    private CounterDayCloseDto? _system;
    private decimal _countedCash;
    private string? _notes;
    private bool _isBusy;
    private string? _errorMessage;
    private bool _closed;

    public CounterDayCloseViewModel(
        IBillingCounterService counters,
        IDialogService dialog,
        IInvoicePrintService print)
    {
        _counters = counters;
        _dialog = dialog;
        _print = print;

        CloseAndPrintCommand = new AsyncRelayCommand(() => CloseAsync(printAfter: true, pdfAfter: false), () => CanClose);
        CloseAndPdfCommand = new AsyncRelayCommand(() => CloseAsync(printAfter: false, pdfAfter: true), () => CanClose);
        PreviewCommand = new RelayCommand(_ => Preview(), _ => SystemReport is not null && !IsBusy);
    }

    public event Action? ClosedSuccessfully;

    public ICommand CloseAndPrintCommand { get; }
    public ICommand CloseAndPdfCommand { get; }
    public ICommand PreviewCommand { get; }

    public CounterDayCloseDto? SystemReport
    {
        get => _system;
        private set
        {
            if (!SetProperty(ref _system, value)) return;
            OnPropertyChanged(nameof(HasReport));
            NotifyVariance();
        }
    }

    public bool HasReport => SystemReport is not null;

    public decimal CountedCash
    {
        get => _countedCash;
        set
        {
            if (!SetProperty(ref _countedCash, Math.Round(value, 2))) return;
            NotifyVariance();
        }
    }

    public string? Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool CanClose => !IsBusy && !_closed && SystemReport is not null && CountedCash >= 0;

    public decimal ExpectedCash => SystemReport?.ExpectedCashInDrawer ?? 0m;
    public decimal Variance => Math.Round(CountedCash - ExpectedCash, 2);
    public string VarianceLabel =>
        Variance > 0.009m ? "Excess" : Variance < -0.009m ? "Shortage" : "Matched";
    public string VarianceAmountText => $"₹ {Math.Abs(Variance):N2}";

    public async Task LoadAsync(int sessionId)
    {
        _sessionId = sessionId;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _counters.PreviewDayCloseAsync(sessionId);
            if (result.IsFailure || result.Value is null)
            {
                ErrorMessage = result.Error ?? "Could not load day-close figures.";
                SystemReport = null;
                return;
            }

            SystemReport = result.Value;
            CountedCash = result.Value.ExpectedCashInDrawer;
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

    private void Preview()
    {
        if (SystemReport is null) return;
        _print.ShowDayClosePreview(WithCounted(SystemReport));
    }

    private async Task CloseAsync(bool printAfter, bool pdfAfter)
    {
        if (SystemReport is null) return;
        if (!_dialog.Confirm(
                "This closes the counter session. New sales will need a counter opened again.\n\nContinue?",
                "Day close"))
            return;

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _counters.CloseDayAsync(_sessionId, CountedCash, Notes);
            if (result.IsFailure || result.Value is null)
            {
                ErrorMessage = result.Error ?? "Could not close the counter.";
                return;
            }

            _closed = true;
            var report = result.Value;
            if (printAfter)
                _print.PrintDayClose(report);
            if (pdfAfter)
            {
                var path = _print.ExportDayClosePdf(report);
                _dialog.ShowInfo($"PDF saved to:\n{path}", "Day close");
            }

            ClosedSuccessfully?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _dialog.ShowError(ex.Message, "Day close");
        }
        finally
        {
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private CounterDayCloseDto WithCounted(CounterDayCloseDto source) => new()
    {
        SessionId = source.SessionId,
        CompanyName = source.CompanyName,
        CounterCode = source.CounterCode,
        CounterName = source.CounterName,
        OperatorName = source.OperatorName,
        OpenedAtLocal = source.OpenedAtLocal,
        ClosedAtLocal = source.ClosedAtLocal,
        OpeningFloat = source.OpeningFloat,
        BillCount = source.BillCount,
        CashCollected = source.CashCollected,
        CardCollected = source.CardCollected,
        UpiCollected = source.UpiCollected,
        OtherCollected = source.OtherCollected,
        CreditCollected = source.CreditCollected,
        ExpectedCashInDrawer = source.ExpectedCashInDrawer,
        CountedCash = CountedCash,
        Remarks = Notes,
        MachineName = source.MachineName,
        IsClosed = source.IsClosed
    };

    private void NotifyVariance()
    {
        OnPropertyChanged(nameof(ExpectedCash));
        OnPropertyChanged(nameof(Variance));
        OnPropertyChanged(nameof(VarianceLabel));
        OnPropertyChanged(nameof(VarianceAmountText));
        CommandManager.InvalidateRequerySuggested();
    }
}
