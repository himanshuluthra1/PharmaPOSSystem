using System.Globalization;
using System.IO;
using PharmaPOS.Application.Features.Reports;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace PharmaPOS.WPF.Services;

/// <summary>Simple landscape PDF table export for reports.</summary>
public static class ReportPdfExporter
{
    public static void Export(
        string filePath,
        string title,
        string subtitle,
        IReadOnlyList<ReportColumnDto> columns,
        IEnumerable<Dictionary<string, object?>> rows)
    {
        var rowList = rows.ToList();
        using var document = new PdfDocument();
        document.Info.Title = title;

        const double margin = 28;
        const double rowHeight = 14;
        const double headerHeight = 18;
        var page = document.AddPage();
        page.Orientation = PdfSharp.PageOrientation.Landscape;
        var gfx = XGraphics.FromPdfPage(page);
        var titleFont = new XFont("Calibri", 14, XFontStyleEx.Bold);
        var subFont = new XFont("Calibri", 9, XFontStyleEx.Regular);
        var headerFont = new XFont("Calibri", 8, XFontStyleEx.Bold);
        var cellFont = new XFont("Calibri", 7.5, XFontStyleEx.Regular);

        var usableWidth = page.Width.Point - margin * 2;
        var colWidths = AllocateWidths(columns.Count, usableWidth);
        var y = margin;

        void EnsureSpace(double needed)
        {
            if (y + needed <= page.Height.Point - margin) return;
            DrawPageNumber(gfx, page, document.PageCount);
            gfx.Dispose();
            page = document.AddPage();
            page.Orientation = PdfSharp.PageOrientation.Landscape;
            gfx = XGraphics.FromPdfPage(page);
            y = margin;
            DrawTableHeader(gfx, columns, colWidths, margin, ref y, headerHeight, headerFont);
        }

        gfx.DrawString(title, titleFont, XBrushes.Black, new XRect(margin, y, usableWidth, 20), XStringFormats.TopLeft);
        y += 22;
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            gfx.DrawString(subtitle, subFont, XBrushes.DimGray, new XRect(margin, y, usableWidth, 14), XStringFormats.TopLeft);
            y += 16;
        }

        DrawTableHeader(gfx, columns, colWidths, margin, ref y, headerHeight, headerFont);

        foreach (var row in rowList)
        {
            EnsureSpace(rowHeight);
            var x = margin;
            for (var i = 0; i < columns.Count; i++)
            {
                row.TryGetValue(columns[i].Key, out var raw);
                var text = FormatCell(raw, columns[i].Format);
                if (text.Length > 42) text = text[..39] + "...";
                gfx.DrawString(text, cellFont, XBrushes.Black,
                    new XRect(x + 1, y, colWidths[i] - 2, rowHeight), XStringFormats.CenterLeft);
                x += colWidths[i];
            }
            y += rowHeight;
        }

        DrawPageNumber(gfx, page, document.PageCount);
        gfx.Dispose();

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        document.Save(filePath);
    }

    private static void DrawTableHeader(
        XGraphics gfx,
        IReadOnlyList<ReportColumnDto> columns,
        double[] colWidths,
        double margin,
        ref double y,
        double headerHeight,
        XFont headerFont)
    {
        var x = margin;
        for (var i = 0; i < columns.Count; i++)
        {
            gfx.DrawRectangle(XBrushes.LightGray, x, y, colWidths[i], headerHeight);
            gfx.DrawString(columns[i].Header, headerFont, XBrushes.Black,
                new XRect(x + 1, y, colWidths[i] - 2, headerHeight), XStringFormats.CenterLeft);
            x += colWidths[i];
        }
        y += headerHeight + 2;
    }

    private static void DrawPageNumber(XGraphics gfx, PdfPage page, int pageNumber)
    {
        var font = new XFont("Calibri", 8, XFontStyleEx.Regular);
        gfx.DrawString($"Page {pageNumber}", font, XBrushes.Gray,
            new XRect(0, page.Height.Point - 22, page.Width.Point - 28, 14), XStringFormats.TopRight);
    }

    private static double[] AllocateWidths(int count, double total)
    {
        if (count <= 0) return [];
        var w = total / count;
        return Enumerable.Repeat(w, count).ToArray();
    }

    private static string FormatCell(object? value, string? format) => value switch
    {
        null => "",
        DateTime dt => dt.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture),
        decimal d when !string.IsNullOrWhiteSpace(format) => d.ToString(format, CultureInfo.InvariantCulture),
        decimal d => d.ToString("N2", CultureInfo.InvariantCulture),
        bool b => b ? "Yes" : "No",
        _ => value.ToString() ?? ""
    };
}
