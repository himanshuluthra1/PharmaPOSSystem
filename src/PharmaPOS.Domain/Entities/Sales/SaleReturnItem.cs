using PharmaPOS.Domain.Common;
using PharmaPOS.Domain.Entities.Inventory;
using PharmaPOS.Domain.Entities.Masters;

namespace PharmaPOS.Domain.Entities.Sales;

/// <summary>A single line returned against an original sale item.</summary>
public class SaleReturnItem : BaseEntity
{
    public int SaleReturnId { get; set; }
    public SaleReturn? SaleReturn { get; set; }

    public int SaleItemId { get; set; }
    public SaleItem? SaleItem { get; set; }

    public int MedicineId { get; set; }
    public Medicine? Medicine { get; set; }

    public int? MedicineBatchId { get; set; }
    public MedicineBatch? MedicineBatch { get; set; }

    public string? BatchNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }

    public decimal ReturnedQuantity { get; set; }
    public decimal Mrp { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal GstPercent { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }

    public int ReturnReasonId { get; set; }
    public ReturnReason? ReturnReason { get; set; }
    public string? ReasonRemarks { get; set; }

    public bool SealIntact { get; set; } = true;
    public bool PackagingDamaged { get; set; }
    public bool ExpiryValid { get; set; } = true;
    public bool IsSaleable { get; set; } = true;
    public bool BatchMismatchApproved { get; set; }
    public string? ScannedBatchNumber { get; set; }
}
