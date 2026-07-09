using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PharmaPOS.WPF.ViewModels.Sales;

namespace PharmaPOS.WPF.Views;

public partial class BillSearchWindow : Window
{
    private readonly BillSearchViewModel _viewModel;

    public BillSearchWindow(BillSearchViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
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
                if (_viewModel.TryConfirmSelection())
                    ConfirmSelection();
                else
                    ScrollToSelected();
                e.Handled = true;
                break;
        }
    }

    private void SuggestionList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (SuggestionList.SelectedItem is string name)
            _viewModel.SelectPatientSuggestion(name);
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => ConfirmSelection();

    private void OpenBillButton_Click(object sender, RoutedEventArgs e)
        => ConfirmSelection();

    private void ConfirmSelection()
    {
        if (_viewModel.SelectedBill is null) return;
        DialogResult = true;
        Close();
    }

    private void ScrollToSelected()
    {
        if (_viewModel.SelectedSuggestionIndex >= 0 &&
            _viewModel.SelectedSuggestionIndex < SuggestionList.Items.Count)
        {
            SuggestionList.ScrollIntoView(SuggestionList.Items[_viewModel.SelectedSuggestionIndex]);
            return;
        }

        if (_viewModel.SelectedIndex >= 0 && _viewModel.SelectedIndex < ResultsList.Items.Count)
            ResultsList.ScrollIntoView(ResultsList.Items[_viewModel.SelectedIndex]);
    }
}
