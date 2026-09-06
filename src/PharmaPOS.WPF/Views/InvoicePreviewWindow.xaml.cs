using System.Windows;
using System.Windows.Controls;
using PharmaPOS.Application.Features.Sales;
using PharmaPOS.Domain.Enums;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.Views;

/// <summary>On-screen preview of a generated invoice with a print action.</summary>
public partial class InvoicePreviewWindow : Window
{
    private readonly IInvoicePrintService _printService;
    private readonly SaleReceiptDto _receipt;
    private InvoicePaperSize _paperSize;

    public InvoicePreviewWindow(IInvoicePrintService printService, SaleReceiptDto receipt)
    {
        InitializeComponent();
        _printService = printService;
        _receipt = receipt;
        _paperSize = receipt.InvoicePaperSize;
        PaperSizeBox.ItemsSource = new InvoicePaperSizeOption[]
        {
            new(InvoicePaperSize.A4, InvoicePageLayout.Label(InvoicePaperSize.A4)),
            new(InvoicePaperSize.A5, InvoicePageLayout.Label(InvoicePaperSize.A5)),
            new(InvoicePaperSize.Thermal80, InvoicePageLayout.Label(InvoicePaperSize.Thermal80)),
            new(InvoicePaperSize.Thermal58, InvoicePageLayout.Label(InvoicePaperSize.Thermal58))
        };
        PaperSizeBox.SelectedValue = _paperSize;
        RefreshDocument();
    }

    private void PaperSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PaperSizeBox.SelectedValue is InvoicePaperSize size)
        {
            _paperSize = size;
            RefreshDocument();
        }
    }

    private void RefreshDocument()
        => Viewer.Document = _printService.BuildDocument(_receipt, _paperSize);

    private void PrintButton_Click(object sender, RoutedEventArgs e)
        => _printService.Print(_receipt, _paperSize);

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
