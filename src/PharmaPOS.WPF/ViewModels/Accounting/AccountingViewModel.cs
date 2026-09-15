using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Accounting;
using PharmaPOS.Application.Features.Settings;
using PharmaPOS.Shared.Constants;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Accounting;

/// <summary>Shell view model for the Accounting module.</summary>
public class AccountingViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IInvoiceViewerDialogService _invoiceViewer;
    private readonly int? _branchId;

    private int _selectedTab;
    private AccountingSummaryDto _summary = new();

    public AccountingViewModel(
        IAccountingService accounting,
        IServiceScopeFactory scopeFactory,
        ISettingsService settings,
        IBillShareService billShare,
        IInvoicePrintService print,
        IInvoiceViewerDialogService invoiceViewer,
        ICurrentUserService currentUser,
        IFinancialYearContext financialYear,
        IDialogService dialog)
    {
        _scopeFactory = scopeFactory;
        _branchId = currentUser.CurrentUser?.BranchId;
        _invoiceViewer = invoiceViewer;

        CanCreateVouchers = currentUser.HasAnyPermission(
            AppConstants.Permissions.AccountingVouchers, AppConstants.Permissions.AccountingManage)
            && financialYear.CanEditTransactions;
        CanViewJournal = currentUser.HasAnyPermission(
            AppConstants.Permissions.AccountingJournal, AppConstants.Permissions.AccountingView,
            AppConstants.Permissions.AccountingManage);

        PartyLedger = new PartyLedgerTabViewModel(
            scopeFactory, currentUser, dialog, CanCreateVouchers, OnPartySelected);
        PartyLedger.BillPaid += OnBillPaidAsync;
        CustomerDues = new CustomerDuesTabViewModel(
            accounting,
            scopeFactory,
            settings,
            billShare,
            print,
            currentUser,
            dialog);
        CustomerDues.DuesChanged += OnDuesChangedAsync;
        Vouchers = new VoucherTabViewModel(accounting, currentUser, dialog);
        Vouchers.VoucherSaved += OnVoucherSavedAsync;
        CashBook = new CashBookTabViewModel(accounting, currentUser);
        Journal = new JournalTabViewModel(accounting, currentUser);

        RecordPaymentCommand = new RelayCommand(_ =>
        {
            if (PartyLedger.SelectedParty is not PartyLedgerRowDto party) return;
            if (PartyLedger.SelectedKind.Kind != PartyLedgerKind.Supplier) return;
            Vouchers.PrefillPayment(party.PartyId, party.Name, party.OutstandingBalance);
            SelectedTab = 2;
        }, _ => CanCreateVouchers && PartyLedger.SelectedParty is not null &&
                 PartyLedger.SelectedKind.Kind == PartyLedgerKind.Supplier &&
                 PartyLedger.SelectedParty.OutstandingBalance > 0);

        RecordReceiptCommand = new RelayCommand(_ =>
        {
            if (PartyLedger.SelectedParty is not PartyLedgerRowDto party) return;
            if (PartyLedger.SelectedKind.Kind != PartyLedgerKind.Customer) return;
            Vouchers.PrefillReceipt(party.PartyId, party.Name, party.OutstandingBalance);
            SelectedTab = 2;
        }, _ => CanCreateVouchers && PartyLedger.SelectedParty is not null &&
                 PartyLedger.SelectedKind.Kind == PartyLedgerKind.Customer &&
                 PartyLedger.SelectedParty.OutstandingBalance > 0);

        RefreshCommand = new AsyncRelayCommand(_ => RefreshAllAsync());
        _ = RefreshAllAsync();
    }

    public PartyLedgerTabViewModel PartyLedger { get; }
    public CustomerDuesTabViewModel CustomerDues { get; }
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
                0 => PartyLedger.RefreshAsync(),
                1 => CustomerDues.RefreshAsync(),
                3 => CashBook.RefreshAsync(),
                4 => Journal.RefreshAsync(),
                _ => Task.CompletedTask
            };
        }
    }

    public ICommand RefreshCommand { get; }
    public ICommand RecordPaymentCommand { get; }
    public ICommand RecordReceiptCommand { get; }

    /// <summary>Open the underlying sale, purchase, or purchase-return for a Parties open-bill row.</summary>
    public Task OpenPartyBillAsync(PartyBillSettleLineViewModel? line)
    {
        if (line is null) return Task.CompletedTask;
        if (line.IsPurchaseReturn)
            return _invoiceViewer.ShowPurchaseReturnAsync(line.Bill.DocumentId);
        if (line.TransactionId <= 0) return Task.CompletedTask;
        return PartyLedger.SelectedKind.Kind == PartyLedgerKind.Supplier
            ? _invoiceViewer.ShowPurchaseAsync(line.TransactionId)
            : _invoiceViewer.ShowSaleAsync(line.TransactionId);
    }

    /// <summary>Open the sale invoice for a Customer Dues open-bill row.</summary>
    public Task OpenCustomerDueBillAsync(PartyBillRowDto? bill)
    {
        if (bill is null || bill.TransactionId <= 0) return Task.CompletedTask;
        return _invoiceViewer.ShowSaleAsync(bill.TransactionId);
    }

    /// <summary>Open the purchase invoice for a voucher bill-allocation row.</summary>
    public Task OpenVoucherAllocationBillAsync(BillAllocationLineViewModel? line)
    {
        if (line is null || line.PurchaseId <= 0) return Task.CompletedTask;
        return _invoiceViewer.ShowPurchaseAsync(line.PurchaseId);
    }

    private void OnPartySelected(PartyLedgerRowDto? party)
        => CommandManager.InvalidateRequerySuggested();

    private async Task OnVoucherSavedAsync()
    {
        await RefreshSummaryAsync().ConfigureAwait(true);
        await PartyLedger.RefreshAsync().ConfigureAwait(true);
        await CustomerDues.RefreshAsync().ConfigureAwait(true);
        if (SelectedTab == 3) await CashBook.RefreshAsync().ConfigureAwait(true);
    }

    private Task OnBillPaidAsync() => OnVoucherSavedAsync();

    private async Task OnDuesChangedAsync()
    {
        try
        {
            await RefreshSummaryAsync().ConfigureAwait(true);
            await PartyLedger.RefreshAsync().ConfigureAwait(true);
            if (SelectedTab == 3) await CashBook.RefreshAsync().ConfigureAwait(true);
        }
        catch
        {
            // Avoid unhandled async exceptions after collect.
        }
    }

    private async Task RefreshAllAsync()
    {
        try
        {
            // Parties first so the default tab is never left empty while summary/dues load.
            await PartyLedger.RefreshAsync().ConfigureAwait(true);
            await RefreshSummaryAsync().ConfigureAwait(true);
            await CustomerDues.RefreshAsync().ConfigureAwait(true);
            if (SelectedTab == 3) await CashBook.RefreshAsync().ConfigureAwait(true);
            if (SelectedTab == 4) await Journal.RefreshAsync().ConfigureAwait(true);
        }
        catch
        {
            // Startup/background refresh must not crash the UI thread.
        }
    }

    private async Task RefreshSummaryAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var accounting = scope.ServiceProvider.GetRequiredService<IAccountingService>();
            Summary = await accounting.GetSummaryAsync(_branchId).ConfigureAwait(true);
        }
        catch
        {
            // KPI strip is secondary to the party grid.
        }
    }
}
