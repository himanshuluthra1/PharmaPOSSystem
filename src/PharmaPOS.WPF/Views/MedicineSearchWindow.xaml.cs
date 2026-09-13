using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Masters;
using PharmaPOS.Application.Features.Sales;
using PharmaPOS.Application.Features.ShortageBook;
using PharmaPOS.Domain.Enums;
using PharmaPOS.WPF.Services;
using PharmaPOS.WPF.ViewModels.Sales;

namespace PharmaPOS.WPF.Views;

public partial class MedicineSearchWindow : Window
{
    private readonly MedicineSearchViewModel _viewModel;
    private readonly IPharmacyMedicineImportService? _import;
    private readonly IMastersService? _masters;
    private readonly IMedicineLedgerDialogService? _medicineLedger;
    private readonly IShortageBookService? _shortageBook;
    private readonly IDialogService? _dialog;
    private readonly ICurrentUserService? _currentUser;
    private MedicineLookupDto? _createdMedicine;
    private bool _shortageBusy;

    /// <summary>Selected existing medicine, or newly created (website / copy).</summary>
    public MedicineLookupDto? ResultMedicine => _createdMedicine ?? _viewModel.SelectedMedicine;

    public MedicineSearchWindow(
        MedicineSearchViewModel viewModel,
        IPharmacyMedicineImportService? import = null,
        IMastersService? masters = null,
        IMedicineLedgerDialogService? medicineLedger = null,
        IShortageBookService? shortageBook = null,
        IDialogService? dialog = null,
        ICurrentUserService? currentUser = null)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _import = import;
        _masters = masters;
        _medicineLedger = medicineLedger;
        _shortageBook = shortageBook;
        _dialog = dialog;
        _currentUser = currentUser;
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
        var allowCopy = _masters is not null;
        CreateFromWebButton.Visibility = allowCreate ? Visibility.Visible : Visibility.Collapsed;
        CreateFromWebButton.IsEnabled = allowCreate;
        CopyMedicineButton.Visibility = allowCopy ? Visibility.Visible : Visibility.Collapsed;
        CopyMedicineButton.IsEnabled = allowCopy;
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

        if (e.Key == Key.F7)
        {
            e.Handled = true;
            await AddSelectedToShortageBookAsync();
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

    private async Task AddSelectedToShortageBookAsync()
    {
        if (_shortageBusy) return;
        var medicine = _viewModel.SelectedMedicine;
        if (medicine is null)
        {
            MessageBox.Show("Select a medicine in the list first.", "Shortage book",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_shortageBook is null || _dialog is null)
        {
            MessageBox.Show("Shortage book is not available.", "Shortage book",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _shortageBusy = true;
        try
        {
            var branchId = _currentUser?.CurrentUser?.BranchId;
            var onHand = await _shortageBook.GetOnHandQuantityAsync(medicine.Id, branchId);
            var defaultRequested = Math.Max(1m, onHand > 0 ? onHand + 1 : 1m);
            var prompt = _dialog.PromptShortageDetails(
                medicine.Name,
                defaultRequested,
                detailLine: $"On hand: {onHand:0.##}. Wanted quantity and customer name are optional.");
            if (prompt is null) return;

            var result = await _shortageBook.RecordAsync(
                new RecordShortageRequest(
                    medicine.Id,
                    prompt.WantedQuantity,
                    onHand,
                    ShortageSource.Manual,
                    prompt.CustomerName),
                branchId,
                _currentUser?.CurrentUser?.FullName ?? _currentUser?.CurrentUser?.Username);

            if (result.IsFailure)
                _dialog.ShowError(result.Error ?? "Could not record shortage.");
            else
                _dialog.ShowInfo($"Added to shortage book: {medicine.Name}", "Shortage book");
        }
        catch (Exception ex)
        {
            _dialog.ShowError(ex.Message);
        }
        finally
        {
            _shortageBusy = false;
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

    private void CopyMedicineButton_Click(object sender, RoutedEventArgs e)
    {
        if (_masters is null) return;

        var selected = _viewModel.SelectedMedicine;
        var win = new MedicineCopyWindow(
            _masters,
            preselectMedicineId: selected?.Id,
            suggestedSearch: selected is null ? _viewModel.SearchText : null)
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
