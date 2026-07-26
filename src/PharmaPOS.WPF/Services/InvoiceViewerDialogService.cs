using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Purchases;
using PharmaPOS.Application.Features.Sales;
using PharmaPOS.WPF.Views;

namespace PharmaPOS.WPF.Services;

/// <summary>Opens sale/purchase invoices in a modal popup over the current screen.</summary>
public interface IInvoiceViewerDialogService
{
    Task ShowSaleAsync(int saleId);
    Task ShowPurchaseAsync(int purchaseId);
}

public sealed class InvoiceViewerDialogService : IInvoiceViewerDialogService
{
    private readonly ISalesService _sales;
    private readonly IPurchaseService _purchases;
    private readonly ICurrentUserService _currentUser;
    private readonly IDialogService _dialog;

    public InvoiceViewerDialogService(
        ISalesService sales,
        IPurchaseService purchases,
        ICurrentUserService currentUser,
        IDialogService dialog)
    {
        _sales = sales;
        _purchases = purchases;
        _currentUser = currentUser;
        _dialog = dialog;
    }

    public async Task ShowSaleAsync(int saleId)
    {
        if (saleId <= 0) return;

        var branchId = _currentUser.CurrentUser?.BranchId;
        var result = await _sales.GetSaleReceiptAsync(saleId, branchId);
        if (result.IsFailure || result.Value is null)
        {
            _dialog.ShowError(result.Error ?? "Could not load the sale invoice.");
            return;
        }

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var window = new SaleBillViewerWindow(result.Value)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            window.ShowDialog();
        });
    }

    public async Task ShowPurchaseAsync(int purchaseId)
    {
        if (purchaseId <= 0) return;

        var branchId = _currentUser.CurrentUser?.BranchId;
        var result = await _purchases.GetPurchaseForLoadAsync(purchaseId, branchId);
        if (result.IsFailure || result.Value is null)
        {
            _dialog.ShowError(result.Error ?? "Could not load the purchase invoice.");
            return;
        }

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var window = new PurchaseBillViewerWindow(result.Value)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            window.ShowDialog();
        });
    }
}
