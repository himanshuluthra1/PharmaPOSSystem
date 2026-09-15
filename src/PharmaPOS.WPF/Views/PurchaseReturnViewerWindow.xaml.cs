using System.Windows;
using PharmaPOS.Application.Features.PurchaseReturns;

namespace PharmaPOS.WPF.Views;

/// <summary>Read-only popup showing a purchase return (e.g. from Accounting → Parties).</summary>
public partial class PurchaseReturnViewerWindow : Window
{
    public PurchaseReturnViewerWindow(PurchaseReturnDetailDto detail)
    {
        InitializeComponent();
        DataContext = new PurchaseReturnViewerModel(detail);
        Title = $"Purchase Return — {detail.ReturnNumber}";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}

public sealed class PurchaseReturnViewerModel
{
    public PurchaseReturnViewerModel(PurchaseReturnDetailDto d)
    {
        ReturnNumber = d.ReturnNumber;
        ReturnDateLabel = d.ReturnDate.ToString("dd/MM/yyyy hh:mm tt");
        SupplierName = string.IsNullOrWhiteSpace(d.SupplierName) ? "—" : d.SupplierName;
        PurchaseInvoiceNumber = string.IsNullOrWhiteSpace(d.PurchaseInvoiceNumber) ? "—" : d.PurchaseInvoiceNumber;
        ReturnTypeLabel = d.IsDirectReturn ? "Direct return" : "Against purchase bill";
        SupplierReceiptLabel = string.IsNullOrWhiteSpace(d.SupplierReturnReceiptNumber)
            ? "—"
            : d.SupplierReturnReceiptNumber!;
        GrandTotal = d.GrandTotal;
        Lines = d.Lines.Select(l => new PurchaseReturnViewerLineModel(l)).ToList();
    }

    public string ReturnNumber { get; }
    public string ReturnDateLabel { get; }
    public string SupplierName { get; }
    public string PurchaseInvoiceNumber { get; }
    public string ReturnTypeLabel { get; }
    public string SupplierReceiptLabel { get; }
    public decimal GrandTotal { get; }
    public IReadOnlyList<PurchaseReturnViewerLineModel> Lines { get; }
}

public sealed class PurchaseReturnViewerLineModel
{
    public PurchaseReturnViewerLineModel(PurchaseReturnDetailLineDto l)
    {
        MedicineName = l.MedicineName;
        BatchNumber = string.IsNullOrWhiteSpace(l.BatchNumber) ? "—" : l.BatchNumber!;
        ExpiryLabel = l.ExpiryDate?.ToString("MM/yyyy") ?? "—";
        ReturnedQuantity = l.ReturnedQuantity;
        ReturnedFreeQuantity = l.ReturnedFreeQuantity;
        PurchasePrice = l.PurchasePrice;
        GstPercent = l.GstPercent;
        LineTotal = l.LineTotal;
        ReasonName = string.IsNullOrWhiteSpace(l.ReasonName) ? "—" : l.ReasonName!;
    }

    public string MedicineName { get; }
    public string BatchNumber { get; }
    public string ExpiryLabel { get; }
    public decimal ReturnedQuantity { get; }
    public decimal ReturnedFreeQuantity { get; }
    public decimal PurchasePrice { get; }
    public decimal GstPercent { get; }
    public decimal LineTotal { get; }
    public string ReasonName { get; }
}
