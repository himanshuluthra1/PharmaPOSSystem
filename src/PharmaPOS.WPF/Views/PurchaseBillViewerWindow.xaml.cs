using System.Windows;
using PharmaPOS.Application.Features.Purchases;

namespace PharmaPOS.WPF.Views;

/// <summary>Read-only popup showing a purchase / GRN invoice from Reports.</summary>
public partial class PurchaseBillViewerWindow : Window
{
    public PurchaseBillViewerWindow(PurchaseLoadDto purchase)
    {
        InitializeComponent();
        DataContext = new PurchaseBillViewerModel(purchase);
        Title = $"Purchase Invoice — {purchase.InvoiceNumber}";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}

public sealed class PurchaseBillViewerModel
{
    public PurchaseBillViewerModel(PurchaseLoadDto p)
    {
        InvoiceNumber = p.InvoiceNumber;
        SupplierInvoiceNumber = string.IsNullOrWhiteSpace(p.SupplierInvoiceNumber)
            ? "—"
            : p.SupplierInvoiceNumber!;
        InvoiceDateLabel = p.InvoiceDate.ToString("dd/MM/yyyy hh:mm tt");
        SupplierName = p.SupplierName;
        SupplierPhone = string.IsNullOrWhiteSpace(p.SupplierPhone) ? "—" : p.SupplierPhone!;
        GrandTotal = p.GrandTotal;
        PaidAmount = p.PaidAmount;
        BalanceDue = Math.Max(0m, p.GrandTotal - p.PaidAmount);
        PaymentMethod = p.PaymentMethod.ToString();
        Lines = p.Lines.Select(l => new PurchaseBillLineModel(l)).ToList();
    }

    public string InvoiceNumber { get; }
    public string SupplierInvoiceNumber { get; }
    public string InvoiceDateLabel { get; }
    public string SupplierName { get; }
    public string SupplierPhone { get; }
    public string PaymentMethod { get; }
    public decimal GrandTotal { get; }
    public decimal PaidAmount { get; }
    public decimal BalanceDue { get; }
    public IReadOnlyList<PurchaseBillLineModel> Lines { get; }
}

public sealed class PurchaseBillLineModel
{
    public PurchaseBillLineModel(PurchaseLoadLineDto l)
    {
        MedicineName = l.MedicineName;
        BatchNumber = l.BatchNumber;
        ExpiryLabel = l.ExpiryDate?.ToString("MM/yyyy") ?? "—";
        Quantity = l.Quantity;
        FreeQuantity = l.FreeQuantity;
        PurchasePrice = l.PurchasePrice;
        Mrp = l.Mrp;
        DiscountPercent = l.DiscountPercent;
        GstPercent = l.GstPercent;
        var taxable = l.Quantity * l.PurchasePrice * (1m - l.DiscountPercent / 100m);
        LineTotal = Math.Round(taxable * (1m + l.GstPercent / 100m), 2);
    }

    public string MedicineName { get; }
    public string BatchNumber { get; }
    public string ExpiryLabel { get; }
    public decimal Quantity { get; }
    public decimal FreeQuantity { get; }
    public decimal PurchasePrice { get; }
    public decimal Mrp { get; }
    public decimal DiscountPercent { get; }
    public decimal GstPercent { get; }
    public decimal LineTotal { get; }
}
