using System.Windows.Documents;
using PharmaPOS.Application.Features.Sales;

namespace PharmaPOS.WPF.Services;

/// <summary>Builds, previews and prints A4 GST invoices from a sale receipt.</summary>
public interface IInvoicePrintService
{
    FlowDocument BuildDocument(SaleReceiptDto receipt);
    void ShowPreview(SaleReceiptDto receipt);
    void Print(SaleReceiptDto receipt);
}
