using System.Windows;
using System.Windows.Input;
using PharmaPOS.Application.Features.Sales;
using PharmaPOS.WPF.ViewModels.Sales;

namespace PharmaPOS.WPF.Views;

public partial class CustomerPickerWindow : Window
{
    public CustomerPickerWindow(CustomerPickerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += () =>
        {
            DialogResult = viewModel.Confirmed;
            Close();
        };
        Loaded += async (_, _) =>
        {
            SearchBox.Focus();
            SearchBox.CaretIndex = SearchBox.Text.Length;
            await viewModel.EnsureInitialSearchAsync();
        };
    }

    public CustomerLookupDto? SelectedCustomer =>
        DataContext is CustomerPickerViewModel vm ? vm.SelectedCustomer : null;

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not CustomerPickerViewModel vm) return;
        switch (e.Key)
        {
            case Key.Escape:
                vm.CancelCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Down:
                vm.MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                vm.MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter when vm.Selected is not null && !SearchBox.IsFocused:
                vm.Confirm();
                e.Handled = true;
                break;
        }
    }

    private void ResultsList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is CustomerPickerViewModel vm)
        {
            vm.Confirm();
            e.Handled = true;
        }
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is CustomerPickerViewModel vm)
            vm.Confirm();
    }
}
