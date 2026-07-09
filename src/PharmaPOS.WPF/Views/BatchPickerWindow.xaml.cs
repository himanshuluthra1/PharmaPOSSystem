using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PharmaPOS.WPF.ViewModels.Sales;

namespace PharmaPOS.WPF.Views;

public partial class BatchPickerWindow : Window
{
    private readonly BatchPickerViewModel _viewModel;

    public BatchPickerWindow(BatchPickerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += (_, _) => BatchGrid.Focus();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
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
                ConfirmSelection();
                e.Handled = true;
                break;
        }
    }

    private void BatchGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => ConfirmSelection();

    private void SelectButton_Click(object sender, RoutedEventArgs e)
        => ConfirmSelection();

    private void ConfirmSelection()
    {
        if (_viewModel.SelectedBatch is null) return;
        DialogResult = true;
        Close();
    }

    private void ScrollToSelected()
    {
        if (_viewModel.SelectedIndex >= 0 && _viewModel.SelectedIndex < BatchGrid.Items.Count)
            BatchGrid.ScrollIntoView(BatchGrid.Items[_viewModel.SelectedIndex]);
    }
}
