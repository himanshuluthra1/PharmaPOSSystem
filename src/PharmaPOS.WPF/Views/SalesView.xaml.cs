using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PharmaPOS.Application.Features.Sales;
using PharmaPOS.WPF.ViewModels.Sales;

namespace PharmaPOS.WPF.Views;

/// <summary>
/// Billing screen code-behind. Enter/Shift+Enter and arrow keys navigate the grid;
/// F3 for customer/save; bill dropdown loads on arrow browse.
/// </summary>
public partial class SalesView : UserControl
{
    private static readonly HashSet<string> EditableColumns = new(StringComparer.Ordinal)
        { "Qty", "MRP", "Disc%" };

    private bool _billListSelectionFromCode;

    public SalesView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => OnRequestItemFocus(ViewModel?.Cart.FirstOrDefault());
    }

    private SalesViewModel? ViewModel => DataContext as SalesViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is SalesViewModel oldVm)
        {
            oldVm.RequestItemFocus -= OnRequestItemFocus;
            oldVm.RequestCustomerFocus -= OnRequestCustomerFocus;
        }
        if (e.NewValue is SalesViewModel newVm)
        {
            newVm.RequestItemFocus += OnRequestItemFocus;
            newVm.RequestCustomerFocus += OnRequestCustomerFocus;
        }
    }

    private void OnRequestItemFocus(CartLineViewModel? line)
    {
        if (line is not null && !line.IsEmpty)
            FocusQuantityColumn(line);
        else
            FocusItemColumn(line);
    }

    private void OnRequestCustomerFocus()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            CustomerNameBox.Focus();
            CustomerNameBox.SelectAll();
        }));
    }

    private void CommitGridEdit()
    {
        CartGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        CartGrid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private int GetColumnIndex()
        => CartGrid.CurrentColumn is null ? 0 : CartGrid.Columns.IndexOf(CartGrid.CurrentColumn);

    private void FocusItemColumn(CartLineViewModel? line)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var target = line ?? ViewModel?.Cart.LastOrDefault(l => l.IsEmpty);
            if (target is null) return;

            CartGrid.SelectedItem = target;
            FocusCell(target, 0, beginEdit: false);
        }));
    }

    private void FocusQuantityColumn(CartLineViewModel line)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            CartGrid.SelectedItem = line;
            var qtyIndex = CartGrid.Columns.ToList().FindIndex(c => c.Header?.ToString() == "Qty");
            if (qtyIndex >= 0)
                FocusCell(line, qtyIndex, beginEdit: true);
        }));
    }

    private void FocusCell(CartLineViewModel line, string header, bool beginEdit)
    {
        var index = CartGrid.Columns.ToList().FindIndex(c => c.Header?.ToString() == header);
        if (index >= 0)
            FocusCell(line, index, beginEdit);
    }

    private void FocusCell(CartLineViewModel line, int columnIndex, bool beginEdit)
    {
        if (columnIndex < 0 || columnIndex >= CartGrid.Columns.Count) return;

        var column = CartGrid.Columns[columnIndex];
        CartGrid.SelectedItem = line;
        CartGrid.CurrentCell = new DataGridCellInfo(line, column);
        CartGrid.Focus();
        if (CartGrid.ItemContainerGenerator.ContainerFromItem(line) is DataGridRow row)
            row.Focus();

        var header = column.Header?.ToString() ?? string.Empty;
        if (beginEdit && EditableColumns.Contains(header))
            CartGrid.BeginEdit();
    }

    private void NavigateCell(int rowDelta, int colDelta)
    {
        if (CartGrid.SelectedItem is not CartLineViewModel line) return;

        var rowIndex = CartGrid.Items.IndexOf(line);
        var colIndex = GetColumnIndex();
        var newRow = Math.Clamp(rowIndex + rowDelta, 0, CartGrid.Items.Count - 1);
        var newCol = Math.Clamp(colIndex + colDelta, 0, CartGrid.Columns.Count - 1);

        if (CartGrid.Items[newRow] is not CartLineViewModel newLine) return;

        var header = CartGrid.Columns[newCol].Header?.ToString() ?? string.Empty;
        var beginEdit = EditableColumns.Contains(header);
        FocusCell(newLine, newCol, beginEdit);
    }

    private void MoveToNextRowItemColumn(CartLineViewModel current)
    {
        var rowIndex = CartGrid.Items.IndexOf(current);
        if (rowIndex + 1 < CartGrid.Items.Count && CartGrid.Items[rowIndex + 1] is CartLineViewModel nextLine)
        {
            FocusCell(nextLine, 0, beginEdit: false);
            return;
        }

        if (ViewModel?.Cart.LastOrDefault(l => l.IsEmpty) is CartLineViewModel empty)
            FocusItemColumn(empty);
    }

    private void MoveToPreviousRowDiscColumn(CartLineViewModel current)
    {
        var rowIndex = CartGrid.Items.IndexOf(current);
        if (rowIndex > 0 && CartGrid.Items[rowIndex - 1] is CartLineViewModel prevLine)
            FocusCell(prevLine, "Disc%", beginEdit: true);
    }

    private void MoveToNextColumn(CartLineViewModel line)
    {
        var colIndex = GetColumnIndex();
        if (colIndex + 1 >= CartGrid.Columns.Count)
        {
            MoveToNextRowItemColumn(line);
            return;
        }

        var header = CartGrid.Columns[colIndex + 1].Header?.ToString() ?? string.Empty;
        FocusCell(line, colIndex + 1, EditableColumns.Contains(header));
    }

    private void MoveToPreviousColumn(CartLineViewModel line)
    {
        var colIndex = GetColumnIndex();
        if (colIndex <= 0)
        {
            MoveToPreviousRowDiscColumn(line);
            return;
        }

        var header = CartGrid.Columns[colIndex - 1].Header?.ToString() ?? string.Empty;
        FocusCell(line, colIndex - 1, EditableColumns.Contains(header));
    }

    private bool IsLastGridColumn()
    {
        var currentColumn = CartGrid.CurrentColumn;
        if (currentColumn is null) return false;
        return CartGrid.Columns.IndexOf(currentColumn) == CartGrid.Columns.Count - 1;
    }

    private void MoveToNextCellLikeTab(CartLineViewModel line)
    {
        var columnHeader = CartGrid.CurrentColumn?.Header?.ToString();

        if (columnHeader == "Qty")
        {
            FocusCell(line, "MRP", beginEdit: true);
            return;
        }

        if (columnHeader == "MRP")
        {
            FocusCell(line, "Disc%", beginEdit: true);
            return;
        }

        if (columnHeader == "Disc%")
        {
            MoveToNextRowItemColumn(line);
            return;
        }

        MoveToNextColumn(line);
    }

    private void MoveToPreviousCellLikeShiftTab(CartLineViewModel line)
    {
        var columnHeader = CartGrid.CurrentColumn?.Header?.ToString();

        if (columnHeader == "Disc%")
        {
            FocusCell(line, "MRP", beginEdit: true);
            return;
        }

        if (columnHeader == "MRP")
        {
            FocusCell(line, "Qty", beginEdit: true);
            return;
        }

        if (columnHeader == "Qty")
        {
            FocusCell(line, "Item", beginEdit: false);
            return;
        }

        if (columnHeader == "Item")
        {
            MoveToPreviousRowDiscColumn(line);
            return;
        }

        if (IsLastGridColumn() || columnHeader == "Amount")
        {
            MoveToPreviousColumn(line);
            return;
        }

        MoveToPreviousColumn(line);
    }

    private bool IsCustomerSectionFocused()
    {
        var focused = Keyboard.FocusedElement as DependencyObject;
        return focused is not null && IsDescendantOf(focused, CustomerPanel);
    }

    private bool IsGridFocused()
        => CartGrid.IsKeyboardFocusWithin || CartGrid.IsFocused;

    private static bool IsDescendantOf(DependencyObject element, DependencyObject ancestor)
    {
        var current = element;
        while (current is not null)
        {
            if (current == ancestor) return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private void BillSelectorToggle_Checked(object sender, RoutedEventArgs e)
    {
        BillPopup.IsOpen = true;
        _billListSelectionFromCode = true;
        BillListBox.SelectedItem = ViewModel?.SelectedBill;
        _billListSelectionFromCode = false;
        Dispatcher.BeginInvoke(FocusBillList, DispatcherPriority.Loaded);
        if (BillListBox.SelectedItem is SaleListItemDto bill)
            _ = TryLoadBillAsync(bill, focusGrid: false);
    }

    private void BillSelectorToggle_Unchecked(object sender, RoutedEventArgs e)
        => BillPopup.IsOpen = false;

    private void BillPopup_Closed(object? sender, EventArgs e)
    {
        if (BillSelectorToggle.IsChecked == true)
            BillSelectorToggle.IsChecked = false;
    }

    private CustomPopupPlacement[] BillPopup_PlacementCallback(
        Size popupSize, Size targetSize, Point offset)
    {
        return
        [
            new CustomPopupPlacement(
                new Point(targetSize.Width - popupSize.Width, targetSize.Height),
                PopupPrimaryAxis.Vertical)
        ];
    }

    private async void BillListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_billListSelectionFromCode || ViewModel is null || ViewModel.SuppressBillLoad) return;
        if (BillListBox.SelectedItem is not SaleListItemDto bill) return;
        await TryLoadBillAsync(bill, focusGrid: false);
    }

    private async void BillListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && BillListBox.SelectedItem is SaleListItemDto bill)
        {
            e.Handled = true;
            await CommitBillSelectionAsync(bill);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            BillPopup.IsOpen = false;
        }
    }

    private async void BillListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (BillListBox.SelectedItem is not SaleListItemDto bill) return;
        await CommitBillSelectionAsync(bill);
    }

    private async Task CommitBillSelectionAsync(SaleListItemDto bill)
    {
        BillPopup.IsOpen = false;
        await TryLoadBillAsync(bill, focusGrid: true);
    }

    private async Task TryLoadBillAsync(SaleListItemDto bill, bool focusGrid)
    {
        if (ViewModel is null || ViewModel.SuppressBillLoad) return;
        await ViewModel.LoadBillFromDropdownAsync(bill, focusGrid);
        if (!focusGrid && BillPopup.IsOpen)
            FocusBillList();
    }

    private void FocusBillList()
    {
        BillListBox.Focus();
        if (BillListBox.SelectedItem is null) return;
        if (BillListBox.ItemContainerGenerator.ContainerFromItem(BillListBox.SelectedItem) is ListBoxItem item)
            item.Focus();
    }

    private async void CartGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is null || CartGrid.SelectedItem is not CartLineViewModel line) return;
        if (CartGrid.CurrentColumn?.Header?.ToString() != "Item") return;

        e.Handled = true;
        await ViewModel.BeginItemSelectionAsync(line);
    }

    private async void CartGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is null || CartGrid.SelectedItem is not CartLineViewModel line) return;

        var columnHeader = CartGrid.CurrentColumn?.Header?.ToString();

        if (e.Key is Key.Up or Key.Down or Key.Left or Key.Right)
        {
            e.Handled = true;
            CommitGridEdit();

            var rowDelta = e.Key == Key.Down ? 1 : e.Key == Key.Up ? -1 : 0;
            var colDelta = e.Key == Key.Right ? 1 : e.Key == Key.Left ? -1 : 0;
            NavigateCell(rowDelta, colDelta);
            return;
        }

        if (e.Key == Key.Space && columnHeader == "Item")
        {
            e.Handled = true;
            await ViewModel.BeginItemSelectionAsync(line);
            return;
        }

        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Shift)
        {
            e.Handled = true;
            CommitGridEdit();
            MoveToPreviousCellLikeShiftTab(line);
            return;
        }

        if (e.Key != Key.Enter) return;

        e.Handled = true;
        CommitGridEdit();

        if (columnHeader == "Item")
        {
            await ViewModel.BeginItemSelectionAsync(line);
            return;
        }

        if (IsLastGridColumn() || columnHeader == "Amount")
        {
            MoveToNextRowItemColumn(line);
            return;
        }

        MoveToNextCellLikeTab(line);
    }

    private async void SalesView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F3 || ViewModel is null) return;

        if (IsCustomerSectionFocused())
        {
            e.Handled = true;
            await ViewModel.TrySaveFromCustomerAsync();
            return;
        }

        if (IsGridFocused())
        {
            e.Handled = true;
            ViewModel.GoToCustomerOrWarn();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var vm = ViewModel;
        if (vm is null) return;

        switch (e.Key)
        {
            case Key.F9:
                if (vm.SaveCommand.CanExecute(null)) vm.SaveCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                vm.NewBillCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}
