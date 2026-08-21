using System.Windows;
using PharmaPOS.Application.Features.Accounting;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.Views;

public partial class CollectionReceiptPreviewWindow : Window
{
    private readonly IInvoicePrintService _printService;
    private readonly CustomerCollectionReceiptDto _receipt;

    public CollectionReceiptPreviewWindow(IInvoicePrintService printService, CustomerCollectionReceiptDto receipt)
    {
        InitializeComponent();
        _printService = printService;
        _receipt = receipt;
        Viewer.Document = printService.BuildCollectionDocument(receipt);
    }

    private void PrintButton_Click(object sender, RoutedEventArgs e) => _printService.PrintCollection(_receipt);

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
