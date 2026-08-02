using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PharmaPOS.Application.Features.Sales;
using PharmaPOS.Application.Features.SaleReturns;
using PharmaPOS.WPF.Views;

namespace PharmaPOS.WPF.Services;

/// <summary>
/// Renders a GST invoice as a WPF <see cref="FlowDocument"/> sized for A4, which can
/// be previewed on screen and sent to any Windows printer (A4 or thermal).
/// </summary>
public class InvoicePrintService : IInvoicePrintService
{
    private static readonly CultureInfo Inr = CultureInfo.GetCultureInfo("en-IN");
    private const double A4Width = 794;   // ~210mm at 96 DPI
    private const double A4Height = 1123; // ~297mm at 96 DPI

    public FlowDocument BuildDocument(SaleReceiptDto r)
    {
        var doc = new FlowDocument
        {
            PageWidth = A4Width,
            PageHeight = A4Height,
            ColumnWidth = A4Width,
            PagePadding = new Thickness(40),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            Background = Brushes.White,
            Foreground = Brushes.Black
        };

        doc.Blocks.Add(BuildHeader(r));
        doc.Blocks.Add(BuildMeta(r));
        doc.Blocks.Add(BuildItemsTable(r));
        doc.Blocks.Add(BuildTotals(r));

        if (!string.IsNullOrWhiteSpace(r.InvoiceFooter))
        {
            doc.Blocks.Add(new Paragraph(new Run(r.InvoiceFooter))
            {
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 24, 0, 0),
                FontStyle = FontStyles.Italic,
                Foreground = Brushes.Gray
            });
        }

        return doc;
    }

    public void ShowPreview(SaleReceiptDto receipt)
    {
        var window = new InvoicePreviewWindow(this, receipt)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    public void Print(SaleReceiptDto receipt)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;

        var doc = BuildDocument(receipt);
        doc.PageWidth = dialog.PrintableAreaWidth;
        doc.ColumnWidth = dialog.PrintableAreaWidth;
        IDocumentPaginatorSource source = doc;
        dialog.PrintDocument(source.DocumentPaginator, $"Invoice {receipt.InvoiceNumber}");
    }

    public string ExportPrintablePdf(SaleReceiptDto receipt)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PharmaPOS", "SharedBills");
        Directory.CreateDirectory(dir);

        var safeInvoice = Regex.Replace(receipt.InvoiceNumber ?? "bill", @"[^a-zA-Z0-9._-]+", "_");
        if (string.IsNullOrWhiteSpace(safeInvoice)) safeInvoice = "bill";
        var path = Path.Combine(dir, $"{safeInvoice}_{DateTime.Now:yyyyMMddHHmmss}.pdf");

        var doc = BuildDocument(receipt);
        // Ensure layout is measured before pagination (required when not shown on screen).
        doc.PageWidth = A4Width;
        doc.PageHeight = A4Height;
        doc.ColumnWidth = A4Width;

        IDocumentPaginatorSource source = doc;
        var paginator = source.DocumentPaginator;
        paginator.PageSize = new Size(A4Width, A4Height);
        paginator.ComputePageCount();

        using var pdf = new PdfDocument();
        pdf.Info.Title = $"Invoice {receipt.InvoiceNumber}";
        pdf.Info.Author = receipt.CompanyName ?? "PharmaPOS";

        for (var i = 0; i < paginator.PageCount; i++)
        {
            var page = paginator.GetPage(i);
            var container = new ContainerVisual();
            container.Children.Add(page.Visual);

            var width = (int)Math.Ceiling(A4Width);
            var height = (int)Math.Ceiling(A4Height);
            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(container);
            rtb.Freeze();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            ms.Position = 0;

            var pdfPage = pdf.AddPage();
            pdfPage.Width = XUnit.FromPoint(A4Width * 72.0 / 96.0);
            pdfPage.Height = XUnit.FromPoint(A4Height * 72.0 / 96.0);

            using var gfx = XGraphics.FromPdfPage(pdfPage);
            using var image = XImage.FromStream(ms);
            gfx.DrawImage(image, 0, 0, pdfPage.Width.Point, pdfPage.Height.Point);
        }

        pdf.Save(path);
        return path;
    }

    public FlowDocument BuildReturnDocument(SaleReturnReceiptDto r)
    {
        var doc = new FlowDocument
        {
            PageWidth = A4Width,
            ColumnWidth = A4Width,
            PagePadding = new Thickness(40),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            Background = Brushes.White,
            Foreground = Brushes.Black
        };

        doc.Blocks.Add(BuildReturnHeader(r));
        doc.Blocks.Add(BuildReturnMeta(r));
        doc.Blocks.Add(BuildReturnItemsTable(r));
        doc.Blocks.Add(BuildReturnTotals(r));

        if (!string.IsNullOrWhiteSpace(r.Remarks))
        {
            doc.Blocks.Add(new Paragraph(new Run("Remarks: " + r.Remarks))
            {
                Margin = new Thickness(0, 12, 0, 0),
                FontSize = 11
            });
        }

        if (!string.IsNullOrWhiteSpace(r.InvoiceFooter))
        {
            doc.Blocks.Add(new Paragraph(new Run(r.InvoiceFooter))
            {
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 24, 0, 0),
                FontStyle = FontStyles.Italic,
                Foreground = Brushes.Gray
            });
        }

        return doc;
    }

    public void ShowReturnPreview(SaleReturnReceiptDto receipt)
    {
        var window = new ReturnReceiptPreviewWindow(this, receipt)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    public void PrintReturn(SaleReturnReceiptDto receipt)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;
        var doc = BuildReturnDocument(receipt);
        doc.PageWidth = dialog.PrintableAreaWidth;
        doc.ColumnWidth = dialog.PrintableAreaWidth;
        IDocumentPaginatorSource source = doc;
        dialog.PrintDocument(source.DocumentPaginator, $"Return {receipt.ReturnNumber}");
    }

    private static Block BuildHeader(SaleReceiptDto r)
    {
        var section = new Section();

        section.Blocks.Add(new Paragraph(new Run(r.CompanyName))
        {
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x69, 0x5C)),
            Margin = new Thickness(0)
        });

        var sub = new Paragraph { Margin = new Thickness(0, 2, 0, 0), FontSize = 11 };
        if (!string.IsNullOrWhiteSpace(r.CompanyAddress)) sub.Inlines.Add(new Run(r.CompanyAddress + "\n"));
        var line3 = new List<string>();
        if (!string.IsNullOrWhiteSpace(r.CompanyPhone)) line3.Add("Ph: " + r.CompanyPhone);
        if (!string.IsNullOrWhiteSpace(r.CompanyGst)) line3.Add("GSTIN: " + r.CompanyGst);
        if (!string.IsNullOrWhiteSpace(r.CompanyDrugLicense)) line3.Add("DL: " + r.CompanyDrugLicense);
        if (line3.Count > 0) sub.Inlines.Add(new Run(string.Join("   |   ", line3)));
        section.Blocks.Add(sub);

        section.Blocks.Add(new Paragraph(new Run("TAX INVOICE"))
        {
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(4),
            Background = new SolidColorBrush(Color.FromRgb(0xE0, 0xF2, 0xF1))
        });

        return section;
    }

    private static Block BuildReturnHeader(SaleReturnReceiptDto r)
    {
        var section = new Section();

        section.Blocks.Add(new Paragraph(new Run(r.CompanyName))
        {
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x69, 0x5C)),
            Margin = new Thickness(0)
        });

        var sub = new Paragraph { Margin = new Thickness(0, 2, 0, 0), FontSize = 11 };
        if (!string.IsNullOrWhiteSpace(r.CompanyAddress)) sub.Inlines.Add(new Run(r.CompanyAddress + "\n"));
        var line3 = new List<string>();
        if (!string.IsNullOrWhiteSpace(r.CompanyPhone)) line3.Add("Ph: " + r.CompanyPhone);
        if (!string.IsNullOrWhiteSpace(r.CompanyGst)) line3.Add("GSTIN: " + r.CompanyGst);
        if (!string.IsNullOrWhiteSpace(r.CompanyDrugLicense)) line3.Add("DL: " + r.CompanyDrugLicense);
        if (line3.Count > 0) sub.Inlines.Add(new Run(string.Join("   |   ", line3)));
        section.Blocks.Add(sub);

        section.Blocks.Add(new Paragraph(new Run("CREDIT NOTE / REFUND"))
        {
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(4),
            Background = new SolidColorBrush(Color.FromRgb(0xE0, 0xF2, 0xF1))
        });

        return section;
    }

    private static Block BuildMeta(SaleReceiptDto r)
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 12, 0, 8) };
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });

        var rg = new TableRowGroup();
        var row = new TableRow();

        var left = new List<string>
        {
            "Bill To: " + r.CustomerName
        };
        if (!string.IsNullOrWhiteSpace(r.CustomerPhone)) left.Add("Phone: " + r.CustomerPhone);
        if (!string.IsNullOrWhiteSpace(r.DoctorName)) left.Add("Doctor: " + r.DoctorName);

        var right = new List<string>
        {
            "Invoice No: " + r.InvoiceNumber,
            "Date: " + r.InvoiceDate.ToString("dd MMM yyyy, hh:mm tt", Inr)
        };

        row.Cells.Add(TextCell(string.Join("\n", left), TextAlignment.Left, bold: false));
        row.Cells.Add(TextCell(string.Join("\n", right), TextAlignment.Right, bold: false));
        rg.Rows.Add(row);
        table.RowGroups.Add(rg);
        return table;
    }

    private static Block BuildReturnMeta(SaleReturnReceiptDto r)
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 12, 0, 8) };
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });

        var rg = new TableRowGroup();
        var row = new TableRow();

        var left = new List<string>
        {
            "Bill To: " + r.CustomerName
        };
        if (!string.IsNullOrWhiteSpace(r.CustomerPhone)) left.Add("Phone: " + r.CustomerPhone);
        left.Add("Cashier: " + r.CashierName);

        var right = new List<string>
        {
            "Return No: " + r.ReturnNumber,
            "Against Invoice: " + r.OriginalInvoiceNumber,
            "Date: " + r.ReturnDate.ToString("dd MMM yyyy, hh:mm tt", Inr),
            "Refund Mode: " + r.RefundMode
        };
        if (!string.IsNullOrWhiteSpace(r.CreditNoteNumber))
            right.Add("Credit Note: " + r.CreditNoteNumber);

        row.Cells.Add(TextCell(string.Join("\n", left), TextAlignment.Left, bold: false));
        row.Cells.Add(TextCell(string.Join("\n", right), TextAlignment.Right, bold: false));
        rg.Rows.Add(row);
        table.RowGroups.Add(rg);
        return table;
    }

    private static Block BuildItemsTable(SaleReceiptDto r)
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 8, 0, 0), BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1) };
        double[] widths = { 0.4, 2.8, 1.0, 0.7, 0.6, 0.8, 0.8, 0.8, 0.7, 1.0 };
        foreach (var w in widths)
            table.Columns.Add(new TableColumn { Width = new GridLength(w, GridUnitType.Star) });

        var header = new TableRowGroup();
        var hr = new TableRow { Background = new SolidColorBrush(Color.FromRgb(0x26, 0xA6, 0x9A)) };
        foreach (var (text, align) in new[]
        {
            ("#", TextAlignment.Center), ("Item", TextAlignment.Left), ("Batch", TextAlignment.Left),
            ("Exp", TextAlignment.Center), ("Qty", TextAlignment.Right), ("MRP", TextAlignment.Right),
            ("Sale", TextAlignment.Right), ("Disc", TextAlignment.Right), ("GST%", TextAlignment.Right),
            ("Amount", TextAlignment.Right)
        })
        {
            hr.Cells.Add(TextCell(text, align, bold: true, foreground: Brushes.White));
        }
        header.Rows.Add(hr);
        table.RowGroups.Add(header);

        var body = new TableRowGroup();
        foreach (var l in r.Lines)
        {
            var row = new TableRow();
            row.Cells.Add(TextCell(l.SerialNo.ToString(), TextAlignment.Center));
            row.Cells.Add(TextCell(l.MedicineName, TextAlignment.Left));
            row.Cells.Add(TextCell(l.BatchNumber, TextAlignment.Left));
            row.Cells.Add(TextCell(l.ExpiryDate?.ToString("MM/yy") ?? "-", TextAlignment.Center));
            row.Cells.Add(TextCell(l.Quantity.ToString("0.##"), TextAlignment.Right));
            row.Cells.Add(TextCell(l.Mrp.ToString("N2", Inr), TextAlignment.Right));
            row.Cells.Add(TextCell(l.UnitPrice.ToString("N2", Inr), TextAlignment.Right));
            row.Cells.Add(TextCell(l.DiscountAmount != 0 ? l.DiscountAmount.ToString("N2", Inr) : "-", TextAlignment.Right));
            row.Cells.Add(TextCell(l.GstPercent.ToString("0.##"), TextAlignment.Right));
            row.Cells.Add(TextCell(l.Amount.ToString("N2", Inr), TextAlignment.Right));
            body.Rows.Add(row);
        }
        table.RowGroups.Add(body);
        return table;
    }

    private static Block BuildReturnItemsTable(SaleReturnReceiptDto r)
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 8, 0, 0), BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1) };
        double[] widths = { 0.4, 2.8, 1.0, 0.7, 0.6, 0.8, 0.8, 0.8, 0.7, 1.0 };
        foreach (var w in widths)
            table.Columns.Add(new TableColumn { Width = new GridLength(w, GridUnitType.Star) });

        var header = new TableRowGroup();
        var hr = new TableRow { Background = new SolidColorBrush(Color.FromRgb(0x26, 0xA6, 0x9A)) };
        foreach (var (text, align) in new[]
        {
            ("#", TextAlignment.Center), ("Item", TextAlignment.Left), ("Batch", TextAlignment.Left),
            ("Exp", TextAlignment.Center), ("Qty", TextAlignment.Right), ("MRP", TextAlignment.Right),
            ("Sale", TextAlignment.Right), ("Disc", TextAlignment.Right), ("GST%", TextAlignment.Right),
            ("Amount", TextAlignment.Right)
        })
        {
            hr.Cells.Add(TextCell(text, align, bold: true, foreground: Brushes.White));
        }
        header.Rows.Add(hr);
        table.RowGroups.Add(header);

        var body = new TableRowGroup();
        foreach (var l in r.Lines)
        {
            var itemLabel = l.MedicineName;
            if (!string.IsNullOrWhiteSpace(l.ReasonName) && l.ReasonName != "—")
                itemLabel += "\n(" + l.ReasonName + ")";

            var row = new TableRow();
            row.Cells.Add(TextCell(l.SrNo.ToString(), TextAlignment.Center));
            row.Cells.Add(TextCell(itemLabel, TextAlignment.Left));
            row.Cells.Add(TextCell(l.BatchNumber, TextAlignment.Left));
            row.Cells.Add(TextCell(l.ExpiryDate?.ToString("MM/yy") ?? "-", TextAlignment.Center));
            row.Cells.Add(TextCell(l.ReturnedQuantity.ToString("0.##"), TextAlignment.Right));
            row.Cells.Add(TextCell(l.Mrp.ToString("N2", Inr), TextAlignment.Right));
            row.Cells.Add(TextCell(l.UnitPrice.ToString("N2", Inr), TextAlignment.Right));
            row.Cells.Add(TextCell(l.DiscountAmount != 0 ? l.DiscountAmount.ToString("N2", Inr) : "-", TextAlignment.Right));
            row.Cells.Add(TextCell(l.GstPercent.ToString("0.##"), TextAlignment.Right));
            row.Cells.Add(TextCell(l.LineTotal.ToString("N2", Inr), TextAlignment.Right));
            body.Rows.Add(row);
        }
        table.RowGroups.Add(body);
        return table;
    }

    private static Block BuildTotals(SaleReceiptDto r)
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 10, 0, 0) };
        table.Columns.Add(new TableColumn { Width = new GridLength(2, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });

        var rg = new TableRowGroup();
        void AddRow(string label, decimal value, bool bold = false, bool big = false)
        {
            var row = new TableRow();
            row.Cells.Add(TextCell(label, TextAlignment.Right, bold));
            var cell = TextCell("₹ " + value.ToString("N2", Inr), TextAlignment.Right, bold);
            if (big) cell.Blocks.OfType<Paragraph>().First().FontSize = 15;
            row.Cells.Add(cell);
            rg.Rows.Add(row);
        }

        AddRow("Sub Total (MRP)", r.SubTotal);
        if (r.DiscountAmount != 0) AddRow("Discount", r.DiscountAmount);
        AddRow("Taxable", r.TaxableAmount);
        if (r.CgstAmount != 0) AddRow("CGST", r.CgstAmount);
        if (r.SgstAmount != 0) AddRow("SGST", r.SgstAmount);
        if (r.RoundOff != 0) AddRow("Round Off", r.RoundOff);
        AddRow("Grand Total", r.GrandTotal, bold: true, big: true);

        table.RowGroups.Add(rg);

        var section = new Section();
        section.Blocks.Add(table);
        if (r.RewardPointsEarned > 0)
        {
            section.Blocks.Add(new Paragraph(new Run($"Reward points earned: {r.RewardPointsEarned}"))
            {
                FontSize = 10,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 6, 0, 0),
                TextAlignment = TextAlignment.Right
            });
        }
        return section;
    }

    private static Block BuildReturnTotals(SaleReturnReceiptDto r)
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 10, 0, 0) };
        table.Columns.Add(new TableColumn { Width = new GridLength(2, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });

        var rg = new TableRowGroup();
        void AddRow(string label, decimal value, bool bold = false, bool big = false)
        {
            var row = new TableRow();
            row.Cells.Add(TextCell(label, TextAlignment.Right, bold));
            var cell = TextCell("₹ " + value.ToString("N2", Inr), TextAlignment.Right, bold);
            if (big) cell.Blocks.OfType<Paragraph>().First().FontSize = 15;
            row.Cells.Add(cell);
            rg.Rows.Add(row);
        }

        AddRow("Sub Total (MRP)", r.SubTotal);
        if (r.DiscountAmount != 0) AddRow("Discount", r.DiscountAmount);
        AddRow("Taxable", r.TaxableAmount);
        if (r.CgstAmount != 0) AddRow("CGST", r.CgstAmount);
        if (r.SgstAmount != 0) AddRow("SGST", r.SgstAmount);
        AddRow("Refund Total", r.GrandTotal, bold: true, big: true);

        table.RowGroups.Add(rg);

        var section = new Section();
        section.Blocks.Add(table);

        var refundNote = $"Refund Amount: ₹ {r.RefundAmount.ToString("N2", Inr)} ({r.RefundMode})";
        if (r.Refunds.Count > 1)
        {
            refundNote += "\n" + string.Join("\n", r.Refunds.Select(x =>
                $"  • {x.Mode}: ₹ {x.Amount.ToString("N2", Inr)}"
                + (string.IsNullOrWhiteSpace(x.TransactionReference) ? "" : $" ({x.TransactionReference})")));
        }
        if (!string.IsNullOrWhiteSpace(r.CreditNoteNumber))
        {
            refundNote += $"\nCredit Note: {r.CreditNoteNumber}";
            if (r.CreditNoteExpiry is not null)
                refundNote += $" (valid till {r.CreditNoteExpiry:dd MMM yyyy})";
        }

        section.Blocks.Add(new Paragraph(new Run(refundNote))
        {
            FontSize = 10,
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 6, 0, 0),
            TextAlignment = TextAlignment.Right
        });

        return section;
    }

    private static TableCell TextCell(string text, TextAlignment align, bool bold = false, Brush? foreground = null)
    {
        var para = new Paragraph(new Run(text))
        {
            TextAlignment = align,
            Margin = new Thickness(4, 3, 4, 3),
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            FontSize = 11
        };
        if (foreground is not null) para.Foreground = foreground;
        return new TableCell(para)
        {
            BorderBrush = Brushes.Gainsboro,
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
    }
}
