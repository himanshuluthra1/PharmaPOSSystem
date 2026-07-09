using PharmaPOS.Domain.Common;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Domain.Entities.Inventory;

namespace PharmaPOS.Domain.Entities.Masters;

/// <summary>
/// The catalogue definition of a medicine / SKU. Actual stock quantities live on
/// <see cref="MedicineBatch"/> records so batches and expiries can be tracked
/// independently (FIFO/FEFO).
/// </summary>
public class Medicine : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? GenericName { get; set; }
    public string? Brand { get; set; }
    public string? Composition { get; set; }
    public string? Strength { get; set; }
    public DosageForm DosageForm { get; set; } = DosageForm.Tablet;

    public int? CategoryId { get; set; }
    public MedicineCategory? Category { get; set; }

    public int? ManufacturerId { get; set; }
    public Manufacturer? Manufacturer { get; set; }

    public string? HsnCode { get; set; }
    public decimal GstPercent { get; set; }

    public bool IsBatchEnabled { get; set; } = true;
    public bool IsExpiryEnabled { get; set; } = true;
    public string? Barcode { get; set; }

    public decimal Mrp { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal DefaultDiscountPercent { get; set; }

    public string? RackNumber { get; set; }
    public string? StorageCondition { get; set; }
    public ScheduleDrugType ScheduleType { get; set; } = ScheduleDrugType.None;
    public bool PrescriptionRequired { get; set; }

    /// <summary>Units per pack/strip used for loose-vs-pack selling.</summary>
    public int UnitsPerPack { get; set; } = 1;
    public string? UnitOfMeasure { get; set; } = "Nos";

    public int ReorderLevel { get; set; }
    public int ReorderQuantity { get; set; }

    public string? ImagePath { get; set; }
    public string? Notes { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;

    public ICollection<MedicineBatch> Batches { get; set; } = new List<MedicineBatch>();
}
