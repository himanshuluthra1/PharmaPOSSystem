using System.Windows;
using PharmaPOS.Application.Features.Sales;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.Views;

/// <summary>On-screen preview of a generated invoice with a print action.</summary>
public partial class InvoicePreviewWindow : Window
{
    private readonly IInvoicePrintService _printService;
    private readonly SaleReceiptDto _receipt;

    public InvoicePreviewWindow(IInvoicePrintService printService, SaleReceiptDto receipt)
    {
        InitializeComponent();
        _printService = printService;
        _receipt = receipt;
        Viewer.Document = printService.BuildDocument(receipt);
    }

    private void PrintButton_Click(object sender, RoutedEventArgs e) => _printService.Print(_receipt);

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
