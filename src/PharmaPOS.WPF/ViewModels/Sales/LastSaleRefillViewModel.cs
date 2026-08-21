using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Features.Sales;
using PharmaPOS.Application.Features.ShortageBook;
using PharmaPOS.Domain.Enums;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Sales;

public sealed class LastSaleRefillLineViewModel : ObservableObject
{
    private bool _isSelected;
    private decimal _refillQty;

    public LastSaleRefillLineViewModel(LastSaleRefillLineDto line)
    {
        Line = line;
        _isSelected = line.HasStock;
        _refillQty = line.HasStock
            ? Math.Min(line.LastQuantity, line.AvailableStock)
            : line.LastQuantity;
        AddCommand = new RelayCommand(_ => AddRequested?.Invoke(this), _ => Line.HasStock && RefillQty > 0);
        LogShortageCommand = new RelayCommand(
            _ => LogShortageRequested?.Invoke(this),
            _ => !Line.HasStock && Line.LastQuantity > 0);
    }

    public LastSaleRefillLineDto Line { get; }

    public event Action<LastSaleRefillLineViewModel>? AddRequested;
    public event Action<LastSaleRefillLineViewModel>? LogShortageRequested;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public decimal RefillQty
    {
        get => _refillQty;
        set
        {
            var qty = Math.Max(0, Math.Round(value, 2));
            if (Line.HasStock && qty > Line.AvailableStock)
                qty = Line.AvailableStock;
            if (SetProperty(ref _refillQty, qty))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public ICommand AddCommand { get; }
    public ICommand LogShortageCommand { get; }

    public bool ShowShortageAction => !Line.HasStock;
}

public sealed class LastSaleRefillViewModel : ObservableObject
{
    private readonly ISalesService _sales;
    private readonly IShortageBookService _shortageBook;
    private readonly int? _branchId;
    private readonly string? _recordedBy;
    private readonly IDialogService _dialog;

    private string _searchText = string.Empty;
    private LastSalePatientMatchDto? _selectedMatch;
    private LastSaleRefillDto? _refill;
    private string? _statusMessage;
    private bool _isBusy;
    private CancellationTokenSource? _searchCts;

    public LastSaleRefillViewModel(
        ISalesService sales,
        IShortageBookService shortageBook,
        int? branchId,
        string? recordedBy,
        IDialogService dialog)
    {
        _sales = sales;
        _shortageBook = shortageBook;
        _branchId = branchId;
        _recordedBy = recordedBy;
        _dialog = dialog;

        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsBusy && SearchText.Trim().Length >= 2);
        AddAllCommand = new RelayCommand(
            _ => AddAllSelected(),
            _ => Lines.Any(l => l.IsSelected && l.Line.HasStock && l.RefillQty > 0));
        ConfirmCommand = new RelayCommand(_ => Confirm(), _ => PendingAdds.Count > 0 || Lines.Any(l => l.IsSelected && l.Line.HasStock));
    }

    public ObservableCollection<LastSalePatientMatchDto> Matches { get; } = new();
    public ObservableCollection<LastSaleRefillLineViewModel> Lines { get; } = new();

    /// <summary>Lines queued for one-tap add before closing, or accumulated from Add buttons.</summary>
    public List<(LastSaleRefillLineDto Line, decimal Qty)> PendingAdds { get; } = new();

    public event Action? RequestClose;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                _ = DebouncedSearchAsync();
        }
    }

    public LastSalePatientMatchDto? SelectedMatch
    {
        get => _selectedMatch;
        set
        {
            if (!SetProperty(ref _selectedMatch, value) || value is null) return;
            _ = LoadRefillAsync(value.SaleId);
        }
    }

    public LastSaleRefillDto? Refill
    {
        get => _refill;
        private set
        {
            if (!SetProperty(ref _refill, value)) return;
            OnPropertyChanged(nameof(HasRefill));
            OnPropertyChanged(nameof(HeaderText));
        }
    }

    public bool HasRefill => Refill is not null;

    public string HeaderText => Refill is null
        ? "Type a patient name or mobile to find their last bill."
        : $"Last bill {Refill.InvoiceNumber} · {Refill.InvoiceDateLabel}";

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
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

    public bool Confirmed { get; private set; }

    public ICommand SearchCommand { get; }
    public ICommand AddAllCommand { get; }
    public ICommand ConfirmCommand { get; }

    public Task InitializeAsync(string? seedPatient)
    {
        if (!string.IsNullOrWhiteSpace(seedPatient))
            SearchText = seedPatient.Trim();
        return Task.CompletedTask;
    }

    private async Task DebouncedSearchAsync()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        try
        {
            await Task.Delay(280, token);
            await SearchAsync();
        }
        catch (OperationCanceledException) { }
    }

    private async Task SearchAsync()
    {
        var term = SearchText.Trim();
        if (term.Length < 2)
        {
            Matches.Clear();
            StatusMessage = "Type at least 2 characters.";
            return;
        }

        IsBusy = true;
        try
        {
            var rows = await _sales.SearchLastSalesByPatientAsync(term, _branchId);
            Matches.Clear();
            foreach (var row in rows)
                Matches.Add(row);

            StatusMessage = rows.Count == 0
                ? "No previous bills found for this patient."
                : $"{rows.Count} recent patient bill(s). Select one to refill.";

            if (rows.Count == 1)
                SelectedMatch = rows[0];
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

    private async Task LoadRefillAsync(int saleId)
    {
        IsBusy = true;
        try
        {
            var result = await _sales.GetLastSaleRefillAsync(saleId, _branchId);
            if (result.IsFailure || result.Value is null)
            {
                StatusMessage = result.Error ?? "Could not load last bill.";
                Refill = null;
                Lines.Clear();
                return;
            }

            Refill = result.Value;
            Lines.Clear();
            foreach (var line in result.Value.Lines)
            {
                var vm = new LastSaleRefillLineViewModel(line);
                vm.AddRequested += OnLineAddRequested;
                vm.LogShortageRequested += OnLogShortageRequested;
                Lines.Add(vm);
            }

            StatusMessage = result.Value.Lines.Count(l => l.HasStock) == 0
                ? "Last bill loaded, but none of the medicines have stock right now."
                : "Tick lines or tap Add. Add all selected adds in-stock items to the cart.";
            CommandManager.InvalidateRequerySuggested();
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

    private void OnLineAddRequested(LastSaleRefillLineViewModel line)
    {
        if (!line.Line.HasStock || line.RefillQty <= 0) return;
        PendingAdds.Add((line.Line, line.RefillQty));
        Confirmed = true;
        RequestClose?.Invoke();
    }

    private async void OnLogShortageRequested(LastSaleRefillLineViewModel line)
    {
        if (line.Line.HasStock) return;
        var qty = line.RefillQty > 0 ? line.RefillQty : line.Line.LastQuantity;
        if (qty <= 0) return;

        try
        {
            var result = await _shortageBook.RecordAsync(
                new RecordShortageRequest(
                    line.Line.MedicineId,
                    qty,
                    line.Line.AvailableStock,
                    ShortageSource.Refill,
                    Refill?.PatientName,
                    Refill?.Mobile),
                _branchId,
                _recordedBy);

            StatusMessage = result.IsFailure
                ? (result.Error ?? "Could not record shortage.")
                : $"Shortage recorded for {line.Line.MedicineName} × {qty:0.##}.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private void AddAllSelected()
    {
        PendingAdds.Clear();
        foreach (var line in Lines.Where(l => l.IsSelected && l.Line.HasStock && l.RefillQty > 0))
            PendingAdds.Add((line.Line, line.RefillQty));

        if (PendingAdds.Count == 0)
        {
            _dialog.ShowInfo("Select at least one in-stock medicine.");
            return;
        }

        Confirmed = true;
        RequestClose?.Invoke();
    }

    private void Confirm()
    {
        if (PendingAdds.Count == 0)
            AddAllSelected();
        else
        {
            Confirmed = true;
            RequestClose?.Invoke();
        }
    }
}
