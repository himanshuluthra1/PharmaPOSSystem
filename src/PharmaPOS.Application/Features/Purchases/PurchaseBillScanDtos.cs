namespace PharmaPOS.Application.Features.Purchases;

/// <summary>Draft purchase bill extracted from a scanned supplier invoice (OCR).</summary>
public class ScannedPurchaseDraftDto
{
    public string? RawText { get; set; }
    public string? SupplierName { get; set; }
    public int? MatchedSupplierId { get; set; }
    public string? MatchedSupplierPhone { get; set; }
    public string? SupplierInvoiceNumber { get; set; }
    public DateTime? InvoiceDate { get; set; }
    public decimal? GrandTotalHint { get; set; }
    public List<ScannedPurchaseLineDto> Lines { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class ScannedPurchaseLineDto
{
    public string OcrItemName { get; set; } = string.Empty;
    public int? MatchedMedicineId { get; set; }
    public string? MatchedMedicineName { get; set; }
    public string? BatchNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public decimal Quantity { get; set; } = 1;
    public decimal FreeQuantity { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal Mrp { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal GstPercent { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal? LineAmountHint { get; set; }
    public bool IsMatched => MatchedMedicineId is > 0;
}
