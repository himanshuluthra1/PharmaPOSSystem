using System.Globalization;
using System.IO.Compression;
using System.Text;
using PharmaPOS.Application.Features.Reports;

namespace PharmaPOS.WPF.Services;

/// <summary>Lightweight single-sheet .xlsx export for generic report tables.</summary>
public static class ReportExcelExporter
{
    public static void Export(
        string filePath,
        string sheetName,
        IReadOnlyList<ReportColumnDto> columns,
        IEnumerable<Dictionary<string, object?>> rows)
    {
        var headers = columns.Select(c => c.Header).ToList();
        var dataRows = rows.Select(row => (IReadOnlyList<string>)columns.Select(c =>
        {
            row.TryGetValue(c.Key, out var raw);
            return FormatCell(raw, c.Format);
        }).ToList()).ToList();

        var sheet = new GstReturnExcelSheetDto
        {
            Name = string.IsNullOrWhiteSpace(sheetName) ? "Report" : sheetName,
            Headers = headers,
            Rows = dataRows
        };
        GstReturnWorkbookWriter.Write(filePath, [sheet]);
    }

    private static string FormatCell(object? value, string? format) => value switch
    {
        null => "",
        DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        decimal d when !string.IsNullOrWhiteSpace(format) => d.ToString(format, CultureInfo.InvariantCulture),
        decimal d => d.ToString(CultureInfo.InvariantCulture),
        double d => d.ToString(CultureInfo.InvariantCulture),
        float f => f.ToString(CultureInfo.InvariantCulture),
        bool b => b ? "Yes" : "No",
        _ => value.ToString() ?? ""
    };
}
