using Microsoft.Extensions.DependencyInjection;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Purchases;
using PharmaPOS.WPF.ViewModels.Purchases;
using PharmaPOS.WPF.Views;

namespace PharmaPOS.WPF.Services;

public class PurchaseSearchService : IPurchaseSearchService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICurrentUserService _currentUser;

    public PurchaseSearchService(IServiceScopeFactory scopeFactory, ICurrentUserService currentUser)
    {
        _scopeFactory = scopeFactory;
        _currentUser = currentUser;
    }

    public Task<PurchaseListItemDto?> PickPurchaseAsync()
    {
        var branchId = _currentUser.CurrentUser?.BranchId;
        using var scope = _scopeFactory.CreateScope();
        var purchaseService = scope.ServiceProvider.GetRequiredService<IPurchaseService>();
        var viewModel = new PurchaseSearchViewModel(purchaseService, branchId);
        var window = new PurchaseSearchWindow(viewModel)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (window.ShowDialog() != true || viewModel.SelectedBill is not PurchaseSupplierBillDto bill)
            return Task.FromResult<PurchaseListItemDto?>(null);

        return Task.FromResult<PurchaseListItemDto?>(
            new PurchaseListItemDto(
                bill.PurchaseId,
                bill.InvoiceNumber,
                bill.InvoiceDate,
                bill.SupplierName,
                bill.SupplierInvoiceNumber));
    }
}
