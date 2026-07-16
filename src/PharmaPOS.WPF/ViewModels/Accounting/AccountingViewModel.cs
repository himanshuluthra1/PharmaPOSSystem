using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Accounting;
using PharmaPOS.Shared.Constants;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Accounting;

/// <summary>Shell view model for the Accounting module.</summary>
public class AccountingViewModel : ObservableObject
{
    private readonly IAccountingService _accounting;
    private readonly int? _branchId;

    private int _selectedTab;
    private AccountingSummaryDto _summary = new();

    public AccountingViewModel(
        IAccountingService accounting,
        ICurrentUserService currentUser,
        IDialogService dialog)
    {
        _accounting = accounting;
        _branchId = currentUser.CurrentUser?.BranchId;

        CanCreateVouchers = currentUser.HasAnyPermission(
            AppConstants.Permissions.AccountingVouchers, AppConstants.Permissions.AccountingManage);
        CanViewJournal = currentUser.HasAnyPermission(
            AppConstants.Permissions.AccountingJournal, AppConstants.Permissions.AccountingView,
            AppConstants.Permissions.AccountingManage);

        PartyLedger = new PartyLedgerTabViewModel(accounting, currentUser, OnPartySelected);
        Vouchers = new VoucherTabViewModel(accounting, currentUser, dialog);
        Vouchers.VoucherSaved += OnVoucherSavedAsync;
        CashBook = new CashBookTabViewModel(accounting, currentUser);
        Journal = new JournalTabViewModel(accounting, currentUser);

        RecordPaymentCommand = new RelayCommand(_ =>
        {
            if (PartyLedger.SelectedParty is not PartyLedgerRowDto party) return;
            if (PartyLedger.SelectedKind.Kind != PartyLedgerKind.Supplier) return;
            Vouchers.PrefillPayment(party.PartyId, party.Name, party.OutstandingBalance);
            SelectedTab = 1;
        }, _ => CanCreateVouchers && PartyLedger.SelectedParty is not null &&
                 PartyLedger.SelectedKind.Kind == PartyLedgerKind.Supplier &&
                 PartyLedger.SelectedParty.OutstandingBalance > 0);

        RecordReceiptCommand = new RelayCommand(_ =>
        {
            if (PartyLedger.SelectedParty is not PartyLedgerRowDto party) return;
            if (PartyLedger.SelectedKind.Kind != PartyLedgerKind.Customer) return;
            Vouchers.PrefillReceipt(party.PartyId, party.Name, party.OutstandingBalance);
            SelectedTab = 1;
        }, _ => CanCreateVouchers && PartyLedger.SelectedParty is not null &&
                 PartyLedger.SelectedKind.Kind == PartyLedgerKind.Customer &&
                 PartyLedger.SelectedParty.OutstandingBalance > 0);

        RefreshCommand = new AsyncRelayCommand(_ => RefreshAllAsync());
        _ = RefreshAllAsync();
    }

    public PartyLedgerTabViewModel PartyLedger { get; }
    public VoucherTabViewModel Vouchers { get; }
    public CashBookTabViewModel CashBook { get; }
    public JournalTabViewModel Journal { get; }

    public bool CanCreateVouchers { get; }
    public bool CanViewJournal { get; }

    public AccountingSummaryDto Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public int SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (!SetProperty(ref _selectedTab, value)) return;
            _ = value switch
            {
                2 => CashBook.RefreshAsync(),
                3 => Journal.RefreshAsync(),
                _ => Task.CompletedTask
            };
        }
    }

    public ICommand RefreshCommand { get; }
    public ICommand RecordPaymentCommand { get; }
    public ICommand RecordReceiptCommand { get; }

    private void OnPartySelected(PartyLedgerRowDto? party)
        => CommandManager.InvalidateRequerySuggested();

    private async Task OnVoucherSavedAsync()
    {
        Summary = await _accounting.GetSummaryAsync(_branchId);
        await PartyLedger.RefreshAsync();
        if (SelectedTab == 2) await CashBook.RefreshAsync();
    }

    private async Task RefreshAllAsync()
    {
        Summary = await _accounting.GetSummaryAsync(_branchId);
        await PartyLedger.RefreshAsync();
        if (SelectedTab == 2) await CashBook.RefreshAsync();
        if (SelectedTab == 3) await Journal.RefreshAsync();
    }
}
