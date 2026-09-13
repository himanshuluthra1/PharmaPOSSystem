using System.Windows.Controls;
using System.Windows.Input;
using PharmaPOS.Application.Features.Accounting;
using PharmaPOS.WPF.Services;
using PharmaPOS.WPF.ViewModels.Accounting;

namespace PharmaPOS.WPF.Views;

public partial class AccountingView : UserControl
{
    public AccountingView()
    {
        InitializeComponent();
    }

    private AccountingViewModel? Vm => DataContext as AccountingViewModel;

    private void PartySuggestionList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Vm is null) return;
        if (sender is not ListBox list || list.SelectedItem is not PartyLedgerRowDto party) return;
        Vm.Vouchers.SelectPartySuggestion(party);
    }

    private void PartyBillsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => InvoiceDrillDown.HandleDoubleClick(e, sender, item =>
            item is PartyBillSettleLineViewModel line
                ? Vm?.OpenPartyBillAsync(line) ?? Task.CompletedTask
                : Task.CompletedTask);

    private void PartyBillsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not DataGrid { SelectedItem: PartyBillSettleLineViewModel line }) return;
        InvoiceDrillDown.TryHandleKey(e, line, _ => Vm?.OpenPartyBillAsync(line) ?? Task.CompletedTask);
    }

    private void CustomerOpenBillsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => InvoiceDrillDown.HandleDoubleClick(e, sender, item =>
            item is PartyBillRowDto bill
                ? Vm?.OpenCustomerDueBillAsync(bill) ?? Task.CompletedTask
                : Task.CompletedTask);

    private void CustomerOpenBillsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not DataGrid { SelectedItem: PartyBillRowDto bill }) return;
        InvoiceDrillDown.TryHandleKey(e, bill, _ => Vm?.OpenCustomerDueBillAsync(bill) ?? Task.CompletedTask);
    }

    private void VoucherAllocationsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => InvoiceDrillDown.HandleDoubleClick(e, sender, item =>
            item is BillAllocationLineViewModel line
                ? Vm?.OpenVoucherAllocationBillAsync(line) ?? Task.CompletedTask
                : Task.CompletedTask);

    private void VoucherAllocationsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not DataGrid { SelectedItem: BillAllocationLineViewModel line }) return;
        InvoiceDrillDown.TryHandleKey(e, line, _ => Vm?.OpenVoucherAllocationBillAsync(line) ?? Task.CompletedTask);
    }
}
