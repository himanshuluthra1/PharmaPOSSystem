using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PharmaPOS.Application.Features.Accounting;
using PharmaPOS.WPF.ViewModels.Accounting;

namespace PharmaPOS.WPF.Views;

public partial class AccountingView : UserControl
{
    public AccountingView()
    {
        InitializeComponent();
    }

    private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        AcctKpiGrid.Columns = e.NewSize.Width < 560 ? 2 : 4;
    }

    private void PartySuggestionList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not AccountingViewModel vm) return;
        if (sender is not ListBox list || list.SelectedItem is not PartyLedgerRowDto party) return;
        vm.Vouchers.SelectPartySuggestion(party);
    }
}
