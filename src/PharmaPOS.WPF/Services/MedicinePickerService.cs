using Microsoft.Extensions.DependencyInjection;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Sales;
using PharmaPOS.WPF.ViewModels.Sales;
using PharmaPOS.WPF.Views;

namespace PharmaPOS.WPF.Services;

public class MedicinePickerService : IMedicinePickerService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICurrentUserService _currentUser;

    public MedicinePickerService(IServiceScopeFactory scopeFactory, ICurrentUserService currentUser)
    {
        _scopeFactory = scopeFactory;
        _currentUser = currentUser;
    }

    public async Task<MedicineBatchSelection?> PickMedicineAsync()
    {
        var branchId = _currentUser.CurrentUser?.BranchId;
        using var scope = _scopeFactory.CreateScope();
        var salesService = scope.ServiceProvider.GetRequiredService<ISalesService>();

        var searchVm = new MedicineSearchViewModel(salesService, branchId);
        var searchWin = new MedicineSearchWindow(searchVm)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (searchWin.ShowDialog() != true || searchVm.SelectedMedicine is not MedicineLookupDto medicine)
            return null;

        var batches = await salesService.GetBatchesAsync(medicine.Id, branchId);
        if (batches.Count == 0) return null;

        BatchLookupDto batch;
        if (batches.Count == 1)
        {
            batch = batches[0];
        }
        else
        {
            var batchVm = new BatchPickerViewModel(batches, medicine.Name);
            var batchWin = new BatchPickerWindow(batchVm)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            if (batchWin.ShowDialog() != true || batchVm.SelectedBatch is null)
                return null;
            batch = batchVm.SelectedBatch;
        }

        return new MedicineBatchSelection(
            medicine.Id,
            batch.BatchId,
            medicine.Name,
            batch.BatchNumber,
            batch.ExpiryDate,
            batch.Mrp,
            batch.GstPercent,
            batch.SellingPrice > 0 ? batch.SellingPrice : batch.Mrp,
            batch.QuantityAvailable,
            medicine.DefaultDiscountPercent);
    }

    public Task<MedicineLookupDto?> PickMedicineLookupAsync()
    {
        var branchId = _currentUser.CurrentUser?.BranchId;
        using var scope = _scopeFactory.CreateScope();
        var salesService = scope.ServiceProvider.GetRequiredService<ISalesService>();

        var searchVm = new MedicineSearchViewModel(salesService, branchId);
        var searchWin = new MedicineSearchWindow(searchVm)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (searchWin.ShowDialog() != true || searchVm.SelectedMedicine is not MedicineLookupDto medicine)
            return Task.FromResult<MedicineLookupDto?>(null);

        return Task.FromResult<MedicineLookupDto?>(medicine);
    }
}
