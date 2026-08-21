using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Features.Accounting;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Accounting;

public sealed class CollectDueViewModel : ObservableObject
{
    private readonly IAccountingService _accounting;
    private readonly IDialogService _dialog;
    private readonly int? _branchId;

    private decimal _amount;
    private AccountLookupDto? _selectedAccount;
    private string? _narration;
    private bool _isBusy;
    private string? _errorMessage;

    public CollectDueViewModel(
        IAccountingService accounting,
        IDialogService dialog,
        int? branchId,
        PartyLedgerRowDto customer)
    {
        _accounting = accounting;
        _dialog = dialog;
        _branchId = branchId;
        Customer = customer;
        _amount = customer.OutstandingBalance;

        CollectCommand = new AsyncRelayCommand(CollectAsync, () => CanCollect);
        _ = LoadAccountsAsync();
    }

    public PartyLedgerRowDto Customer { get; }
    public ObservableCollection<AccountLookupDto> CashBankAccounts { get; } = new();

    public event Action<CustomerCollectionReceiptDto>? Collected;

    public decimal Amount
    {
        get => _amount;
        set
        {
            if (SetProperty(ref _amount, Math.Round(value, 2)))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public AccountLookupDto? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (SetProperty(ref _selectedAccount, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public string? Narration
    {
        get => _narration;
        set => SetProperty(ref _narration, value);
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

    public bool CanCollect =>
        !IsBusy
        && Amount > 0
        && Amount <= Customer.OutstandingBalance + 0.009m
        && SelectedAccount is not null;

    public ICommand CollectCommand { get; }

    private async Task LoadAccountsAsync()
    {
        try
        {
            var accounts = await _accounting.ListCashAndBankAccountsAsync();
            CashBankAccounts.Clear();
            foreach (var a in accounts)
                CashBankAccounts.Add(a);
            SelectedAccount = CashBankAccounts.FirstOrDefault();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task CollectAsync()
    {
        if (SelectedAccount is null) return;
        if (Amount <= 0)
        {
            ErrorMessage = "Enter an amount to collect.";
            return;
        }

        if (Amount > Customer.OutstandingBalance + 0.009m)
        {
            ErrorMessage = $"Amount cannot exceed outstanding ₹{Customer.OutstandingBalance:N2}.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _accounting.CreateReceiptAsync(new CreateReceiptRequest
            {
                CustomerId = Customer.PartyId,
                Amount = Amount,
                CashOrBankAccountId = SelectedAccount.Id,
                EntryDate = DateTime.Today,
                Narration = string.IsNullOrWhiteSpace(Narration)
                    ? $"Collection from {Customer.Name}"
                    : Narration.Trim()
            }, _branchId);

            if (result.IsFailure || result.Value is null)
            {
                ErrorMessage = result.Error ?? "Could not save receipt.";
                return;
            }

            var voucher = result.Value;
            Collected?.Invoke(new CustomerCollectionReceiptDto
            {
                CompanyName = string.IsNullOrWhiteSpace(voucher.CompanyName) ? "PharmaPOS" : voucher.CompanyName!,
                VoucherNumber = voucher.VoucherNumber,
                EntryDate = voucher.EntryDate,
                CustomerName = voucher.PartyName ?? Customer.Name,
                CustomerPhone = voucher.PartyPhone ?? Customer.Phone,
                AmountCollected = voucher.Amount,
                OutstandingAfter = voucher.OutstandingAfter,
                ReceivedInAccount = voucher.CashOrBankAccountName ?? SelectedAccount.DisplayLabel,
                Narration = voucher.Narration
            });
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _dialog.ShowError(ex.Message, "Collect now");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
