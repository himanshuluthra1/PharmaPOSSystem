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

        return await PickBatchForMedicineAsync(salesService, medicine, branchId);
    }

    public async Task<MedicineBatchSelection?> PickSubstituteAsync(
        IReadOnlyList<SubstituteMedicineDto> substitutes, int medicineId)
    {
        if (substitutes.Count == 0)
            return null;

        var branchId = _currentUser.CurrentUser?.BranchId;
        using var scope = _scopeFactory.CreateScope();
        var salesService = scope.ServiceProvider.GetRequiredService<ISalesService>();

        var vm = new SubstituteMedicineViewModel(substitutes, medicineId);
        var window = new SubstituteMedicineWindow(vm)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (window.ShowDialog() != true || vm.SelectedMedicine is not SubstituteMedicineDto medicine)
            return null;

        return await PickBatchForMedicineAsync(salesService, medicine, branchId);
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

    private static async Task<MedicineBatchSelection?> PickBatchForMedicineAsync(
        ISalesService salesService, MedicineLookupDto medicine, int? branchId)
        => await PickBatchForMedicineAsync(
            salesService,
            medicine.Id,
            medicine.Name,
            medicine.DefaultDiscountPercent,
            branchId);

    private static async Task<MedicineBatchSelection?> PickBatchForMedicineAsync(
        ISalesService salesService, SubstituteMedicineDto medicine, int? branchId)
        => await PickBatchForMedicineAsync(
            salesService,
            medicine.Id,
            medicine.Name,
            medicine.DefaultDiscountPercent,
            branchId);

    private static async Task<MedicineBatchSelection?> PickBatchForMedicineAsync(
        ISalesService salesService, int medicineId, string medicineName, decimal defaultDiscountPercent, int? branchId)
    {
        var batches = await salesService.GetBatchesAsync(medicineId, branchId);
        if (batches.Count == 0) return null;

        BatchLookupDto batch;
        if (batches.Count == 1)
        {
            batch = batches[0];
        }
        else
        {
            var batchVm = new BatchPickerViewModel(batches, medicineName);
            var batchWin = new BatchPickerWindow(batchVm)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            if (batchWin.ShowDialog() != true || batchVm.SelectedBatch is null)
                return null;
            batch = batchVm.SelectedBatch;
        }

        return new MedicineBatchSelection(
            medicineId,
            batch.BatchId,
            medicineName,
            batch.BatchNumber,
            batch.ExpiryDate,
            batch.Mrp,
            batch.GstPercent,
            batch.SellingPrice > 0 ? batch.SellingPrice : batch.Mrp,
            batch.QuantityAvailable,
            defaultDiscountPercent);
    }
}
