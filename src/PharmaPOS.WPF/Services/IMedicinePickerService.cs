using PharmaPOS.Application.Features.Sales;

namespace PharmaPOS.WPF.Services;

/// <summary>Result of picking a medicine and batch for a sales line.</summary>
public record MedicineBatchSelection(
    int MedicineId,
    int BatchId,
    string MedicineName,
    string BatchNumber,
    DateTime? ExpiryDate,
    decimal Mrp,
    decimal GstPercent,
    decimal UnitPrice,
    decimal AvailableStock,
    decimal DefaultDiscountPercent);

/// <summary>
/// Shows medicine search and optional batch-variant picker dialogs used by the
/// sales grid item column.
/// </summary>
public interface IMedicinePickerService
{
    Task<MedicineBatchSelection?> PickMedicineAsync();

    /// <summary>Shows medicines with the same salt and strength; returns batch selection for replacement.</summary>
    Task<MedicineBatchSelection?> PickSubstituteAsync(IReadOnlyList<SubstituteMedicineDto> substitutes, int medicineId);

    /// <summary>Shows medicine search only (no batch picker) for purchase entry.</summary>
    Task<MedicineLookupDto?> PickMedicineLookupAsync();
}
