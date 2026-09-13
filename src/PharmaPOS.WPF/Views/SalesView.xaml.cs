using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PharmaPOS.Application.Features.Sales;
using PharmaPOS.WPF.Services;
using PharmaPOS.WPF.ViewModels.Sales;

namespace PharmaPOS.WPF.Views;

/// <summary>
/// Billing screen code-behind. Enter/Tab and arrow keys navigate only editable cart fields;
/// empty (no medicine) rows only allow Item selection.
/// </summary>
public partial class SalesView : UserControl
{
    /// <summary>Numeric/text cells that support BeginEdit (Item opens the picker instead).</summary>
    private static readonly HashSet<string> EditableColumns = new(StringComparer.Ordinal)
        { "Batch", "Expiry", "Qty", "MRP", "Sale", "Disc %", "GST %" };

    /// <summary>Focus order for a filled medicine line.</summary>
    private static readonly string[] MedicineFocusColumns = ["Item", "Batch", "Expiry", "Qty", "MRP", "Sale", "Disc %", "GST %"];

    private static readonly string[] EmptyRowFocusColumns = ["Item"];

    private readonly UsbBarcodeWedge _barcodeWedge = new();

    public SalesView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _barcodeWedge.BarcodeScanned += code =>
        {
            Dispatcher.InvokeAsync(async () =>
            {
                if (ViewModel is null) return;
                await ViewModel.TryAddByBarcodeAsync(code);
            });
        };
    }

    private SalesViewModel? ViewModel => DataContext as SalesViewModel;
    private IUiLayoutService? _layout;
    private DataGridLayoutTracker? _gridLayoutTracker;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _layout ??= App.Services.GetService(typeof(IUiLayoutService)) as IUiLayoutService;
        if (_layout is null) return;

        SideColumn.Width = new GridLength(_layout.GetSidePanelWidth(UiLayoutService.SalesKey));
        _gridLayoutTracker?.Dispose();
        _gridLayoutTracker = new DataGridLayoutTracker(CartGrid, UiLayoutService.SalesKey, _layout);
        OnRequestItemFocus(ViewModel?.Cart.FirstOrDefault());
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        PersistSidePanelWidth();
        _gridLayoutTracker?.Dispose();
        _gridLayoutTracker = null;
    }

    private void SideSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        => PersistSidePanelWidth();

    private void PersistSidePanelWidth()
    {
        if (_layout is null || SideColumn.ActualWidth < 1) return;
        _layout.SetSidePanelWidth(UiLayoutService.SalesKey, SideColumn.ActualWidth);
    }

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

        if (ViewModel is not null)
            _ = ViewModel.RefreshCartLineDetailAsync(line);
    }

    private void CartGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is null) return;
        _ = ViewModel.RefreshCartLineDetailAsync(CartGrid.SelectedItem as CartLineViewModel);
    }

    private void OnRequestCustomerFocus()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            CustomerNameBox.Focus();
            CustomerNameBox.SelectAll();
        }));
    }

    private void CustomerField_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not System.Windows.Controls.TextBox box) return;

        // Enter moves like Tab within the customer section.
        var next = box.Name switch
        {
            nameof(CustomerNameBox) => DoctorNameBox,
            nameof(DoctorNameBox) => CustomerMobileBox,
            nameof(CustomerMobileBox) => CustomerAddressBox,
            _ => null
        };
        if (next is null) return;
        next.Focus();
        next.SelectAll();
        e.Handled = true;
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
        var header = column.Header?.ToString() ?? string.Empty;

        // Empty / return lines may only land on Item — never edit other cells.
        if (!line.IsEditable && header != "Item")
        {
            FocusCell(line, "Item", beginEdit: false);
            return;
        }

        CartGrid.SelectedItem = line;
        CartGrid.CurrentCell = new DataGridCellInfo(line, column);
        CartGrid.Focus();
        if (CartGrid.ItemContainerGenerator.ContainerFromItem(line) is DataGridRow row)
            row.Focus();

        if (beginEdit && CanBeginEdit(line, header))
            CartGrid.BeginEdit();
    }

    private static string[] GetFocusColumns(CartLineViewModel line)
        => line.IsEmpty || line.IsReturnLine ? EmptyRowFocusColumns : MedicineFocusColumns;

    private static bool CanBeginEdit(CartLineViewModel line, string? header)
        => line.IsEditable && header is not null && EditableColumns.Contains(header);

    private void FocusFocusColumn(CartLineViewModel line, string preferredHeader)
    {
        var columns = GetFocusColumns(line);
        var header = columns.Contains(preferredHeader, StringComparer.Ordinal)
            ? preferredHeader
            : columns[0];
        FocusCell(line, header, CanBeginEdit(line, header));
    }

    private void NavigateCell(int rowDelta, int colDelta)
    {
        if (CartGrid.SelectedItem is not CartLineViewModel line) return;

        var rowIndex = CartGrid.Items.IndexOf(line);
        if (rowDelta != 0)
        {
            var newRow = Math.Clamp(rowIndex + rowDelta, 0, CartGrid.Items.Count - 1);
            if (CartGrid.Items[newRow] is not CartLineViewModel newLine) return;
            var preferred = CartGrid.CurrentColumn?.Header?.ToString() ?? "Item";
            FocusFocusColumn(newLine, preferred);
            return;
        }

        if (colDelta > 0)
            MoveToNextCellLikeTab(line);
        else if (colDelta < 0)
            MoveToPreviousCellLikeShiftTab(line);
    }

    private void MoveToNextRowItemColumn(CartLineViewModel current)
    {
        var rowIndex = CartGrid.Items.IndexOf(current);
        if (rowIndex + 1 < CartGrid.Items.Count && CartGrid.Items[rowIndex + 1] is CartLineViewModel nextLine)
        {
            FocusFocusColumn(nextLine, "Item");
            return;
        }

        if (ViewModel?.Cart.LastOrDefault(l => l.IsEmpty) is CartLineViewModel empty)
            FocusItemColumn(empty);
    }

    private void MoveToPreviousRowLastEditable(CartLineViewModel current)
    {
        var rowIndex = CartGrid.Items.IndexOf(current);
        if (rowIndex <= 0) return;
        if (CartGrid.Items[rowIndex - 1] is not CartLineViewModel prevLine) return;

        var columns = GetFocusColumns(prevLine);
        FocusFocusColumn(prevLine, columns[^1]);
    }

    private void MoveToNextCellLikeTab(CartLineViewModel line)
    {
        var columns = GetFocusColumns(line);
        var current = CartGrid.CurrentColumn?.Header?.ToString() ?? "Item";
        var idx = Array.FindIndex(columns, h => h == current);

        if (idx < 0)
        {
            var colIndex = GetColumnIndex();
            for (var i = colIndex + 1; i < CartGrid.Columns.Count; i++)
            {
                var header = CartGrid.Columns[i].Header?.ToString() ?? string.Empty;
                if (columns.Contains(header, StringComparer.Ordinal))
                {
                    FocusFocusColumn(line, header);
                    return;
                }
            }

            MoveToNextRowItemColumn(line);
            return;
        }

        if (idx >= columns.Length - 1)
        {
            MoveToNextRowItemColumn(line);
            return;
        }

        FocusFocusColumn(line, columns[idx + 1]);
    }

    private void MoveToPreviousCellLikeShiftTab(CartLineViewModel line)
    {
        var columns = GetFocusColumns(line);
        var current = CartGrid.CurrentColumn?.Header?.ToString() ?? "Item";
        var idx = Array.FindIndex(columns, h => h == current);

        if (idx < 0)
        {
            var colIndex = GetColumnIndex();
            for (var i = colIndex - 1; i >= 0; i--)
            {
                var header = CartGrid.Columns[i].Header?.ToString() ?? string.Empty;
                if (columns.Contains(header, StringComparer.Ordinal))
                {
                    FocusFocusColumn(line, header);
                    return;
                }
            }

            MoveToPreviousRowLastEditable(line);
            return;
        }

        if (idx <= 0)
        {
            MoveToPreviousRowLastEditable(line);
            return;
        }

        FocusFocusColumn(line, columns[idx - 1]);
    }

    private bool IsLastFocusColumn(CartLineViewModel line)
    {
        var columns = GetFocusColumns(line);
        var current = CartGrid.CurrentColumn?.Header?.ToString();
        return current is not null && columns[^1] == current;
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

    private static Key GetKey(KeyEventArgs e)
        => e.Key == Key.System ? e.SystemKey : e.Key;

    private async Task TryShowMedicineDetailForSelectedRowAsync()
    {
        if (ViewModel is null) return;
        if (CartGrid.SelectedItem is not CartLineViewModel line) return;
        CommitGridEdit();
        await ViewModel.ShowMedicineDetailAsync(line);
    }

    private async Task TryReplaceWithSubstituteForSelectedRowAsync()
    {
        if (ViewModel is null) return;
        if (CartGrid.SelectedItem is not CartLineViewModel line) return;
        CommitGridEdit();
        await ViewModel.ReplaceWithSubstituteAsync(line);
    }

    private async Task TryAddSelectedLineToShortageBookAsync()
    {
        if (ViewModel is null) return;
        if (CartGrid.SelectedItem is not CartLineViewModel line) return;
        CommitGridEdit();
        await ViewModel.RecordShortageFromCartLineAsync(line);
    }

    private void BillSelectorToggle_Checked(object sender, RoutedEventArgs e)
    {
        BillPopup.IsOpen = true;
        BillListBox.SelectedItem = ViewModel?.SelectedBill;
    }

    private void BillPopup_Opened(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(FocusBillList, DispatcherPriority.Input);

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

    private void BillListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
        {
            Dispatcher.BeginInvoke(
                () => _ = LoadHighlightedBillFromDropdownAsync(),
                DispatcherPriority.Input);
            return;
        }

        if (e.Key == Key.Enter && BillListBox.SelectedItem is SaleListItemDto bill)
        {
            e.Handled = true;
            if (Keyboard.Modifiers == ModifierKeys.Control)
                _ = InvoiceDrillDown.OpenSaleAsync(bill.SaleId);
            else
                _ = CommitBillSelectionAsync(bill);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            BillPopup.IsOpen = false;
        }
    }

    private void BillListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (BillListBox.SelectedItem is not SaleListItemDto bill || bill.SaleId <= 0) return;
        e.Handled = true;
        _ = InvoiceDrillDown.OpenSaleAsync(bill.SaleId);
    }

    private async void BillListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!IsInvoiceNumberClick(e.OriginalSource as DependencyObject))
            return;

        var bill = GetBillFromClick(e.OriginalSource as DependencyObject);
        if (bill is null) return;

        BillListBox.SelectedItem = bill;

        await CommitBillSelectionAsync(bill);
    }

    private static bool IsInvoiceNumberClick(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { Tag: string tag } && tag == "InvoiceNumber")
                return true;

            if (source is ListBoxItem)
                return false;

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private static SaleListItemDto? GetBillFromClick(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ListBoxItem { DataContext: SaleListItemDto bill })
                return bill;

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private async Task LoadHighlightedBillFromDropdownAsync()
    {
        if (!BillPopup.IsOpen) return;
        if (ViewModel is null || ViewModel.SuppressBillLoad) return;
        if (BillListBox.SelectedItem is not SaleListItemDto bill) return;
        await TryLoadBillAsync(bill, focusGrid: false);
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
        if (BillListBox.Items.Count == 0) return;

        BillListBox.Focus();
        Keyboard.Focus(BillListBox);

        if (BillListBox.SelectedItem is null)
            BillListBox.SelectedIndex = 0;

        BillListBox.UpdateLayout();
        if (BillListBox.ItemContainerGenerator.ContainerFromItem(BillListBox.SelectedItem) is ListBoxItem item)
            item.Focus();
    }

    private void CartGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.Item is not CartLineViewModel line)
        {
            e.Cancel = true;
            return;
        }

        // Empty rows (no medicine) and return lines are not editable.
        if (!line.IsEditable)
        {
            e.Cancel = true;
            return;
        }

        var header = e.Column.Header?.ToString();
        if (!CanBeginEdit(line, header))
            e.Cancel = true;
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
        if (e.Handled) return;
        if (ViewModel is null || CartGrid.SelectedItem is not CartLineViewModel line) return;

        var key = GetKey(e);
        var columnHeader = CartGrid.CurrentColumn?.Header?.ToString();

        if (key is Key.Up or Key.Down or Key.Left or Key.Right)
        {
            e.Handled = true;
            CommitGridEdit();

            var rowDelta = key == Key.Down ? 1 : key == Key.Up ? -1 : 0;
            var colDelta = key == Key.Right ? 1 : key == Key.Left ? -1 : 0;
            NavigateCell(rowDelta, colDelta);
            return;
        }

        if (key == Key.Tab)
        {
            e.Handled = true;
            CommitGridEdit();
            if (Keyboard.Modifiers == ModifierKeys.Shift)
                MoveToPreviousCellLikeShiftTab(line);
            else
                MoveToNextCellLikeTab(line);
            return;
        }

        // Empty row: only Item + Enter/Space to pick medicine — no other cell input.
        if (line.IsEmpty)
        {
            if (columnHeader != "Item")
            {
                e.Handled = true;
                FocusFocusColumn(line, "Item");
                return;
            }

            if (key is Key.Enter or Key.Space)
            {
                e.Handled = true;
                await ViewModel.BeginItemSelectionAsync(line);
                return;
            }

            // Swallow typing so empty qty/price cells never receive input if focus drifted.
            if (key is not (Key.Escape or Key.Tab or Key.Left or Key.Right or Key.Up or Key.Down
                or Key.F3 or Key.F4 or Key.F5 or Key.F6 or Key.F7 or Key.F8 or Key.System or Key.LeftAlt or Key.RightAlt
                or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift))
                e.Handled = true;

            return;
        }

        if (key == Key.Space && columnHeader == "Item")
        {
            e.Handled = true;
            await ViewModel.BeginItemSelectionAsync(line);
            return;
        }

        if (key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Shift)
        {
            e.Handled = true;
            CommitGridEdit();
            MoveToPreviousCellLikeShiftTab(line);
            return;
        }

        if (key != Key.Enter) return;

        e.Handled = true;
        CommitGridEdit();

        if (columnHeader == "Item")
        {
            await ViewModel.BeginItemSelectionAsync(line);
            return;
        }

        MoveToNextCellLikeTab(line);
    }

    private async void BarcodeScanBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is null) return;
        if (e.Key is not (Key.Enter or Key.Return)) return;

        e.Handled = true;
        var code = BarcodeScanBox.Text?.Trim() ?? string.Empty;
        BarcodeScanBox.Clear();
        if (code.Length >= 3)
            await ViewModel.TryAddByBarcodeAsync(code);
    }

    private async void SalesView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is null) return;
        var key = GetKey(e);

        if (TryHandleMultiCustomerHotkeys(ViewModel, key, e))
            return;

        // USB wedge: only when not typing in normal text fields (except the scan box).
        var focused = Keyboard.FocusedElement;
        var inScanBox = ReferenceEquals(focused, BarcodeScanBox);
        var inOtherText = focused is TextBox or PasswordBox && !inScanBox;
        if (!inOtherText && !inScanBox)
        {
            if (_barcodeWedge.ProcessKeyDown(key, Keyboard.Modifiers))
            {
                e.Handled = true;
                return;
            }
        }
        else if (!inScanBox)
        {
            _barcodeWedge.Reset();
        }

        if (key == Key.F4)
        {
            e.Handled = true;
            await TryShowMedicineDetailForSelectedRowAsync();
            return;
        }

        if (key == Key.F5)
        {
            e.Handled = true;
            await TryReplaceWithSubstituteForSelectedRowAsync();
            return;
        }

        if (key == Key.F6)
        {
            e.Handled = true;
            if (ViewModel.LastSaleRefillCommand.CanExecute(null))
                ViewModel.LastSaleRefillCommand.Execute(null);
            return;
        }

        if (key == Key.F7)
        {
            e.Handled = true;
            await TryAddSelectedLineToShortageBookAsync();
            return;
        }

        if (key == Key.F8)
        {
            e.Handled = true;
            if (ViewModel.OpenSaleReturnCommand.CanExecute(null))
                ViewModel.OpenSaleReturnCommand.Execute(null);
            return;
        }

        if (key != Key.F3) return;

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

    private bool TryHandleMultiCustomerHotkeys(SalesViewModel vm, Key key, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        if ((mods & ModifierKeys.Control) != ModifierKeys.Control)
            return false;
        if ((mods & (ModifierKeys.Alt | ModifierKeys.Shift)) != ModifierKeys.None)
            return false;

        // Ctrl+T — another customer bill
        if (key == Key.T)
        {
            CommitGridEdit();
            if (vm.NewCustomerBillCommand.CanExecute(null))
                vm.NewCustomerBillCommand.Execute(null);
            e.Handled = true;
            return true;
        }

        // Ctrl+W — close current customer bill
        if (key == Key.W)
        {
            CommitGridEdit();
            if (vm.CloseCustomerBillCommand.CanExecute(null))
                vm.CloseCustomerBillCommand.Execute(null);
            e.Handled = true;
            return true;
        }

        // Ctrl+1..4 — switch open bill
        var billNumber = key switch
        {
            Key.D1 or Key.NumPad1 => 1,
            Key.D2 or Key.NumPad2 => 2,
            Key.D3 or Key.NumPad3 => 3,
            Key.D4 or Key.NumPad4 => 4,
            _ => 0
        };
        if (billNumber == 0)
            return false;

        CommitGridEdit();
        if (vm.TrySwitchToBillNumber(billNumber))
            e.Handled = true;
        return true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var vm = ViewModel;
        var key = GetKey(e);

        if (vm is not null && TryHandleMultiCustomerHotkeys(vm, key, e))
            return;

        if (vm is not null)
        {
            switch (key)
            {
                case Key.F4:
                    e.Handled = true;
                    CommitGridEdit();
                    if (CartGrid.SelectedItem is CartLineViewModel line)
                        _ = vm.ShowMedicineDetailAsync(line);
                    return;
                case Key.F5:
                    e.Handled = true;
                    CommitGridEdit();
                    if (CartGrid.SelectedItem is CartLineViewModel substituteLine)
                        _ = vm.ReplaceWithSubstituteAsync(substituteLine);
                    return;
                case Key.F6:
                    e.Handled = true;
                    CommitGridEdit();
                    if (vm.LastSaleRefillCommand.CanExecute(null))
                        vm.LastSaleRefillCommand.Execute(null);
                    return;
                case Key.F7:
                    e.Handled = true;
                    CommitGridEdit();
                    if (CartGrid.SelectedItem is CartLineViewModel shortageLine)
                        _ = vm.RecordShortageFromCartLineAsync(shortageLine);
                    return;
                case Key.F8:
                    if (vm.OpenSaleReturnCommand.CanExecute(null))
                        vm.OpenSaleReturnCommand.Execute(null);
                    e.Handled = true;
                    return;
                case Key.F9:
                    // Prefer update/save when editing is allowed; otherwise print the loaded bill.
                    if (vm.SaveCommand.CanExecute(null))
                        vm.SaveCommand.Execute(null);
                    else if (vm.IsEditing && vm.PrintCommand.CanExecute(null))
                        vm.PrintCommand.Execute(null);
                    e.Handled = true;
                    return;
                // Escape does not clear the bill — use New / Cancel button.
            }
        }

        base.OnKeyDown(e);
    }
}
