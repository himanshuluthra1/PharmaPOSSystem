using System.Windows.Controls;
using System.Windows.Input;
using PharmaPOS.WPF.ViewModels.Purchases;

namespace PharmaPOS.WPF.Views;

/// <summary>
/// Purchase / goods-receipt screen code-behind. Supports the keyboard-first
/// workflow (F3 to search, F9 to receive, Esc to start a new purchase, Enter to
/// add the selected medicine as a line).
/// </summary>
public partial class PurchaseView : UserControl
{
    public PurchaseView()
    {
        InitializeComponent();
        Loaded += (_, _) => SearchBox.Focus();
    }

    private PurchaseViewModel? ViewModel => DataContext as PurchaseViewModel;

    private void FocusSearch()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        }));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var vm = ViewModel;
        if (vm is null) return;

        switch (e.Key)
        {
            case Key.F3:
                FocusSearch();
                e.Handled = true;
                break;
            case Key.F9:
                if (vm.SaveCommand.CanExecute(null)) vm.SaveCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                vm.NewPurchaseCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Enter:
                if (vm.AddLineCommand.CanExecute(null))
                {
                    vm.AddLineCommand.Execute(null);
                    e.Handled = true;
                }
                break;
        }
    }
}
