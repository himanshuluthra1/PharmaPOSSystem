using PharmaPOS.Domain.Common;
using PharmaPOS.Domain.Entities.Inventory;
using PharmaPOS.Domain.Entities.Masters;

namespace PharmaPOS.Domain.Entities.Sales;

/// <summary>A single line on a sales invoice, tied to a specific batch.</summary>
public class SaleItem : BaseEntity
{
    public int SaleId { get; set; }
    public Sale? Sale { get; set; }

    public int MedicineId { get; set; }
    public Medicine? Medicine { get; set; }

    public int? MedicineBatchId { get; set; }
    public MedicineBatch? MedicineBatch { get; set; }

    public string? BatchNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }

    public decimal Quantity { get; set; }
    public decimal Mrp { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal GstPercent { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
}
