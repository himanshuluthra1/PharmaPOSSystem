using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PharmaPOS.Application.Features.Purchases;
using PharmaPOS.WPF.ViewModels.Purchases;

namespace PharmaPOS.WPF.Views;

public partial class PurchaseSearchWindow : Window
{
    private readonly PurchaseSearchViewModel _viewModel;
    private bool _navigatingSuggestions = true;

    public PurchaseSearchWindow(PurchaseSearchViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += async (_, _) =>
        {
            SupplierFilterBox.Focus();
            SupplierFilterBox.SelectAll();
            if (_viewModel.IsAllSuppliersFilter)
                await _viewModel.SelectAllSuppliersAsync();
        };
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e) => HandleNavigationKey(e, ref _navigatingSuggestions);

    private void SupplierFilterBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        _navigatingSuggestions = _viewModel.ShowSupplierSuggestions;
        HandleNavigationKey(e, ref _navigatingSuggestions);
    }

    private void SupplierSuggestionList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        _navigatingSuggestions = true;
        HandleNavigationKey(e, ref _navigatingSuggestions);
    }

    private void BillsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        _navigatingSuggestions = false;
        HandleNavigationKey(e, ref _navigatingSuggestions);
    }

    private void HandleNavigationKey(KeyEventArgs e, ref bool navigateSuggestions)
    {
        switch (e.Key)
        {
            case Key.Down:
                if (navigateSuggestions && _viewModel.ShowSupplierSuggestions)
                    _viewModel.MoveSupplierSuggestion(1);
                else
                    _viewModel.MoveBillSelection(1);
                ScrollSelections();
                e.Handled = true;
                break;
            case Key.Up:
                if (navigateSuggestions && _viewModel.ShowSupplierSuggestions)
                    _viewModel.MoveSupplierSuggestion(-1);
                else
                    _viewModel.MoveBillSelection(-1);
                ScrollSelections();
                e.Handled = true;
                break;
            case Key.Enter:
                if (navigateSuggestions && _viewModel.ShowSupplierSuggestions)
                {
                    _viewModel.ConfirmSupplierSuggestion();
                    navigateSuggestions = false;
                }
                else
                {
                    ConfirmSelection();
                }
                e.Handled = true;
                break;
        }
    }

    private void SupplierSuggestionList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (SupplierSuggestionList.SelectedItem is SupplierLookupDto supplier)
            _viewModel.SelectSupplierSuggestion(supplier);
    }

    private void BillsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => ConfirmSelection();

    private void OpenPurchaseButton_Click(object sender, RoutedEventArgs e)
        => ConfirmSelection();

    private void ConfirmSelection()
    {
        if (_viewModel.SelectedBill is null) return;
        DialogResult = true;
        Close();
    }

    private void ScrollSelections()
    {
        if (_viewModel.ShowSupplierSuggestions &&
            _viewModel.SupplierSuggestionIndex >= 0 &&
            _viewModel.SupplierSuggestionIndex < SupplierSuggestionList.Items.Count)
        {
            SupplierSuggestionList.ScrollIntoView(SupplierSuggestionList.Items[_viewModel.SupplierSuggestionIndex]);
            return;
        }

        if (_viewModel.SelectedBillIndex >= 0 && _viewModel.SelectedBillIndex < BillsGrid.Items.Count)
            BillsGrid.ScrollIntoView(BillsGrid.Items[_viewModel.SelectedBillIndex]);
    }
}
