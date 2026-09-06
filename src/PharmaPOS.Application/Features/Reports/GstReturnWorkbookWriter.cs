using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace PharmaPOS.Application.Features.Reports;

/// <summary>Minimal .xlsx writer (inline strings) so GST returns open in Excel without extra packages.</summary>
public static class GstReturnWorkbookWriter
{
    public static void Write(string filePath, IReadOnlyList<GstReturnExcelSheetDto> sheets)
    {
        if (sheets.Count == 0)
            throw new InvalidOperationException("No worksheets to export.");

        using var stream = File.Create(filePath);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        WriteEntry(zip, "[Content_Types].xml", ContentTypes(sheets.Count));
        WriteEntry(zip, "_rels/.rels", RelsRoot());
        WriteEntry(zip, "xl/workbook.xml", Workbook(sheets));
        WriteEntry(zip, "xl/_rels/workbook.xml.rels", WorkbookRels(sheets.Count));
        WriteEntry(zip, "xl/styles.xml", Styles());

        for (var i = 0; i < sheets.Count; i++)
            WriteEntry(zip, $"xl/worksheets/sheet{i + 1}.xml", Worksheet(sheets[i]));
    }

    private static void WriteEntry(ZipArchive zip, string name, string xml)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(xml);
    }

    private static string ContentTypes(int sheetCount)
    {
        var sb = new StringBuilder();
        sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        sb.Append("""<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">""");
        sb.Append("""<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>""");
        sb.Append("""<Default Extension="xml" ContentType="application/xml"/>""");
        sb.Append("""<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>""");
        sb.Append("""<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>""");
        for (var i = 1; i <= sheetCount; i++)
            sb.Append($"<Override PartName=\"/xl/worksheets/sheet{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        sb.Append("</Types>");
        return sb.ToString();
    }

    private static string RelsRoot() =>
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""";

    private static string WorkbookRels(int sheetCount)
    {
        var sb = new StringBuilder();
        sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""");
        sb.Append("""<Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>""");
        for (var i = 1; i <= sheetCount; i++)
            sb.Append($"<Relationship Id=\"rId{i}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i}.xml\"/>");
        sb.Append("</Relationships>");
        return sb.ToString();
    }

    private static string Workbook(IReadOnlyList<GstReturnExcelSheetDto> sheets)
    {
        var sb = new StringBuilder();
        sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>""");
        for (var i = 0; i < sheets.Count; i++)
        {
            var name = SanitizeSheetName(sheets[i].Name, i);
            sb.Append($"<sheet name=\"{XmlEscape(name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
        }
        sb.Append("</sheets></workbook>");
        return sb.ToString();
    }

    private static string Styles() =>
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="1"><font><sz val="11"/><name val="Calibri"/></font></fonts><fills count="1"><fill><patternFill patternType="none"/></fill></fills><borders count="1"><border/></borders><cellStyleXfs count="1"><xf/></cellStyleXfs><cellXfs count="1"><xf/></cellXfs></styleSheet>""";

    private static string Worksheet(GstReturnExcelSheetDto sheet)
    {
        var sb = new StringBuilder();
        sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");

        var rowIndex = 1;
        WriteRow(sb, rowIndex++, sheet.Headers);
        foreach (var row in sheet.Rows)
            WriteRow(sb, rowIndex++, row);

        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private static void WriteRow(StringBuilder sb, int rowIndex, IReadOnlyList<string> cells)
    {
        sb.Append($"<row r=\"{rowIndex}\">");
        for (var c = 0; c < cells.Count; c++)
        {
            var refName = ColumnName(c) + rowIndex;
            var value = cells[c] ?? "";
            if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) &&
                !value.Contains('/') && value.Length > 0 && char.IsDigit(value[0]))
            {
                sb.Append($"<c r=\"{refName}\"><v>{XmlEscape(n.ToString(CultureInfo.InvariantCulture))}</v></c>");
            }
            else
            {
                sb.Append($"<c r=\"{refName}\" t=\"inlineStr\"><is><t>{XmlEscape(value)}</t></is></c>");
            }
        }
        sb.Append("</row>");
    }

    private static string ColumnName(int index)
    {
        var n = index + 1;
        var s = "";
        while (n > 0)
        {
            n--;
            s = (char)('A' + n % 26) + s;
            n /= 26;
        }
        return s;
    }

    private static string SanitizeSheetName(string name, int index)
    {
        var cleaned = string.Join("_", name.Split(['\\', '/', '*', '?', ':', '[', ']'], StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = $"Sheet{index + 1}";
        return cleaned.Length > 31 ? cleaned[..31] : cleaned;
    }

    private static string XmlEscape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
