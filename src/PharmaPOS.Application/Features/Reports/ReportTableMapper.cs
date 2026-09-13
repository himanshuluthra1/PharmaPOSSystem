using System.Globalization;
using PharmaPOS.Application.Features.SaleReturns;

namespace PharmaPOS.Application.Features.Reports;

/// <summary>Maps typed report rows into the generic <see cref="ReportTableDto"/> shape.</summary>
public static class ReportTableMapper
{
    public static ReportTableDto FromSales(ReportSummaryDto summary, IReadOnlyList<SalesReportRowDto> rows)
        => Table(summary,
            Cols(
                C("InvoiceNumber", "Invoice"),
                C("InvoiceDateLabel", "Date"),
                C("CustomerName", "Customer"),
                C("ItemCount", "Items"),
                C("SubTotal", "SubTotal", "N2"),
                C("DiscountAmount", "Discount", "N2"),
                C("TaxAmount", "Tax", "N2"),
                C("GrandTotal", "Total", "N2"),
                C("PaidAmount", "Paid", "N2"),
                C("BalanceDue", "Due", "N2")),
            rows.Select(r => Dict(
                ("SaleId", (object?)r.SaleId),
                ("InvoiceNumber", r.InvoiceNumber),
                ("InvoiceDateLabel", r.InvoiceDateLabel),
                ("CustomerName", r.CustomerName),
                ("ItemCount", r.ItemCount),
                ("SubTotal", r.SubTotal),
                ("DiscountAmount", r.DiscountAmount),
                ("TaxAmount", r.TaxAmount),
                ("GrandTotal", r.GrandTotal),
                ("PaidAmount", r.PaidAmount),
                ("BalanceDue", r.BalanceDue))));

    public static ReportTableDto FromPurchases(ReportSummaryDto summary, IReadOnlyList<PurchaseReportRowDto> rows)
        => Table(summary,
            Cols(
                C("InvoiceNumber", "Invoice"),
                C("InvoiceDateLabel", "Date"),
                C("SupplierName", "Supplier"),
                C("ItemCount", "Items"),
                C("GrandTotal", "Total", "N2"),
                C("CashPaid", "CashPaid", "N2"),
                C("ReturnCreditApplied", "ReturnCredit", "N2"),
                C("BalanceDue", "Due", "N2"),
                C("DueReason", "DueReason")),
            rows.Select(r => Dict(
                ("PurchaseId", (object?)r.PurchaseId),
                ("InvoiceNumber", r.InvoiceNumber),
                ("InvoiceDateLabel", r.InvoiceDateLabel),
                ("SupplierName", r.SupplierName),
                ("ItemCount", r.ItemCount),
                ("GrandTotal", r.GrandTotal),
                ("PaidAmount", r.PaidAmount),
                ("CashPaid", r.CashPaid),
                ("ReturnCreditApplied", r.ReturnCreditApplied),
                ("BalanceDue", r.BalanceDue),
                ("DueReason", r.DueReason))));

    public static ReportTableDto FromGst(GstSummaryDto gst, IReadOnlyList<GstDetailRowDto> rows)
        => new()
        {
            GstSummary = gst,
            Summary = new ReportSummaryDto
            {
                RecordCount = rows.Count,
                TotalAmount = rows.Sum(r => r.GrandTotal),
                TotalTax = rows.Sum(r => r.TotalTax),
                FooterNote = $"Net GST payable ₹{gst.NetTaxPayable:N2}"
            },
            Columns = Cols(
                C("DocumentType", "Type"),
                C("InvoiceNumber", "Invoice"),
                C("InvoiceDateLabel", "Date"),
                C("PartyName", "Party"),
                C("TaxableAmount", "Taxable", "N2"),
                C("CgstAmount", "CGST", "N2"),
                C("SgstAmount", "SGST", "N2"),
                C("IgstAmount", "IGST", "N2"),
                C("GrandTotal", "Total", "N2")),
            Rows = rows.Select(r => Dict(
                ("DocumentType", (object?)r.DocumentType),
                ("InvoiceNumber", r.InvoiceNumber),
                ("InvoiceDateLabel", r.InvoiceDateLabel),
                ("PartyName", r.PartyName),
                ("TaxableAmount", r.TaxableAmount),
                ("CgstAmount", r.CgstAmount),
                ("SgstAmount", r.SgstAmount),
                ("IgstAmount", r.IgstAmount),
                ("GrandTotal", r.GrandTotal))).ToList()
        };

    public static ReportTableDto FromGstReturn(GstReturnExportDto export)
        => new()
        {
            GstReturnExport = export,
            Summary = new ReportSummaryDto
            {
                RecordCount = export.PreviewRows.Count,
                TotalAmount = export.PreviewRows.Sum(r => r.InvoiceValue),
                TotalTax = export.PreviewRows.Sum(r => r.TotalTax),
                FooterNote = export.Disclaimer
            },
            Columns = Cols(
                C("Section", "Section"),
                C("InvoiceNumber", "Invoice"),
                C("InvoiceDateLabel", "Date"),
                C("PartyName", "Party"),
                C("Gstin", "GSTIN"),
                C("TaxableAmount", "Taxable", "N2"),
                C("CgstAmount", "CGST", "N2"),
                C("SgstAmount", "SGST", "N2"),
                C("IgstAmount", "IGST", "N2"),
                C("InvoiceValue", "Value", "N2")),
            Rows = export.PreviewRows.Select(r => Dict(
                ("Section", (object?)r.Section),
                ("InvoiceNumber", r.InvoiceNumber),
                ("InvoiceDateLabel", r.InvoiceDateLabel),
                ("PartyName", r.PartyName),
                ("Gstin", r.Gstin),
                ("TaxableAmount", r.TaxableAmount),
                ("CgstAmount", r.CgstAmount),
                ("SgstAmount", r.SgstAmount),
                ("IgstAmount", r.IgstAmount),
                ("InvoiceValue", r.InvoiceValue))).ToList()
        };

    public static ReportTableDto FromProfit(ReportSummaryDto summary, IReadOnlyList<ProfitReportRowDto> rows)
        => Table(summary,
            Cols(
                C("InvoiceNumber", "Invoice"),
                C("InvoiceDateLabel", "Date"),
                C("CustomerName", "Customer"),
                C("Revenue", "Revenue", "N2"),
                C("Cost", "Cost", "N2"),
                C("GrossProfit", "GrossProfit", "N2"),
                C("MarginPercent", "Margin%", "N1")),
            rows.Select(r => Dict(
                ("InvoiceNumber", (object?)r.InvoiceNumber),
                ("InvoiceDateLabel", r.InvoiceDateLabel),
                ("CustomerName", r.CustomerName),
                ("Revenue", r.Revenue),
                ("Cost", r.Cost),
                ("GrossProfit", r.GrossProfit),
                ("MarginPercent", r.MarginPercent))));

    public static ReportTableDto FromMedicineSales(ReportSummaryDto summary, IReadOnlyList<MedicineSalesRowDto> rows)
        => Table(summary,
            Cols(
                C("MedicineName", "Medicine"),
                C("GenericName", "Salt"),
                C("QuantitySold", "Qty", "0.##"),
                C("Revenue", "Revenue", "N2"),
                C("Cost", "Cost", "N2"),
                C("GrossProfit", "Profit", "N2"),
                C("MarginPercent", "Margin%", "N1")),
            rows.Select(r => Dict(
                ("MedicineName", (object?)r.MedicineName),
                ("GenericName", r.GenericName),
                ("QuantitySold", r.QuantitySold),
                ("Revenue", r.Revenue),
                ("Cost", r.Cost),
                ("GrossProfit", r.GrossProfit),
                ("MarginPercent", r.MarginPercent))));

    public static ReportTableDto FromStock(ReportSummaryDto summary, IReadOnlyList<StockValuationReportRowDto> rows)
        => Table(summary,
            Cols(
                C("MedicineName", "Medicine"),
                C("BatchNumber", "Batch"),
                C("ExpiryLabel", "Expiry"),
                C("Quantity", "Qty", "0.##"),
                C("PurchasePrice", "Cost", "N2"),
                C("Mrp", "MRP", "N2"),
                C("StockAmount", "MRPValue", "N2"),
                C("StockValue", "CostValue", "N2")),
            rows.Select(r => Dict(
                ("MedicineName", (object?)r.MedicineName),
                ("BatchNumber", r.BatchNumber),
                ("ExpiryLabel", r.ExpiryLabel),
                ("Quantity", r.Quantity),
                ("PurchasePrice", r.PurchasePrice),
                ("Mrp", r.Mrp),
                ("StockAmount", r.StockAmount),
                ("StockValue", r.StockValue))));

    public static ReportTableDto FromExpiry(ReportSummaryDto summary, IReadOnlyList<ExpiryReportRowDto> rows)
        => Table(summary,
            Cols(
                C("MedicineName", "Medicine"),
                C("BatchNumber", "Batch"),
                C("ExpiryLabel", "Expiry"),
                C("Quantity", "Qty", "0.##"),
                C("StockValue", "Value", "N2"),
                C("ExpiryStatus", "Status"),
                C("SupplierLabel", "Supplier")),
            rows.Select(r => Dict(
                ("MedicineName", (object?)r.MedicineName),
                ("BatchNumber", r.BatchNumber),
                ("ExpiryDate", r.ExpiryDate),
                ("ExpiryLabel", r.ExpiryLabel),
                ("Quantity", r.Quantity),
                ("StockValue", r.StockValue),
                ("ExpiryStatus", r.ExpiryStatus),
                ("SupplierId", r.SupplierId),
                ("SupplierName", r.SupplierName),
                ("SupplierLabel", r.SupplierLabel))));

    public static ReportTableDto FromLowStock(ReportSummaryDto summary, IReadOnlyList<LowStockReportRowDto> rows)
        => Table(summary,
            Cols(
                C("MedicineName", "Medicine"),
                C("GenericName", "Salt"),
                C("QuantityOnHand", "OnHand", "0.##"),
                C("ReorderLevel", "Reorder"),
                C("ReorderQuantity", "ReorderQty"),
                C("Shortfall", "Shortfall", "0.##")),
            rows.Select(r => Dict(
                ("MedicineName", (object?)r.MedicineName),
                ("GenericName", r.GenericName),
                ("QuantityOnHand", r.QuantityOnHand),
                ("ReorderLevel", r.ReorderLevel),
                ("ReorderQuantity", r.ReorderQuantity),
                ("Shortfall", r.Shortfall),
                ("IsCritical", r.IsCritical))));

    public static ReportTableDto FromSchedule(ReportSummaryDto summary, ScheduleRegisterReportDto report)
        => new()
        {
            Summary = summary,
            ScheduleRegister = report,
            Columns = Cols(
                C("InvoiceDateLabel", "Date"),
                C("InvoiceNumber", "Invoice"),
                C("PatientName", "Patient"),
                C("DoctorDisplay", "Doctor"),
                C("MedicineName", "Medicine"),
                C("ScheduleLabel", "Sch"),
                C("BatchNumber", "Batch"),
                C("Quantity", "Qty", "0.##")),
            Rows = report.Rows.Select(r => Dict(
                ("SaleId", (object?)r.SaleId),
                ("InvoiceDateLabel", r.InvoiceDateLabel),
                ("InvoiceNumber", r.InvoiceNumber),
                ("PatientName", r.PatientName),
                ("DoctorDisplay", r.DoctorDisplay),
                ("MedicineName", r.MedicineName),
                ("ScheduleLabel", r.ScheduleLabel),
                ("BatchNumber", r.BatchNumber),
                ("Quantity", r.Quantity))).ToList()
        };

    public static ReportTableDto FromSaleReturns(ReportSummaryDto summary, IReadOnlyList<SaleReturnSummaryRowDto> rows)
        => Table(summary,
            Cols(
                C("ReturnNumber", "Return#"),
                C("ReturnDateLabel", "Date"),
                C("InvoiceNumber", "Invoice"),
                C("CustomerName", "Customer"),
                C("RefundAmount", "Refund", "N2"),
                C("RefundMode", "Mode"),
                C("IsFullReturn", "Full"),
                C("CashierName", "Cashier")),
            rows.Select(r => Dict(
                ("ReturnNumber", (object?)r.ReturnNumber),
                ("ReturnDateLabel", r.ReturnDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)),
                ("InvoiceNumber", r.OriginalInvoiceNumber),
                ("CustomerName", r.CustomerName),
                ("RefundAmount", r.RefundAmount),
                ("RefundMode", r.RefundMode.ToString()),
                ("IsFullReturn", r.IsFullReturn ? "Yes" : "No"),
                ("CashierName", r.CashierName))));

    public static ReportTableDto FromMedicineReturns(ReportSummaryDto summary, IReadOnlyList<MedicineReturnReportRowDto> rows)
        => Table(summary,
            Cols(
                C("MedicineName", "Medicine"),
                C("BatchNumber", "Batch"),
                C("ReturnedQuantity", "Qty", "0.##"),
                C("RefundAmount", "Refund", "N2"),
                C("ReturnCount", "Returns")),
            rows.Select(r => Dict(
                ("MedicineName", (object?)r.MedicineName),
                ("BatchNumber", r.BatchNumber),
                ("ReturnedQuantity", r.ReturnedQuantity),
                ("RefundAmount", r.RefundAmount),
                ("ReturnCount", r.ReturnCount))));

    public static ReportTableDto Table(
        ReportSummaryDto summary,
        List<ReportColumnDto> columns,
        IEnumerable<Dictionary<string, object?>> rows) => new()
    {
        Summary = summary,
        Columns = columns,
        Rows = rows.ToList()
    };

    public static List<ReportColumnDto> Cols(params ReportColumnDto[] cols)
        => cols.ToList();

    public static ReportColumnDto C(string key, string header, string? format = null)
        => new(key, header, format);

    public static Dictionary<string, object?> Dict(params (string Key, object? Value)[] pairs)
    {
        var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in pairs)
            d[k] = v;
        return d;
    }
}
