using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.Application.Features.Accounting;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Accounting;

/// <summary>Popup to pay/collect full or partial amount against a single open bill.</summary>
public sealed class PayBillViewModel : ObservableObject
{
    private readonly IAccountingService _accounting;
    private readonly IDialogService _dialog;
    private readonly int? _branchId;
    private readonly Task _accountsLoaded;

    private decimal _amount;
    private AccountLookupDto? _selectedAccount;
    private string? _narration;
    private bool _isBusy;
    private string? _errorMessage;

    public PayBillViewModel(
        IAccountingService accounting,
        IDialogService dialog,
        int? branchId,
        PartyLedgerKind kind,
        PartyLedgerRowDto party,
        PartyBillRowDto bill)
    {
        _accounting = accounting;
        _dialog = dialog;
        _branchId = branchId;
        Kind = kind;
        Party = party;
        Bill = bill;
        _amount = bill.BalanceDue;

        PayCommand = new AsyncRelayCommand(PayAsync, () => CanPay);
        PayFullCommand = new RelayCommand(_ => Amount = Bill.BalanceDue, _ => !IsBusy);
        _isBusy = true;
        _accountsLoaded = LoadAccountsAsync();
    }

    public PartyLedgerKind Kind { get; }
    public PartyLedgerRowDto Party { get; }
    public PartyBillRowDto Bill { get; }

    public bool IsSupplierPayment => Kind == PartyLedgerKind.Supplier;
    public string Title => IsSupplierPayment ? "Pay bill" : "Collect on bill";
    public string Subtitle => IsSupplierPayment
        ? "Record cash/bank payment against this purchase invoice."
        : "Record cash/bank receipt against this sale invoice.";
    public string AmountLabel => IsSupplierPayment ? "Amount to pay" : "Amount to collect";
    public string AccountLabel => IsSupplierPayment ? "Pay from" : "Receive in";
    public string ConfirmLabel => IsSupplierPayment ? "Pay now" : "Collect now";

    public ObservableCollection<AccountLookupDto> CashBankAccounts { get; } = new();

    public event Action? Paid;

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

    public bool CanPay =>
        !IsBusy
        && Amount > 0
        && Amount <= Bill.BalanceDue + 0.009m
        && SelectedAccount is not null;

    public ICommand PayCommand { get; }
    public ICommand PayFullCommand { get; }

    private async Task LoadAccountsAsync()
    {
        IsBusy = true;
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
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PayAsync()
    {
        await _accountsLoaded;
        if (SelectedAccount is null) return;

        if (Amount <= 0)
        {
            ErrorMessage = "Enter an amount.";
            return;
        }

        if (Amount > Bill.BalanceDue + 0.009m)
        {
            ErrorMessage = $"Amount cannot exceed balance due ₹{Bill.BalanceDue:N2}.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var narration = string.IsNullOrWhiteSpace(Narration)
                ? $"{(IsSupplierPayment ? "Payment" : "Receipt")} — {Bill.InvoiceNumber}"
                : Narration.Trim();

            ResultLike result = IsSupplierPayment
                ? await PaySupplierAsync(narration)
                : await CollectCustomerAsync(narration);

            if (!result.Ok)
            {
                ErrorMessage = result.Error ?? "Could not save voucher.";
                return;
            }

            Paid?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _dialog.ShowError(ex.Message, Title);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<ResultLike> PaySupplierAsync(string narration)
    {
        var result = await _accounting.CreatePaymentAsync(new CreatePaymentRequest
        {
            SupplierId = Party.PartyId,
            Amount = Amount,
            CashOrBankAccountId = SelectedAccount!.Id,
            EntryDate = DateTime.Today,
            Narration = narration,
            AllocationMode = PaymentAllocationMode.BillWise,
            BillAllocations =
            [
                new BillPaymentAllocationDto
                {
                    PurchaseId = Bill.TransactionId,
                    Amount = Amount
                }
            ]
        }, _branchId);

        return new ResultLike(result.IsSuccess, result.Error);
    }

    private async Task<ResultLike> CollectCustomerAsync(string narration)
    {
        var result = await _accounting.CreateReceiptAsync(new CreateReceiptRequest
        {
            CustomerId = Party.PartyId,
            Amount = Amount,
            CashOrBankAccountId = SelectedAccount!.Id,
            EntryDate = DateTime.Today,
            Narration = narration,
            AllocationMode = PaymentAllocationMode.BillWise,
            BillAllocations =
            [
                new BillReceiptAllocationDto
                {
                    SaleId = Bill.TransactionId,
                    Amount = Amount
                }
            ]
        }, _branchId);

        return new ResultLike(result.IsSuccess, result.Error);
    }

    private sealed record ResultLike(bool Ok, string? Error);
}
