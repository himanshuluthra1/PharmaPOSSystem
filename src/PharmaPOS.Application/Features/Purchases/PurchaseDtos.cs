using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Application.Features.Purchases;

/// <summary>A medicine match returned by the purchase-entry search box, carrying
/// the last known costing so the operator can accept or override it.</summary>
public record PurchaseMedicineDto(
    int Id,
    string Name,
    string? GenericName,
    string? Barcode,
    decimal GstPercent,
    decimal PurchasePrice,
    decimal Mrp,
    decimal SellingPrice);

/// <summary>A supplier match for the vendor picker.</summary>
public record SupplierLookupDto(
    int Id,
    string Name,
    string? Phone,
    string? GstNumber,
    decimal OutstandingBalance);

/// <summary>One received line on a purchase/GRN (prices are tax-exclusive; GST is
/// added on top, as is standard on Indian purchase invoices).</summary>
public class PurchaseLineRequest
{
    public int MedicineId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public DateTime? ManufacturingDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public decimal Quantity { get; set; }
    public decimal FreeQuantity { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal Mrp { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal GstPercent { get; set; }
}

/// <summary>Everything needed to record and receive a purchase invoice.</summary>
public class CreatePurchaseRequest
{
    public int SupplierId { get; set; }
    public string? SupplierInvoiceNumber { get; set; }
    public DateTime InvoiceDate { get; set; }
    public decimal PaidAmount { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public string? Remarks { get; set; }
    public List<PurchaseLineRequest> Lines { get; set; } = new();
}

/// <summary>Snapshot of a saved purchase, returned for confirmation/printing.</summary>
public class PurchaseReceiptDto
{
    public int PurchaseId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string? SupplierInvoiceNumber { get; set; }
    public DateTime InvoiceDate { get; set; }
    public string SupplierName { get; set; } = string.Empty;

    public int ItemCount { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal SubTotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal RoundOff { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal BalanceDue { get; set; }
}
