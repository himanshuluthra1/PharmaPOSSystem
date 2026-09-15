using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PharmaPOS.Application.Features.Sales;
using PharmaPOS.WPF.ViewModels.Sales;

namespace PharmaPOS.WPF.Views;

public partial class BillSearchWindow : Window
{
    private readonly BillSearchViewModel _viewModel;
    private readonly Func<int, Task>? _openBillAsync;
    private bool _isOpeningBill;

    public BillSearchWindow(BillSearchViewModel viewModel, Func<int, Task>? openBillAsync = null)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _openBillAsync = openBillAsync;
        DataContext = viewModel;
        Loaded += (_, _) =>
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        };
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e) => HandleNavigationKey(e);

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e) => HandleNavigationKey(e);

    private void SuggestionList_PreviewKeyDown(object sender, KeyEventArgs e) => HandleNavigationKey(e);

    private void ResultsList_PreviewKeyDown(object sender, KeyEventArgs e) => HandleNavigationKey(e);

    private void HandleNavigationKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                _viewModel.MoveSelection(1);
                ScrollToSelected();
                e.Handled = true;
                break;
            case Key.Up:
                _viewModel.MoveSelection(-1);
                ScrollToSelected();
                e.Handled = true;
                break;
            case Key.Enter:
                if (_viewModel.TryConfirmBillSelection())
                    _ = OpenSelectedBillAsync();
                else
                    ScrollToSelected();
                e.Handled = true;
                break;
        }
    }

    private void SuggestionList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (SuggestionList.SelectedItem is BillSearchSuggestionDto suggestion)
        {
            _viewModel.FocusSuggestion(suggestion);
            ScrollToSelected();
        }
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => _ = OpenSelectedBillAsync();

    private void OpenBillButton_Click(object sender, RoutedEventArgs e)
        => _ = OpenSelectedBillAsync();

    private async Task OpenSelectedBillAsync()
    {
        if (_isOpeningBill || _viewModel.SelectedBill is null) return;
        _isOpeningBill = true;
        try
        {
            if (_openBillAsync is not null)
                await _openBillAsync(_viewModel.SelectedBill.SaleId);
            else
            {
                DialogResult = true;
                Close();
            }
        }
        finally
        {
            _isOpeningBill = false;
            Activate();
            if (_viewModel.SelectedIndex >= 0)
                ResultsList.Focus();
            else
                SearchBox.Focus();
        }
    }

    private void ScrollToSelected()
    {
        if (_viewModel.SelectedIndex < 0 &&
            _viewModel.SelectedSuggestionIndex >= 0 &&
            _viewModel.SelectedSuggestionIndex < SuggestionList.Items.Count)
        {
            SuggestionList.ScrollIntoView(SuggestionList.Items[_viewModel.SelectedSuggestionIndex]);
            return;
        }

        if (_viewModel.SelectedIndex >= 0 && _viewModel.SelectedIndex < ResultsList.Items.Count)
            ResultsList.ScrollIntoView(ResultsList.Items[_viewModel.SelectedIndex]);
    }
}
