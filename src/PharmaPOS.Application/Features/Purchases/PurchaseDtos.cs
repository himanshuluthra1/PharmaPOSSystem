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

/// <summary>Update an existing purchase invoice (same number, revised lines/totals).</summary>
public class UpdatePurchaseRequest : CreatePurchaseRequest
{
    public int PurchaseId { get; set; }
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

/// <summary>A row in the purchase invoice history dropdown.</summary>
public record PurchaseListItemDto(
    int PurchaseId,
    string InvoiceNumber,
    DateTime InvoiceDate,
    string SupplierName,
    string? SupplierInvoiceNumber = null)
{
    public string DisplayLabel => PurchaseId == 0
        ? $"{InvoiceNumber} (New)"
        : $"{InvoiceNumber} - {InvoiceDate:dd/MM/yyyy hh:mm tt} - {SupplierName}";

    public bool IsNewPurchase => PurchaseId == 0;

    public string BillDateLabel => PurchaseId == 0
        ? "(New)"
        : InvoiceDate.ToString("dd/MM/yyyy hh:mm tt");

    public string BillSupplierLabel => PurchaseId == 0
        ? string.Empty
        : SupplierName;

    public string BillSupplierInvoiceLabel => string.IsNullOrWhiteSpace(SupplierInvoiceNumber)
        ? "—"
        : SupplierInvoiceNumber!;
}

/// <summary>Full purchase payload for loading an existing GRN into the screen.</summary>
public class PurchaseLoadDto
{
    public int PurchaseId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string? SupplierInvoiceNumber { get; set; }
    public DateTime InvoiceDate { get; set; }
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string? SupplierPhone { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal PaidAmount { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public List<PurchaseLoadLineDto> Lines { get; set; } = new();
}

public class PurchaseLoadLineDto
{
    public int MedicineId { get; set; }
    public string MedicineName { get; set; } = string.Empty;
    public string? GenericName { get; set; }
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

/// <summary>A purchase invoice row in the supplier search drill-down grid.</summary>
public record PurchaseSupplierBillDto(
    int PurchaseId,
    string InvoiceNumber,
    string? SupplierInvoiceNumber,
    DateTime InvoiceDate,
    string SupplierName,
    decimal GrandTotal,
    decimal PaidAmount,
    int ItemCount)
{
    public decimal PaymentDue => Math.Max(0m, GrandTotal - PaidAmount);

    public string InvoiceDateLabel => InvoiceDate.ToString("dd/MM/yyyy hh:mm tt");
}

/// <summary>A line on a purchase invoice shown when drilling into a bill.</summary>
public record PurchaseInvoiceLineRowDto(
    string MedicineName,
    string BatchNumber,
    decimal Quantity,
    decimal FreeQuantity,
    decimal PurchasePrice,
    decimal LineTotal);
