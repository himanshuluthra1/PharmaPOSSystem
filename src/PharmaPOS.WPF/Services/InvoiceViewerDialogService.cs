using Microsoft.Extensions.DependencyInjection;
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
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICurrentUserService _currentUser;
    private readonly IDialogService _dialog;

    public InvoiceViewerDialogService(
        IServiceScopeFactory scopeFactory,
        ICurrentUserService currentUser,
        IDialogService dialog)
    {
        _scopeFactory = scopeFactory;
        _currentUser = currentUser;
        _dialog = dialog;
    }

    public async Task ShowSaleAsync(int saleId)
    {
        if (saleId <= 0) return;

        var branchId = _currentUser.CurrentUser?.BranchId;
        using var scope = _scopeFactory.CreateScope();
        var sales = scope.ServiceProvider.GetRequiredService<ISalesService>();
        var result = await sales.GetSaleReceiptAsync(saleId, branchId);
        if (result.IsFailure || result.Value is null)
        {
            _dialog.ShowError(result.Error ?? "Could not load the sale invoice.");
            return;
        }

        var receipt = result.Value;
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var window = new SaleBillViewerWindow(receipt)
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
        using var scope = _scopeFactory.CreateScope();
        var purchases = scope.ServiceProvider.GetRequiredService<IPurchaseService>();
        var result = await purchases.GetPurchaseForLoadAsync(purchaseId, branchId);
        if (result.IsFailure || result.Value is null)
        {
            _dialog.ShowError(result.Error ?? "Could not load the purchase invoice.");
            return;
        }

        var purchase = result.Value;
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var window = new PurchaseBillViewerWindow(purchase)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            window.ShowDialog();
        });
    }
}
