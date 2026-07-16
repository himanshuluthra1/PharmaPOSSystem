using Microsoft.Extensions.DependencyInjection;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.SaleReturns;
using PharmaPOS.WPF.ViewModels.Sales;
using PharmaPOS.WPF.Views;

namespace PharmaPOS.WPF.Services;

public class SaleReturnDialogService : ISaleReturnDialogService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICurrentUserService _currentUser;

    public SaleReturnDialogService(IServiceScopeFactory scopeFactory, ICurrentUserService currentUser)
    {
        _scopeFactory = scopeFactory;
        _currentUser = currentUser;
    }

    public async Task<SaleReturnDialogResult> ShowForSaleAsync(int saleId)
    {
        using var scope = _scopeFactory.CreateScope();
        var vm = scope.ServiceProvider.GetRequiredService<SaleReturnViewModel>();
        vm.ConfigureAsDialog();

        var window = new SaleReturnDialogWindow(vm)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        await vm.LoadInvoiceByIdAsync(saleId);

        if (!vm.HasLoadedInvoice)
            return new SaleReturnDialogResult(DialogShown: false, ReturnPosted: false, Receipt: null);

        var returnPosted = window.ShowDialog() == true;
        return new SaleReturnDialogResult(DialogShown: true, returnPosted, returnPosted ? vm.LastReceipt : null);
    }
}
