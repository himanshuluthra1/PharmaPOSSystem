using PharmaPOS.Domain.Common;

namespace PharmaPOS.Domain.Entities.Masters;

/// <summary>
/// Persistent MedWin ↔ OneMG link. One OneMG medicine may have many MedWin mappings.
/// Each MedWin catalog id may appear only once.
/// </summary>
public class MedicineMedWinMapping : BaseEntity
{
    public int OneMgMedicineId { get; set; }
    public Medicine? OneMgMedicine { get; set; }

    /// <summary>PharmaPOS medicine row id for the MedWin-only row at mapping time.</summary>
    public int? MedWinMedicineId { get; set; }

    public int MedWinId { get; set; }
    public string MedWinMedicineName { get; set; } = string.Empty;
    public string OneMgMedicineName { get; set; } = string.Empty;
    public string? OneMgCatalogId { get; set; }
}
