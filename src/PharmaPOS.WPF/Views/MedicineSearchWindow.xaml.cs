using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PharmaPOS.Application.Features.Masters;
using PharmaPOS.Application.Features.Sales;
using PharmaPOS.WPF.Services;
using PharmaPOS.WPF.ViewModels.Sales;

namespace PharmaPOS.WPF.Views;

public partial class MedicineSearchWindow : Window
{
    private readonly MedicineSearchViewModel _viewModel;
    private readonly IPharmacyMedicineImportService? _import;
    private readonly IMastersService? _masters;
    private readonly IMedicineLedgerDialogService? _medicineLedger;
    private MedicineLookupDto? _createdMedicine;

    /// <summary>Selected existing medicine, or newly created from website.</summary>
    public MedicineLookupDto? ResultMedicine => _createdMedicine ?? _viewModel.SelectedMedicine;

    public MedicineSearchWindow(
        MedicineSearchViewModel viewModel,
        IPharmacyMedicineImportService? import = null,
        IMastersService? masters = null,
        IMedicineLedgerDialogService? medicineLedger = null)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _import = import;
        _masters = masters;
        _medicineLedger = medicineLedger;
        DataContext = viewModel;
        Loaded += (_, _) =>
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            UpdateCreateButtonVisibility();
        };
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MedicineSearchViewModel.Hint)
                or nameof(MedicineSearchViewModel.Results)
                or nameof(MedicineSearchViewModel.SearchText)
                or nameof(MedicineSearchViewModel.SelectedIndex))
                UpdateCreateButtonVisibility();
        };
        viewModel.Results.CollectionChanged += (_, _) => UpdateCreateButtonVisibility();
    }

    private void UpdateCreateButtonVisibility()
    {
        var allowCreate = _import is not null && _masters is not null;
        CreateFromWebButton.Visibility = allowCreate ? Visibility.Visible : Visibility.Collapsed;
        CreateFromWebButton.IsEnabled = allowCreate;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e) => _ = HandleNavigationKeyAsync(e);

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e) => _ = HandleNavigationKeyAsync(e);

    private void ResultsList_PreviewKeyDown(object sender, KeyEventArgs e) => _ = HandleNavigationKeyAsync(e);

    private async Task HandleNavigationKeyAsync(KeyEventArgs e)
    {
        if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            await ShowLedgerForSelectedAsync();
            return;
        }

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

    private async Task ShowLedgerForSelectedAsync()
    {
        var medicine = _viewModel.SelectedMedicine;
        if (medicine is null)
        {
            MessageBox.Show("Select a medicine in the list first.", "Medicine ledger",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_medicineLedger is null)
        {
            MessageBox.Show("Medicine ledger is not available.", "Medicine ledger",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await _medicineLedger.ShowAsync(medicine.Id, medicine.Name);
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => ConfirmSelection();

    private void SelectButton_Click(object sender, RoutedEventArgs e)
        => ConfirmSelection();

    private void ConfirmSelection()
    {
        if (ResultMedicine is null) return;
        DialogResult = true;
        Close();
    }

    private void CreateFromWebButton_Click(object sender, RoutedEventArgs e)
    {
        if (_import is null || _masters is null) return;

        var win = new MedicineFromUrlWindow(_import, _masters, _viewModel.SearchText)
        {
            Owner = this
        };
        if (win.ShowDialog() == true && win.CreatedMedicine is not null)
        {
            _createdMedicine = win.CreatedMedicine;
            DialogResult = true;
            Close();
        }
    }

    private void ScrollToSelected()
    {
        if (_viewModel.SelectedIndex >= 0 && _viewModel.SelectedIndex < ResultsList.Items.Count)
            ResultsList.ScrollIntoView(ResultsList.Items[_viewModel.SelectedIndex]);
    }
}
