using Microsoft.Extensions.DependencyInjection;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Sales;
using PharmaPOS.WPF.ViewModels.Sales;
using PharmaPOS.WPF.Views;

namespace PharmaPOS.WPF.Services;

public class BillSearchService : IBillSearchService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICurrentUserService _currentUser;
    private readonly IInvoiceViewerDialogService _invoiceViewer;

    public BillSearchService(
        IServiceScopeFactory scopeFactory,
        ICurrentUserService currentUser,
        IInvoiceViewerDialogService invoiceViewer)
    {
        _scopeFactory = scopeFactory;
        _currentUser = currentUser;
        _invoiceViewer = invoiceViewer;
    }

    public Task SearchAndViewAsync()
    {
        var branchId = _currentUser.CurrentUser?.BranchId;
        using var scope = _scopeFactory.CreateScope();
        var salesService = scope.ServiceProvider.GetRequiredService<ISalesService>();
        var viewModel = new BillSearchViewModel(salesService, branchId);

        BillSearchWindow? window = null;
        window = new BillSearchWindow(
            viewModel,
            openBillAsync: saleId => _invoiceViewer.ShowSaleAsync(saleId, window))
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        window.ShowDialog();
        return Task.CompletedTask;
    }
}
