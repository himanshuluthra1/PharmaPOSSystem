using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Accounting;
using PharmaPOS.WPF.Mvvm;

namespace PharmaPOS.WPF.ViewModels.Accounting;

public class CashBookTabViewModel : ObservableObject
{
    private readonly IAccountingService _accounting;
    private readonly int? _branchId;
    private string? _statusMessage;

    public CashBookTabViewModel(IAccountingService accounting, ICurrentUserService currentUser)
    {
        _accounting = accounting;
        _branchId = currentUser.CurrentUser?.BranchId;
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
    }

    public ObservableCollection<CashBookRowDto> Items { get; } = new();

    public decimal ClosingBalance => Items.LastOrDefault()?.RunningBalance ?? 0m;

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public ICommand RefreshCommand { get; }

    public async Task RefreshAsync()
    {
        try
        {
            var rows = await _accounting.GetCashBookAsync(_branchId);
            Items.Clear();
            foreach (var row in rows)
                Items.Add(row);

            OnPropertyChanged(nameof(ClosingBalance));
            StatusMessage = rows.Count == 0
                ? "No cash book entries yet. Post payments, receipts, or expenses to see activity."
                : $"{rows.Count} cash movement(s). Closing balance: {ClosingBalance:N2}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load cash book: {ex.Message}";
        }
    }
}
