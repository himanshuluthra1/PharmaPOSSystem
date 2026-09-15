using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using PharmaPOS.Application.Features.Sales;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.Views;

/// <summary>Read-only popup showing a sale invoice, with print/PDF in invoice format.</summary>
public partial class SaleBillViewerWindow : Window
{
    private readonly SaleReceiptDto _receipt;
    private readonly IInvoicePrintService? _printService;

    public SaleBillViewerWindow(SaleReceiptDto receipt, IInvoicePrintService? printService = null)
    {
        InitializeComponent();
        _receipt = receipt;
        _printService = printService;
        DataContext = new SaleBillViewerModel(receipt);
        Title = $"Sale Invoice — {receipt.InvoiceNumber}";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void PrintButton_Click(object sender, RoutedEventArgs e)
    {
        if (_printService is null)
        {
            MessageBox.Show(this, "Print service is not available.", "Print",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            _printService.Print(_receipt);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Print failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PdfButton_Click(object sender, RoutedEventArgs e)
    {
        if (_printService is null)
        {
            MessageBox.Show(this, "PDF service is not available.", "PDF",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var generated = _printService.ExportPrintablePdf(_receipt);
            var dialog = new SaveFileDialog
            {
                Title = "Save invoice PDF",
                Filter = "PDF files (*.pdf)|*.pdf",
                FileName = InvoicePageLayout.BuildPdfFileName(_receipt.InvoiceNumber, _receipt.CustomerName),
                AddExtension = true,
                DefaultExt = ".pdf"
            };
            if (dialog.ShowDialog(this) != true) return;

            File.Copy(generated, dialog.FileName, overwrite: true);
            Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "PDF export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

public sealed class SaleBillViewerModel
{
    public SaleBillViewerModel(SaleReceiptDto r)
    {
        InvoiceNumber = r.InvoiceNumber;
        InvoiceDateLabel = r.InvoiceDate.ToString("dd/MM/yyyy hh:mm tt");
        CustomerName = string.IsNullOrWhiteSpace(r.CustomerName) ? "Walk-in Customer" : r.CustomerName;
        CustomerPhone = string.IsNullOrWhiteSpace(r.CustomerPhone) ? "—" : r.CustomerPhone!;
        DoctorName = string.IsNullOrWhiteSpace(r.DoctorName) ? "—" : r.DoctorName!;
        SubTotal = r.SubTotal;
        DiscountAmount = r.DiscountAmount;
        CgstAmount = r.CgstAmount;
        SgstAmount = r.SgstAmount;
        RoundOff = r.RoundOff;
        GrandTotal = r.GrandTotal;
        PaidAmount = r.PaidAmount;
        BalanceDue = Math.Max(0m, r.GrandTotal - r.PaidAmount);
        Lines = r.Lines.Select(l => new SaleBillLineModel(l)).ToList();
    }

    public string InvoiceNumber { get; }
    public string InvoiceDateLabel { get; }
    public string CustomerName { get; }
    public string CustomerPhone { get; }
    public string DoctorName { get; }
    public decimal SubTotal { get; }
    public decimal DiscountAmount { get; }
    public decimal CgstAmount { get; }
    public decimal SgstAmount { get; }
    public decimal RoundOff { get; }
    public decimal GrandTotal { get; }
    public decimal PaidAmount { get; }
    public decimal BalanceDue { get; }
    public IReadOnlyList<SaleBillLineModel> Lines { get; }
}

public sealed class SaleBillLineModel
{
    public SaleBillLineModel(SaleReceiptLineDto l)
    {
        MedicineName = l.IsReturnLine ? $"{l.MedicineName} (Return)" : l.MedicineName;
        BatchNumber = l.BatchNumber;
        ExpiryLabel = l.ExpiryDate?.ToString("MM/yyyy") ?? "—";
        Quantity = l.Quantity;
        Mrp = l.Mrp;
        UnitPrice = l.UnitPrice;
        DiscountPercent = l.DiscountPercent;
        GstPercent = l.GstPercent;
        Amount = l.Amount;
    }

    public string MedicineName { get; }
    public string BatchNumber { get; }
    public string ExpiryLabel { get; }
    public decimal Quantity { get; }
    public decimal Mrp { get; }
    public decimal UnitPrice { get; }
    public decimal DiscountPercent { get; }
    public decimal GstPercent { get; }
    public decimal Amount { get; }
}
