namespace PharmaPOS.Application.Features.Reports;

public enum GstReturnKind
{
    Gstr1,
    Gstr2B
}

public record GstReturnPreviewRowDto(
    string Section,
    string InvoiceNumber,
    DateTime InvoiceDate,
    string PartyName,
    string? Gstin,
    string PlaceOfSupply,
    decimal GstRate,
    decimal TaxableAmount,
    decimal CgstAmount,
    decimal SgstAmount,
    decimal IgstAmount,
    decimal InvoiceValue,
    string? HsnCode)
{
    public string InvoiceDateLabel => InvoiceDate.ToString("dd/MM/yyyy");
    public decimal TotalTax => CgstAmount + SgstAmount + IgstAmount;
}

public sealed class GstReturnExportDto
{
    public GstReturnKind Kind { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Gstin { get; set; } = string.Empty;
    public string FilingPeriod { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public string Disclaimer { get; set; } = string.Empty;
    public List<GstReturnPreviewRowDto> PreviewRows { get; set; } = [];
    public string JsonPayload { get; set; } = "{}";
    public List<GstReturnExcelSheetDto> ExcelSheets { get; set; } = [];
}

public sealed class GstReturnExcelSheetDto
{
    public string Name { get; set; } = "Sheet";
    public List<string> Headers { get; set; } = [];
    public List<IReadOnlyList<string>> Rows { get; set; } = [];
}
