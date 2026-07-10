using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace PharmaPOS.WPF.Services;

/// <summary>Exports report rows to a simple CSV file.</summary>
public static class ReportCsvExporter
{
    public static void Export<T>(string filePath, IEnumerable<T> rows, IReadOnlyList<string>? columnOrder = null)
    {
        var list = rows.ToList();
        var props = ResolveColumns<T>(columnOrder);
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", props.Select(p => Escape(p.Name))));

        foreach (var row in list)
        {
            var values = props.Select(p => FormatValue(p.GetValue(row)));
            sb.AppendLine(string.Join(",", values.Select(Escape)));
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }

    private static PropertyInfo[] ResolveColumns<T>(IReadOnlyList<string>? columnOrder)
    {
        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        if (columnOrder is { Count: > 0 })
            return columnOrder.Where(props.ContainsKey).Select(n => props[n]).ToArray();

        return props.Values
            .Where(p => p.PropertyType.IsPrimitive || p.PropertyType == typeof(string) ||
                        p.PropertyType == typeof(decimal) || p.PropertyType == typeof(DateTime) ||
                        Nullable.GetUnderlyingType(p.PropertyType) != null)
            .OrderBy(p => p.Name)
            .ToArray();
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "",
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            decimal d => d.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString(CultureInfo.InvariantCulture),
            float f => f.ToString(CultureInfo.InvariantCulture),
            bool b => b ? "Yes" : "No",
            _ => value.ToString() ?? ""
        };
    }

    private static string Escape(string? value)
    {
        value ??= "";
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
