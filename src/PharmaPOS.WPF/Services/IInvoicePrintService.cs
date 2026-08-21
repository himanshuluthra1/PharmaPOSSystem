using System.Windows.Documents;
using PharmaPOS.Application.Features.Accounting;
using PharmaPOS.Application.Features.Counters;
using PharmaPOS.Application.Features.Reports;
using PharmaPOS.Application.Features.Sales;
using PharmaPOS.Application.Features.SaleReturns;

namespace PharmaPOS.WPF.Services;

/// <summary>Builds, previews and prints A4 GST invoices from a sale receipt.</summary>
public interface IInvoicePrintService
{
    FlowDocument BuildDocument(SaleReceiptDto receipt);
    void ShowPreview(SaleReceiptDto receipt);
    void Print(SaleReceiptDto receipt);

    /// <summary>Renders the printable A4 invoice to a PDF file and returns its path.</summary>
    string ExportPrintablePdf(SaleReceiptDto receipt);

    FlowDocument BuildDayCloseDocument(CounterDayCloseDto report);
    void ShowDayClosePreview(CounterDayCloseDto report);
    void PrintDayClose(CounterDayCloseDto report);
    string ExportDayClosePdf(CounterDayCloseDto report, string? destinationPath = null);

    FlowDocument BuildReturnDocument(SaleReturnReceiptDto receipt);
    void ShowReturnPreview(SaleReturnReceiptDto receipt);
    void PrintReturn(SaleReturnReceiptDto receipt);

    FlowDocument BuildCollectionDocument(CustomerCollectionReceiptDto receipt);
    void ShowCollectionPreview(CustomerCollectionReceiptDto receipt);
    void PrintCollection(CustomerCollectionReceiptDto receipt);

    FlowDocument BuildScheduleRegisterDocument(ScheduleRegisterReportDto report);
    void ShowScheduleRegisterPreview(ScheduleRegisterReportDto report);
    void PrintScheduleRegister(ScheduleRegisterReportDto report);
    string ExportScheduleRegisterPdf(ScheduleRegisterReportDto report, string? destinationPath = null);
}
