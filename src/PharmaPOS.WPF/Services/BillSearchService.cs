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

    public BillSearchService(IServiceScopeFactory scopeFactory, ICurrentUserService currentUser)
    {
        _scopeFactory = scopeFactory;
        _currentUser = currentUser;
    }

    public Task<SaleListItemDto?> PickBillAsync()
    {
        var branchId = _currentUser.CurrentUser?.BranchId;
        using var scope = _scopeFactory.CreateScope();
        var salesService = scope.ServiceProvider.GetRequiredService<ISalesService>();
        var viewModel = new BillSearchViewModel(salesService, branchId);
        var window = new BillSearchWindow(viewModel)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (window.ShowDialog() != true || viewModel.SelectedBill is not BillSearchResultDto bill)
            return Task.FromResult<SaleListItemDto?>(null);

        return Task.FromResult<SaleListItemDto?>(
            new SaleListItemDto(bill.SaleId, bill.InvoiceNumber, bill.InvoiceDate, bill.PatientName));
    }
}
