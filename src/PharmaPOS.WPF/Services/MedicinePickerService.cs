using Microsoft.Extensions.DependencyInjection;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Inventory;
using PharmaPOS.Application.Features.Masters;
using PharmaPOS.Application.Features.Sales;
using PharmaPOS.Application.Features.ShortageBook;
using PharmaPOS.Domain.Enums;
using PharmaPOS.WPF.ViewModels.Sales;
using PharmaPOS.WPF.Views;

namespace PharmaPOS.WPF.Services;

public class MedicinePickerService : IMedicinePickerService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICurrentUserService _currentUser;
    private readonly IDialogService _dialog;

    public MedicinePickerService(
        IServiceScopeFactory scopeFactory,
        ICurrentUserService currentUser,
        IDialogService dialog)
    {
        _scopeFactory = scopeFactory;
        _currentUser = currentUser;
        _dialog = dialog;
    }

    public async Task<MedicineBatchSelection?> PickMedicineAsync()
    {
        var branchId = _currentUser.CurrentUser?.BranchId;
        using var scope = _scopeFactory.CreateScope();
        var salesService = scope.ServiceProvider.GetRequiredService<ISalesService>();
        var shortageBook = scope.ServiceProvider.GetRequiredService<IShortageBookService>();
        var medicine = ShowMedicineSearch(scope, branchId);
        if (medicine is null) return null;

        await TryRecordZeroStockShortageAsync(shortageBook, medicine, ShortageSource.SalesCart, branchId);

        return await PickBatchForSaleAsync(salesService, medicine, branchId);
    }

    public async Task<MedicineBatchSelection?> PickMedicineForAdjustmentAsync()
    {
        var branchId = _currentUser.CurrentUser?.BranchId;
        using var scope = _scopeFactory.CreateScope();
        var inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();

        var medicine = ShowMedicineSearch(scope, branchId);
        if (medicine is null) return null;

        var adjustmentBatches = await inventory.GetBatchesForAdjustmentAsync(medicine.Id, branchId);
        if (adjustmentBatches.Count == 0)
        {
            _dialog.ShowInfo(
                $"No batches found for \"{medicine.Name}\". Create stock via Purchase first, or try again.",
                "No batch");
            return null;
        }

        var batches = adjustmentBatches
            .Select(b => new BatchLookupDto(
                b.BatchId, b.BatchNumber, b.ExpiryDate, b.QuantityAvailable,
                b.Mrp, b.Mrp, 0m))
            .ToList();

        return PickBatchFromList(medicine.Id, medicine.Name, medicine.DefaultDiscountPercent, batches, medicine.LocationLabel);
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

        return await PickBatchForSaleAsync(
            salesService,
            medicine.Id,
            medicine.Name,
            medicine.DefaultDiscountPercent,
            branchId,
            locationLabel: null);
    }

    public Task<MedicineLookupDto?> PickMedicineLookupAsync()
    {
        var branchId = _currentUser.CurrentUser?.BranchId;
        using var scope = _scopeFactory.CreateScope();
        return Task.FromResult(ShowMedicineSearch(scope, branchId));
    }

    private static MedicineLookupDto? ShowMedicineSearch(
        IServiceScope scope, int? branchId)
    {
        var import = scope.ServiceProvider.GetRequiredService<IPharmacyMedicineImportService>();
        var masters = scope.ServiceProvider.GetRequiredService<IMastersService>();
        var ledger = scope.ServiceProvider.GetRequiredService<IMedicineLedgerDialogService>();
        var shortageBook = scope.ServiceProvider.GetRequiredService<IShortageBookService>();
        var dialog = scope.ServiceProvider.GetRequiredService<IDialogService>();
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUserService>();
        var searchIndex = scope.ServiceProvider.GetRequiredService<IMedicineSearchIndex>();

        var searchVm = new MedicineSearchViewModel(searchIndex, branchId);
        var searchWin = new MedicineSearchWindow(searchVm, import, masters, ledger, shortageBook, dialog, currentUser)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (searchWin.ShowDialog() != true)
            return null;

        return searchWin.ResultMedicine;
    }

    public async Task<MedicineBatchSelection?> PickBatchForMedicineAsync(MedicineLookupDto medicine)
    {
        var branchId = _currentUser.CurrentUser?.BranchId;
        using var scope = _scopeFactory.CreateScope();
        var salesService = scope.ServiceProvider.GetRequiredService<ISalesService>();
        var shortageBook = scope.ServiceProvider.GetRequiredService<IShortageBookService>();
        await TryRecordZeroStockShortageAsync(shortageBook, medicine, ShortageSource.Barcode, branchId);
        return await PickBatchForSaleAsync(salesService, medicine, branchId);
    }

    private async Task TryRecordZeroStockShortageAsync(
        IShortageBookService shortageBook,
        MedicineLookupDto medicine,
        ShortageSource source,
        int? branchId)
    {
        if (medicine.TotalStock > 0) return;

        var onHand = await shortageBook.GetOnHandQuantityAsync(medicine.Id, branchId);
        var prompt = _dialog.PromptShortageDetails(
            medicine.Name,
            defaultWantedQuantity: 1m,
            detailLine: "No stock (lost sale). Wanted quantity and customer name are optional.");
        if (prompt is null) return;

        var result = await shortageBook.RecordAsync(
            new RecordShortageRequest(
                medicine.Id,
                prompt.WantedQuantity,
                onHand,
                source,
                prompt.CustomerName),
            branchId,
            _currentUser.CurrentUser?.FullName ?? _currentUser.CurrentUser?.Username);

        if (result.IsFailure)
            _dialog.ShowError(result.Error ?? "Could not record shortage.");
        else
            _dialog.ShowInfo($"Added to shortage book: {medicine.Name}", "Shortage book");
    }

    private static async Task<MedicineBatchSelection?> PickBatchForSaleAsync(
        ISalesService salesService, MedicineLookupDto medicine, int? branchId)
        => await PickBatchForSaleAsync(
            salesService,
            medicine.Id,
            medicine.Name,
            medicine.DefaultDiscountPercent,
            branchId,
            medicine.LocationLabel);

    private static async Task<MedicineBatchSelection?> PickBatchForSaleAsync(
        ISalesService salesService, int medicineId, string medicineName, decimal defaultDiscountPercent, int? branchId, string? locationLabel)
    {
        var batches = await salesService.GetBatchesAsync(medicineId, branchId);
        if (batches.Count == 0) return null;
        return PickBatchFromList(medicineId, medicineName, defaultDiscountPercent, batches, locationLabel);
    }

    private static MedicineBatchSelection? PickBatchFromList(
        int medicineId,
        string medicineName,
        decimal defaultDiscountPercent,
        IReadOnlyList<BatchLookupDto> batches,
        string? locationFallback = null)
    {
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
            defaultDiscountPercent,
            batch.LocationLabel ?? locationFallback);
    }
}
