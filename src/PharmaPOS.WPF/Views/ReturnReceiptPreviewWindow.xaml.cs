using System.Windows;
using PharmaPOS.Application.Features.SaleReturns;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.Views;

public partial class ReturnReceiptPreviewWindow : Window
{
    private readonly IInvoicePrintService _printService;
    private readonly SaleReturnReceiptDto _receipt;

    public ReturnReceiptPreviewWindow(IInvoicePrintService printService, SaleReturnReceiptDto receipt)
    {
        InitializeComponent();
        _printService = printService;
        _receipt = receipt;
        Viewer.Document = printService.BuildReturnDocument(receipt);
    }

    private void PrintButton_Click(object sender, RoutedEventArgs e) => _printService.PrintReturn(_receipt);

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
