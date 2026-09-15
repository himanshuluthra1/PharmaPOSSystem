using System.IO;
using System.Windows;
using PharmaPOS.Domain.Enums;

namespace PharmaPOS.WPF.Services;

public sealed record InvoicePageLayout(
    InvoicePaperSize Size,
    double Width,
    double Height,
    Thickness Padding,
    double FontSize,
    bool IsThermal,
    bool CompactColumns)
{
    public static InvoicePageLayout For(InvoicePaperSize size) => size switch
    {
        InvoicePaperSize.A5 => new(size, 559, 794, new Thickness(28), 11, false, true),
        InvoicePaperSize.Thermal80 => new(size, 302, 1800, new Thickness(10, 8, 10, 8), 10, true, true),
        InvoicePaperSize.Thermal58 => new(size, 219, 2000, new Thickness(6), 9, true, true),
        _ => new(InvoicePaperSize.A4, 794, 1123, new Thickness(40), 12, false, false)
    };

    public static string Label(InvoicePaperSize size) => size switch
    {
        InvoicePaperSize.A5 => "A5 (148 × 210 mm)",
        InvoicePaperSize.Thermal80 => "Thermal 80 mm",
        InvoicePaperSize.Thermal58 => "Thermal 58 mm",
        _ => "A4 (210 × 297 mm)"
    };

    /// <summary>Suggested PDF name: BillNo-CustomerName.pdf</summary>
    public static string BuildPdfFileName(string? invoiceNumber, string? customerName)
    {
        static string Sanitize(string? value, string fallback)
        {
            var raw = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            foreach (var c in Path.GetInvalidFileNameChars())
                raw = raw.Replace(c, '_');
            raw = raw.Replace(' ', '_');
            while (raw.Contains("__", StringComparison.Ordinal))
                raw = raw.Replace("__", "_", StringComparison.Ordinal);
            return string.IsNullOrWhiteSpace(raw) ? fallback : raw.Trim('_');
        }

        var bill = Sanitize(invoiceNumber, "bill");
        var customer = Sanitize(customerName, "Customer");
        return $"{bill}-{customer}.pdf";
    }
}

public readonly record struct InvoicePaperSizeOption(InvoicePaperSize Value, string Label);
