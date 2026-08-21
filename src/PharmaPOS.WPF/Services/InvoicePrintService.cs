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
using PharmaPOS.Application.Features.Accounting;
using PharmaPOS.Application.Features.Counters;
using PharmaPOS.Application.Features.Reports;
using PharmaPOS.Application.Features.Sales;
using PharmaPOS.Application.Features.SaleReturns;
using PharmaPOS.Domain.Enums;
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

    public FlowDocument BuildDayCloseDocument(CounterDayCloseDto r)
    {
        var doc = new FlowDocument
        {
            PageWidth = A4Width,
            PageHeight = A4Height,
            ColumnWidth = A4Width,
            PagePadding = new Thickness(48),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            Background = Brushes.White,
            Foreground = Brushes.Black
        };

        doc.Blocks.Add(new Paragraph(new Run(r.CompanyName))
        {
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2)
        });
        doc.Blocks.Add(new Paragraph(new Run("Counter day close"))
        {
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16)
        });

        doc.Blocks.Add(DayCloseLine("Counter", $"{r.CounterCode}  ·  {r.CounterName}"));
        doc.Blocks.Add(DayCloseLine("Operator", r.OperatorName));
        doc.Blocks.Add(DayCloseLine("Opened", r.OpenedAtLocal.ToString("dd-MMM-yyyy hh:mm tt")));
        doc.Blocks.Add(DayCloseLine("Closed", r.ClosedAtLocal?.ToString("dd-MMM-yyyy hh:mm tt") ?? "—"));
        if (!string.IsNullOrWhiteSpace(r.MachineName))
            doc.Blocks.Add(DayCloseLine("Machine", r.MachineName));

        doc.Blocks.Add(new Paragraph(new Run("Collections"))
        {
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 18, 0, 8)
        });
        doc.Blocks.Add(DayCloseAmount("Bills", r.BillCount.ToString("N0", Inr)));
        doc.Blocks.Add(DayCloseAmount("Opening float", r.OpeningFloat));
        doc.Blocks.Add(DayCloseAmount("Cash collected", r.CashCollected));
        doc.Blocks.Add(DayCloseAmount("Card", r.CardCollected));
        doc.Blocks.Add(DayCloseAmount("UPI", r.UpiCollected));
        if (r.OtherCollected != 0)
            doc.Blocks.Add(DayCloseAmount("Other", r.OtherCollected));
        if (r.CreditCollected != 0)
            doc.Blocks.Add(DayCloseAmount("Credit / unpaid", r.CreditCollected));

        doc.Blocks.Add(new Paragraph(new Run("Cash drawer"))
        {
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 18, 0, 8)
        });
        doc.Blocks.Add(DayCloseAmount("System cash (float + cash sales)", r.ExpectedCashInDrawer, bold: true));
        doc.Blocks.Add(DayCloseAmount("Cash counted", r.CountedCash, bold: true));
        doc.Blocks.Add(DayCloseAmount(r.VarianceLabel, r.Variance, bold: true));

        doc.Blocks.Add(new Paragraph(new Run(
            "Shortage means counted cash is less than the system drawer. Excess means more cash was counted."))
        {
            FontSize = 10,
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 16, 0, 0)
        });

        if (!string.IsNullOrWhiteSpace(r.Remarks))
        {
            doc.Blocks.Add(new Paragraph(new Run("Notes: " + r.Remarks))
            {
                Margin = new Thickness(0, 12, 0, 0),
                FontSize = 11
            });
        }

        doc.Blocks.Add(new Paragraph(new Run("Operator sign ________________     Owner sign ________________"))
        {
            Margin = new Thickness(0, 36, 0, 0),
            FontSize = 11
        });

        return doc;
    }

    public void ShowDayClosePreview(CounterDayCloseDto report)
    {
        var window = new DayClosePreviewWindow(this, report)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    public void PrintDayClose(CounterDayCloseDto report)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;
        var doc = BuildDayCloseDocument(report);
        doc.PageWidth = dialog.PrintableAreaWidth;
        doc.ColumnWidth = dialog.PrintableAreaWidth;
        IDocumentPaginatorSource source = doc;
        dialog.PrintDocument(source.DocumentPaginator,
            $"Day close {report.CounterCode} {report.OpenedAtLocal:yyyyMMdd}");
    }

    public string ExportDayClosePdf(CounterDayCloseDto report, string? destinationPath = null)
    {
        var path = destinationPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "PharmaPOS");
            Directory.CreateDirectory(dir);
            path = Path.Combine(dir,
                $"DayClose-{report.CounterCode}-{DateTime.Now:yyyyMMdd-HHmmss}.pdf");
        }

        var dirName = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dirName))
            Directory.CreateDirectory(dirName);

        var doc = BuildDayCloseDocument(report);
        doc.PageWidth = A4Width;
        doc.PageHeight = A4Height;
        doc.ColumnWidth = A4Width;

        IDocumentPaginatorSource source = doc;
        var paginator = source.DocumentPaginator;
        paginator.PageSize = new Size(A4Width, A4Height);
        paginator.ComputePageCount();

        using var pdf = new PdfDocument();
        pdf.Info.Title = $"Day close {report.CounterCode}";
        pdf.Info.Author = report.CompanyName;

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

    private static Paragraph DayCloseLine(string label, string value) =>
        new(new Run($"{label}:  {value}")) { Margin = new Thickness(0, 2, 0, 2) };

    private static Paragraph DayCloseAmount(string label, decimal value, bool bold = false) =>
        DayCloseAmount(label, "₹ " + value.ToString("N2", Inr), bold);

    private static Paragraph DayCloseAmount(string label, string value, bool bold = false)
    {
        var para = new Paragraph { Margin = new Thickness(0, 2, 0, 2) };
        para.Inlines.Add(new Run(label + ":  ") { FontWeight = bold ? FontWeights.Bold : FontWeights.Normal });
        para.Inlines.Add(new Run(value) { FontWeight = bold ? FontWeights.Bold : FontWeights.Normal });
        return para;
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

    public FlowDocument BuildCollectionDocument(CustomerCollectionReceiptDto r)
    {
        var doc = new FlowDocument
        {
            PageWidth = A4Width,
            PageHeight = A4Height,
            ColumnWidth = A4Width,
            PagePadding = new Thickness(48),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            Background = Brushes.White,
            Foreground = Brushes.Black
        };

        doc.Blocks.Add(new Paragraph(new Run(r.CompanyName))
        {
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2)
        });
        doc.Blocks.Add(new Paragraph(new Run("Payment receipt"))
        {
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16)
        });

        doc.Blocks.Add(DayCloseLine("Receipt no.", r.VoucherNumber));
        doc.Blocks.Add(DayCloseLine("Date", r.EntryDate.ToString("dd-MMM-yyyy")));
        doc.Blocks.Add(DayCloseLine("Customer", r.CustomerName));
        if (!string.IsNullOrWhiteSpace(r.CustomerPhone))
            doc.Blocks.Add(DayCloseLine("Mobile", r.CustomerPhone!));
        if (!string.IsNullOrWhiteSpace(r.ReceivedInAccount))
            doc.Blocks.Add(DayCloseLine("Received in", r.ReceivedInAccount!));

        doc.Blocks.Add(new Paragraph(new Run("Amount"))
        {
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 18, 0, 8)
        });
        doc.Blocks.Add(DayCloseAmount("Collected", r.AmountCollected, bold: true));
        doc.Blocks.Add(DayCloseAmount("Balance due after", r.OutstandingAfter, bold: true));

        if (!string.IsNullOrWhiteSpace(r.Narration))
        {
            doc.Blocks.Add(new Paragraph(new Run("Notes: " + r.Narration))
            {
                Margin = new Thickness(0, 12, 0, 0),
                FontSize = 11
            });
        }

        doc.Blocks.Add(new Paragraph(new Run("Thank you for your payment."))
        {
            Margin = new Thickness(0, 24, 0, 0),
            FontStyle = FontStyles.Italic,
            Foreground = Brushes.Gray
        });

        doc.Blocks.Add(new Paragraph(new Run("Customer sign ________________     Shop sign ________________"))
        {
            Margin = new Thickness(0, 36, 0, 0),
            FontSize = 11
        });

        return doc;
    }

    public void ShowCollectionPreview(CustomerCollectionReceiptDto receipt)
    {
        var window = new CollectionReceiptPreviewWindow(this, receipt)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    public void PrintCollection(CustomerCollectionReceiptDto receipt)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;
        var doc = BuildCollectionDocument(receipt);
        doc.PageWidth = dialog.PrintableAreaWidth;
        doc.ColumnWidth = dialog.PrintableAreaWidth;
        IDocumentPaginatorSource source = doc;
        dialog.PrintDocument(source.DocumentPaginator, $"Receipt {receipt.VoucherNumber}");
    }

    public FlowDocument BuildScheduleRegisterDocument(ScheduleRegisterReportDto r)
    {
        // Landscape A4 for inspector columns.
        const double pageW = A4Height;
        const double pageH = A4Width;

        var doc = new FlowDocument
        {
            PageWidth = pageW,
            PageHeight = pageH,
            ColumnWidth = pageW,
            PagePadding = new Thickness(36),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 10,
            Background = Brushes.White,
            Foreground = Brushes.Black
        };

        doc.Blocks.Add(new Paragraph(new Run(r.CompanyName))
        {
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0)
        });

        if (!string.IsNullOrWhiteSpace(r.Address) || !string.IsNullOrWhiteSpace(r.Phone))
        {
            var sub = new Paragraph { Margin = new Thickness(0, 2, 0, 0), FontSize = 10 };
            if (!string.IsNullOrWhiteSpace(r.Address)) sub.Inlines.Add(new Run(r.Address));
            if (!string.IsNullOrWhiteSpace(r.Phone))
            {
                if (sub.Inlines.Count > 0) sub.Inlines.Add(new Run("  ·  "));
                sub.Inlines.Add(new Run("Ph: " + r.Phone));
            }
            doc.Blocks.Add(sub);
        }

        if (!string.IsNullOrWhiteSpace(r.DrugLicenseNumber))
        {
            doc.Blocks.Add(new Paragraph(new Run("Drug Licence: " + r.DrugLicenseNumber))
            {
                Margin = new Thickness(0, 2, 0, 0),
                FontSize = 10
            });
        }

        doc.Blocks.Add(new Paragraph(new Run($"{r.FilterLabel} Register"))
        {
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 2)
        });

        doc.Blocks.Add(new Paragraph(new Run(
            $"Period: {r.FromDate:dd-MMM-yyyy} to {r.ToDate:dd-MMM-yyyy}  ·  " +
            $"{r.RecordCount} line(s)  ·  Total qty {r.TotalQuantity:0.##}"))
        {
            Margin = new Thickness(0, 0, 0, 10),
            FontSize = 10
        });

        var table = new Table { CellSpacing = 0 };
        table.Columns.Add(new TableColumn { Width = new GridLength(70) });
        table.Columns.Add(new TableColumn { Width = new GridLength(90) });
        table.Columns.Add(new TableColumn { Width = new GridLength(130) });
        table.Columns.Add(new TableColumn { Width = new GridLength(130) });
        table.Columns.Add(new TableColumn { Width = new GridLength(160) });
        table.Columns.Add(new TableColumn { Width = new GridLength(36) });
        table.Columns.Add(new TableColumn { Width = new GridLength(70) });
        table.Columns.Add(new TableColumn { Width = new GridLength(50) });
        table.RowGroups.Add(new TableRowGroup());

        var header = new TableRow();
        foreach (var h in new[] { "Date", "Invoice", "Patient", "Doctor", "Medicine", "Sch", "Batch", "Qty" })
            header.Cells.Add(ScheduleCell(h, bold: true, header: true));
        table.RowGroups[0].Rows.Add(header);

        foreach (var row in r.Rows)
        {
            var tr = new TableRow();
            tr.Cells.Add(ScheduleCell(row.InvoiceDateLabel));
            tr.Cells.Add(ScheduleCell(row.InvoiceNumber));
            tr.Cells.Add(ScheduleCell(row.PatientName + (string.IsNullOrWhiteSpace(row.PatientPhone) ? "" : $"\n{row.PatientPhone}")));
            tr.Cells.Add(ScheduleCell(row.DoctorDisplay));
            tr.Cells.Add(ScheduleCell(row.MedicineName));
            tr.Cells.Add(ScheduleCell(row.ScheduleLabel));
            tr.Cells.Add(ScheduleCell(string.IsNullOrWhiteSpace(row.BatchNumber) ? "—" : row.BatchNumber));
            tr.Cells.Add(ScheduleCell(row.Quantity.ToString("0.##"), right: true));
            table.RowGroups[0].Rows.Add(tr);
        }

        doc.Blocks.Add(table);

        doc.Blocks.Add(new Paragraph(new Run(
            "Certified that the above particulars are true to the best of my knowledge."))
        {
            Margin = new Thickness(0, 18, 0, 28),
            FontSize = 9,
            FontStyle = FontStyles.Italic
        });

        var sign = new Paragraph { Margin = new Thickness(0, 24, 0, 0) };
        sign.Inlines.Add(new Run("Pharmacist / Authorised signatory ____________________"));
        doc.Blocks.Add(sign);

        doc.Blocks.Add(new Paragraph(new Run($"Printed {DateTime.Now:dd-MMM-yyyy hh:mm tt}"))
        {
            Margin = new Thickness(0, 16, 0, 0),
            FontSize = 8,
            Foreground = Brushes.Gray
        });

        return doc;
    }

    public void ShowScheduleRegisterPreview(ScheduleRegisterReportDto report)
    {
        var window = new ScheduleRegisterPreviewWindow(this, report)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    public void PrintScheduleRegister(ScheduleRegisterReportDto report)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;
        var doc = BuildScheduleRegisterDocument(report);
        IDocumentPaginatorSource source = doc;
        dialog.PrintDocument(source.DocumentPaginator, $"{report.FilterLabel} Register");
    }

    public string ExportScheduleRegisterPdf(ScheduleRegisterReportDto report, string? destinationPath = null)
    {
        var path = destinationPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "PharmaPOS");
            Directory.CreateDirectory(dir);
            path = Path.Combine(dir,
                $"ScheduleRegister-{report.FromDate:yyyyMMdd}-{report.ToDate:yyyyMMdd}.pdf");
        }

        var dirName = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dirName))
            Directory.CreateDirectory(dirName);

        const double pageW = A4Height;
        const double pageH = A4Width;

        var doc = BuildScheduleRegisterDocument(report);
        doc.PageWidth = pageW;
        doc.PageHeight = pageH;
        doc.ColumnWidth = pageW;

        IDocumentPaginatorSource source = doc;
        var paginator = source.DocumentPaginator;
        paginator.PageSize = new Size(pageW, pageH);
        paginator.ComputePageCount();

        using var pdf = new PdfDocument();
        pdf.Info.Title = $"{report.FilterLabel} Register";
        pdf.Info.Author = report.CompanyName;

        for (var i = 0; i < paginator.PageCount; i++)
        {
            var page = paginator.GetPage(i);
            var container = new ContainerVisual();
            container.Children.Add(page.Visual);

            var width = (int)Math.Ceiling(pageW);
            var height = (int)Math.Ceiling(pageH);
            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(container);
            rtb.Freeze();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            ms.Position = 0;

            var pdfPage = pdf.AddPage();
            pdfPage.Width = XUnit.FromPoint(pageW * 72.0 / 96.0);
            pdfPage.Height = XUnit.FromPoint(pageH * 72.0 / 96.0);

            using var gfx = XGraphics.FromPdfPage(pdfPage);
            using var image = XImage.FromStream(ms);
            gfx.DrawImage(image, 0, 0, pdfPage.Width.Point, pdfPage.Height.Point);
        }

        pdf.Save(path);
        return path;
    }

    private static TableCell ScheduleCell(string text, bool bold = false, bool header = false, bool right = false)
    {
        var para = new Paragraph(new Run(text ?? string.Empty))
        {
            Margin = new Thickness(3, 2, 3, 2),
            FontSize = header ? 9 : 9,
            FontWeight = bold || header ? FontWeights.SemiBold : FontWeights.Normal,
            TextAlignment = right ? TextAlignment.Right : TextAlignment.Left
        };
        var cell = new TableCell(para)
        {
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(0.5),
            Padding = new Thickness(1)
        };
        if (header)
            cell.Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));
        return cell;
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
        foreach (var payment in r.Payments.Where(p => p.Amount > 0))
            AddRow(PaymentMethodLabel(payment.Method), payment.Amount);
        if (r.ChangeReturned > 0)
            AddRow("Change", r.ChangeReturned);
        var due = r.GrandTotal - Math.Min(r.PaidAmount, r.GrandTotal);
        if (due > 0.009m)
            AddRow("Balance Due", due, bold: true);

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

    internal static string PaymentMethodLabel(PaymentMethod method) => method switch
    {
        PaymentMethod.Upi => "UPI",
        PaymentMethod.BankTransfer => "Bank transfer",
        _ => method.ToString()
    };

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
