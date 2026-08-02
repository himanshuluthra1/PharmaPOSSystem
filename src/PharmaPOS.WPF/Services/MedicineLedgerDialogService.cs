using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Inventory;
using PharmaPOS.Application.Features.Masters;
using PharmaPOS.WPF.Views;

namespace PharmaPOS.WPF.Services;

public interface IMedicineLedgerDialogService
{
    /// <summary>
    /// If focus is on a medicine-bearing grid row, shows that medicine's stock ledger.
    /// Returns true when the shortcut was handled (even if nothing to show).
    /// </summary>
    Task<bool> TryShowForFocusedMedicineAsync();

    Task ShowAsync(int medicineId, string? medicineName = null, int? batchId = null);
}

public sealed class MedicineLedgerDialogService : IMedicineLedgerDialogService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICurrentUserService _currentUser;
    private readonly IDialogService _dialog;

    public MedicineLedgerDialogService(
        IServiceScopeFactory scopeFactory,
        ICurrentUserService currentUser,
        IDialogService dialog)
    {
        _scopeFactory = scopeFactory;
        _currentUser = currentUser;
        _dialog = dialog;
    }

    public async Task<bool> TryShowForFocusedMedicineAsync()
    {
        if (!TryResolveFocusedMedicine(out var medicineId, out var medicineName, out var batchId))
            return false;

        if (medicineId <= 0)
        {
            _dialog.ShowInfo("Select a row with a medicine first.", "Medicine ledger");
            return true;
        }

        await ShowAsync(medicineId, medicineName, batchId);
        return true;
    }

    public async Task ShowAsync(int medicineId, string? medicineName = null, int? batchId = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        var masters = scope.ServiceProvider.GetRequiredService<IMastersService>();
        var branchId = _currentUser.CurrentUser?.BranchId;

        if (string.IsNullOrWhiteSpace(medicineName))
        {
            var med = await masters.GetMedicineAsync(medicineId);
            medicineName = med?.Name ?? $"Medicine #{medicineId}";
        }

        var rows = await inventory.GetStockLedgerAsync(
            term: null,
            medicineId: medicineId,
            batchId: batchId,
            branchId: branchId,
            take: 500);

        var owner = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                    ?? System.Windows.Application.Current.MainWindow;
        var window = new MedicineLedgerPopupWindow(medicineName!, rows)
        {
            Owner = owner
        };
        window.ShowDialog();
    }

    private static bool TryResolveFocusedMedicine(out int medicineId, out string? medicineName, out int? batchId)
    {
        medicineId = 0;
        medicineName = null;
        batchId = null;

        var focused = Keyboard.FocusedElement as DependencyObject;
        var grid = FindAncestor<DataGrid>(focused);
        if (grid is null && focused is FrameworkElement fe)
            grid = FindDescendantDataGridWithSelection(Window.GetWindow(fe));

        if (grid?.SelectedItem is not null)
            return TryReadMedicineFromRow(grid.SelectedItem, out medicineId, out medicineName, out batchId);

        // Medicine search popup uses a ListBox, not a DataGrid.
        var list = FindAncestor<ListBox>(focused);
        if (list is null && focused is FrameworkElement fe2)
            list = FindDescendantListBoxWithSelection(Window.GetWindow(fe2));

        if (list?.SelectedItem is not null)
            return TryReadMedicineFromRow(list.SelectedItem, out medicineId, out medicineName, out batchId);

        return false;
    }

    private static bool TryReadMedicineFromRow(
        object row, out int medicineId, out string? medicineName, out int? batchId)
    {
        medicineId = 0;
        medicineName = GetStringProp(row, "MedicineName")
                       ?? GetStringProp(row, "Name")
                       ?? GetStringProp(row, "OcrItemName")
                       ?? GetStringProp(row, "MatchedMedicineName");
        batchId = GetNullableIntProp(row, "BatchId") ?? GetNullableIntProp(row, "MedicineBatchId");

        var mid = GetNullableIntProp(row, "MedicineId");
        if (mid is > 0)
        {
            medicineId = mid.Value;
            return true;
        }

        // Masters / search lookup rows use Id.
        var typeName = row.GetType().Name;
        if (typeName.Contains("Medicine", StringComparison.OrdinalIgnoreCase))
        {
            var id = GetNullableIntProp(row, "Id");
            if (id is > 0)
            {
                medicineId = id.Value;
                return true;
            }
        }

        return false;
    }

    private static int? GetNullableIntProp(object row, string name)
    {
        var p = row.GetType().GetProperty(name);
        if (p is null) return null;
        var v = p.GetValue(row);
        if (v is null) return null;
        if (v is int i) return i;
        if (v is long l) return (int)l;
        return null;
    }

    private static string? GetStringProp(object row, string name)
    {
        var p = row.GetType().GetProperty(name);
        return p?.GetValue(row) as string;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }

    private static DataGrid? FindDescendantDataGridWithSelection(DependencyObject? root)
    {
        if (root is null) return null;
        if (root is DataGrid { SelectedItem: not null } dg) return dg;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var found = FindDescendantDataGridWithSelection(child);
            if (found is not null) return found;
        }
        return null;
    }

    private static ListBox? FindDescendantListBoxWithSelection(DependencyObject? root)
    {
        if (root is null) return null;
        if (root is ListBox { SelectedItem: not null } lb) return lb;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var found = FindDescendantListBoxWithSelection(child);
            if (found is not null) return found;
        }
        return null;
    }
}
