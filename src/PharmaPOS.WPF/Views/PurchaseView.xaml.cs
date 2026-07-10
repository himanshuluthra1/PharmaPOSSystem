using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PharmaPOS.Application.Features.Purchases;
using PharmaPOS.WPF.ViewModels.Purchases;

namespace PharmaPOS.WPF.Views;

/// <summary>
/// Purchase / goods-receipt screen code-behind. Enter on Item opens medicine popup;
/// F9 save; Esc new purchase; supplier and purchase dropdown keyboard support.
/// </summary>
public partial class PurchaseView : UserControl
{
    private static readonly HashSet<string> EditableColumns = new(StringComparer.Ordinal)
    {
        "Batch", "Qty", "Free", "Cost", "MRP", "Sale", "Disc%", "GST%"
    };

    private bool _purchaseListSelectionFromCode;

    public PurchaseView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => OnRequestItemFocus(ViewModel?.Lines.FirstOrDefault(l => l.IsEmpty));
    }

    private PurchaseViewModel? ViewModel => DataContext as PurchaseViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is PurchaseViewModel oldVm)
            oldVm.RequestItemFocus -= OnRequestItemFocus;
        if (e.NewValue is PurchaseViewModel newVm)
            newVm.RequestItemFocus += OnRequestItemFocus;
    }

    private void OnRequestItemFocus(PurchaseLineViewModel? line)
    {
        if (line is not null && !line.IsEmpty)
            FocusColumn(line, "Batch", beginEdit: true);
        else
            FocusItemColumn(line);
    }

    private void CommitGridEdit()
    {
        PurchaseGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        PurchaseGrid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private int GetColumnIndex()
        => PurchaseGrid.CurrentColumn is null ? 0 : PurchaseGrid.Columns.IndexOf(PurchaseGrid.CurrentColumn);

    private void FocusItemColumn(PurchaseLineViewModel? line)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var target = line ?? ViewModel?.Lines.LastOrDefault(l => l.IsEmpty);
            if (target is null) return;
            PurchaseGrid.SelectedItem = target;
            FocusCell(target, 0, beginEdit: false);
        });
    }

    private void FocusColumn(PurchaseLineViewModel line, string header, bool beginEdit)
    {
        var index = PurchaseGrid.Columns.ToList().FindIndex(c => c.Header?.ToString() == header);
        if (index >= 0)
            FocusCell(line, index, beginEdit);
    }

    private void FocusCell(PurchaseLineViewModel line, int columnIndex, bool beginEdit)
    {
        if (columnIndex < 0 || columnIndex >= PurchaseGrid.Columns.Count) return;

        var column = PurchaseGrid.Columns[columnIndex];
        PurchaseGrid.SelectedItem = line;
        PurchaseGrid.CurrentCell = new DataGridCellInfo(line, column);
        PurchaseGrid.Focus();
        if (PurchaseGrid.ItemContainerGenerator.ContainerFromItem(line) is DataGridRow row)
            row.Focus();

        var header = column.Header?.ToString() ?? string.Empty;
        if (beginEdit && EditableColumns.Contains(header))
            PurchaseGrid.BeginEdit();
    }

    private void NavigateCell(int rowDelta, int colDelta)
    {
        if (PurchaseGrid.SelectedItem is not PurchaseLineViewModel line) return;

        var rowIndex = PurchaseGrid.Items.IndexOf(line);
        var colIndex = GetColumnIndex();
        var newRow = Math.Clamp(rowIndex + rowDelta, 0, PurchaseGrid.Items.Count - 1);
        var newCol = Math.Clamp(colIndex + colDelta, 0, PurchaseGrid.Columns.Count - 1);

        if (PurchaseGrid.Items[newRow] is not PurchaseLineViewModel newLine) return;

        var header = PurchaseGrid.Columns[newCol].Header?.ToString() ?? string.Empty;
        FocusCell(newLine, newCol, EditableColumns.Contains(header));
    }

    private void MoveToNextRowItemColumn(PurchaseLineViewModel current)
    {
        var rowIndex = PurchaseGrid.Items.IndexOf(current);
        if (rowIndex + 1 < PurchaseGrid.Items.Count && PurchaseGrid.Items[rowIndex + 1] is PurchaseLineViewModel nextLine)
        {
            FocusCell(nextLine, 0, beginEdit: false);
            return;
        }

        if (ViewModel?.Lines.LastOrDefault(l => l.IsEmpty) is PurchaseLineViewModel empty)
            FocusItemColumn(empty);
    }

    private void MoveToNextColumn(PurchaseLineViewModel line)
    {
        var colIndex = GetColumnIndex();
        if (colIndex + 1 >= PurchaseGrid.Columns.Count - 1)
        {
            MoveToNextRowItemColumn(line);
            return;
        }

        var header = PurchaseGrid.Columns[colIndex + 1].Header?.ToString() ?? string.Empty;
        if (header == "Amount" || string.IsNullOrEmpty(header))
        {
            MoveToNextRowItemColumn(line);
            return;
        }

        FocusCell(line, colIndex + 1, EditableColumns.Contains(header));
    }

    private void MoveToPreviousColumn(PurchaseLineViewModel line)
    {
        var colIndex = GetColumnIndex();
        if (colIndex <= 0) return;

        var header = PurchaseGrid.Columns[colIndex - 1].Header?.ToString() ?? string.Empty;
        FocusCell(line, colIndex - 1, EditableColumns.Contains(header));
    }

    private bool IsSupplierSectionFocused()
    {
        var focused = Keyboard.FocusedElement as DependencyObject;
        return focused is not null && IsDescendantOf(focused, SupplierSearchBox)
               || focused is not null && IsDescendantOf(focused, SupplierResultsList);
    }

    private bool IsPurchaseDropdownFocused()
        => PurchasePopup.IsOpen && PurchaseListBox.IsKeyboardFocusWithin;

    private static bool IsDescendantOf(DependencyObject? node, DependencyObject ancestor)
    {
        var current = node;
        while (current is not null)
        {
            if (current == ancestor) return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    #region Supplier autosuggest

    private void SupplierSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        => HandleSupplierNavigationKey(e);

    private void SupplierResultsList_PreviewKeyDown(object sender, KeyEventArgs e)
        => HandleSupplierNavigationKey(e);

    private void HandleSupplierNavigationKey(KeyEventArgs e)
    {
        if (ViewModel is null) return;

        switch (e.Key)
        {
            case Key.Down:
                ViewModel.MoveSupplierSelection(1);
                ScrollSupplierToSelected();
                e.Handled = true;
                break;
            case Key.Up:
                ViewModel.MoveSupplierSelection(-1);
                ScrollSupplierToSelected();
                e.Handled = true;
                break;
            case Key.Enter:
                ViewModel.ConfirmSupplierSelection();
                SupplierSearchBox.Focus();
                e.Handled = true;
                break;
            case Key.Escape:
                ViewModel.DismissSupplierSuggestions();
                e.Handled = true;
                break;
        }
    }

    private void SupplierResultsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is null) return;
        ViewModel.ConfirmSupplierSelection();
    }

    private void ScrollSupplierToSelected()
    {
        if (ViewModel is null || ViewModel.SupplierSuggestionIndex < 0) return;
        if (ViewModel.SupplierSuggestionIndex < SupplierResultsList.Items.Count)
            SupplierResultsList.ScrollIntoView(SupplierResultsList.Items[ViewModel.SupplierSuggestionIndex]);
    }

    #endregion

    #region Purchase invoice dropdown

    private void PurchaseSelectorToggle_Checked(object sender, RoutedEventArgs e)
    {
        PurchasePopup.IsOpen = true;
        _purchaseListSelectionFromCode = true;
        PurchaseListBox.SelectedItem = ViewModel?.SelectedPurchase;
        _purchaseListSelectionFromCode = false;
        Dispatcher.BeginInvoke(FocusPurchaseList, DispatcherPriority.Loaded);
        if (PurchaseListBox.SelectedItem is PurchaseListItemDto purchase)
            _ = TryLoadPurchaseAsync(purchase, focusGrid: false);
    }

    private void PurchaseSelectorToggle_Unchecked(object sender, RoutedEventArgs e)
        => PurchasePopup.IsOpen = false;

    private void PurchasePopup_Closed(object? sender, EventArgs e)
    {
        if (PurchaseSelectorToggle.IsChecked == true)
            PurchaseSelectorToggle.IsChecked = false;
    }

    private CustomPopupPlacement[] PurchasePopup_PlacementCallback(Size popupSize, Size targetSize, Point offset)
    {
        return
        [
            new CustomPopupPlacement(
                new Point(targetSize.Width - popupSize.Width, targetSize.Height),
                PopupPrimaryAxis.Vertical)
        ];
    }

    private async void PurchaseListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_purchaseListSelectionFromCode || ViewModel is null || ViewModel.SuppressPurchaseLoad) return;
        if (PurchaseListBox.SelectedItem is not PurchaseListItemDto purchase) return;
        await TryLoadPurchaseAsync(purchase, focusGrid: false);
    }

    private async void PurchaseListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && PurchaseListBox.SelectedItem is PurchaseListItemDto purchase)
        {
            e.Handled = true;
            await CommitPurchaseSelectionAsync(purchase);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            PurchasePopup.IsOpen = false;
        }
    }

    private async void PurchaseListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (PurchaseListBox.SelectedItem is not PurchaseListItemDto purchase) return;
        await CommitPurchaseSelectionAsync(purchase);
    }

    private async Task CommitPurchaseSelectionAsync(PurchaseListItemDto purchase)
    {
        PurchasePopup.IsOpen = false;
        await TryLoadPurchaseAsync(purchase, focusGrid: true);
    }

    private async Task TryLoadPurchaseAsync(PurchaseListItemDto purchase, bool focusGrid)
    {
        if (ViewModel is null || ViewModel.SuppressPurchaseLoad) return;
        await ViewModel.LoadPurchaseFromDropdownAsync(purchase, focusGrid);
        if (!focusGrid && PurchasePopup.IsOpen)
            FocusPurchaseList();
    }

    private void FocusPurchaseList()
    {
        PurchaseListBox.Focus();
        if (PurchaseListBox.SelectedItem is null) return;
        if (PurchaseListBox.ItemContainerGenerator.ContainerFromItem(PurchaseListBox.SelectedItem) is ListBoxItem item)
            item.Focus();
    }

    #endregion

    private async void PurchaseGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is null || PurchaseGrid.SelectedItem is not PurchaseLineViewModel line) return;

        var columnHeader = PurchaseGrid.CurrentColumn?.Header?.ToString();

        if (e.Key is Key.Up or Key.Down or Key.Left or Key.Right)
        {
            e.Handled = true;
            CommitGridEdit();

            var rowDelta = e.Key == Key.Down ? 1 : e.Key == Key.Up ? -1 : 0;
            var colDelta = e.Key == Key.Right ? 1 : e.Key == Key.Left ? -1 : 0;
            NavigateCell(rowDelta, colDelta);
            return;
        }

        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Shift)
        {
            e.Handled = true;
            CommitGridEdit();
            MoveToPreviousColumn(line);
            return;
        }

        if (e.Key != Key.Enter) return;

        e.Handled = true;
        CommitGridEdit();

        if (columnHeader == "Item" || line.IsEmpty)
        {
            await ViewModel.BeginItemSelectionAsync(line);
            return;
        }

        MoveToNextColumn(line);
    }

    private async void PurchaseGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is null || PurchaseGrid.SelectedItem is not PurchaseLineViewModel line) return;
        if (PurchaseGrid.CurrentColumn?.Header?.ToString() != "Item") return;

        e.Handled = true;
        await ViewModel.BeginItemSelectionAsync(line);
    }

    private void PurchaseView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsSupplierSectionFocused() || IsPurchaseDropdownFocused()) return;

        var vm = ViewModel;
        if (vm is null) return;

        switch (e.Key)
        {
            case Key.F9:
                if (vm.SaveCommand.CanExecute(null)) vm.SaveCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                vm.NewPurchaseCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}
