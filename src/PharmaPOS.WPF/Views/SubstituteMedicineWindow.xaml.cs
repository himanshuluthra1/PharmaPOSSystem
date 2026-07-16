using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PharmaPOS.WPF.ViewModels.Sales;

namespace PharmaPOS.WPF.Views;

public partial class SubstituteMedicineWindow : Window
{
    private readonly SubstituteMedicineViewModel _viewModel;

    public SubstituteMedicineWindow(SubstituteMedicineViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += (_, _) =>
        {
            MedicineList.Focus();
            ScrollToSelected();
        };
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e) => HandleNavigationKey(e);

    private void MedicineList_PreviewKeyDown(object sender, KeyEventArgs e) => HandleNavigationKey(e);

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
                ConfirmSelection();
                e.Handled = true;
                break;
        }
    }

    private void MedicineList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => ConfirmSelection();

    private void SelectButton_Click(object sender, RoutedEventArgs e)
        => ConfirmSelection();

    private void ConfirmSelection()
    {
        if (_viewModel.SelectedMedicine is null) return;
        DialogResult = true;
        Close();
    }

    private void ScrollToSelected()
    {
        if (_viewModel.SelectedIndex >= 0 && _viewModel.SelectedIndex < MedicineList.Items.Count)
            MedicineList.ScrollIntoView(MedicineList.Items[_viewModel.SelectedIndex]);
    }
}
