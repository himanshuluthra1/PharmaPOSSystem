using System.Globalization;
using System.Text.RegularExpressions;

namespace PharmaPOS.MedWinImport;

internal static class ImportHelpers
{
    private static readonly Regex GstinRegex = new(@"([0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][0-9A-Z]Z[0-9A-Z])", RegexOptions.Compiled);

    public static string? Trunc(string? value, int max)
        => string.IsNullOrWhiteSpace(value) ? null : (value.Trim().Length <= max ? value.Trim() : value.Trim()[..max]);

    public static decimal Dec(object? value)
    {
        if (value is null or DBNull) return 0m;
        if (value is decimal d) return d;
        if (value is double db) return (decimal)db;
        if (value is float f) return (decimal)f;
        if (value is int i) return i;
        if (value is long l) return l;
        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;
    }

    public static int Int(object? value)
    {
        if (value is null or DBNull) return 0;
        if (value is int i) return i;
        if (value is short s) return s;
        if (value is long l) return (int)l;
        return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed) ? parsed : 0;
    }

    public static DateTime? Date(object? value)
    {
        if (value is null or DBNull) return null;
        if (value is DateTime dt) return dt;
        return DateTime.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed) ? parsed : null;
    }

    /// <summary>MedWin stores month/year; year is often two digits (e.g. 26 = 2026).</summary>
    public static DateTime? ParseExpiryMonthYear(int year, int month)
    {
        if (month is < 1 or > 12) return null;
        if (year is >= 1 and <= 99)
            year += 2000;
        if (year < 1900 || year > 2100) return null;
        return new DateTime(year, month, DateTime.DaysInMonth(year, month));
    }

    public static DateTime CombineDateAndTime(DateTime? date, string? timeText)
    {
        if (date is null) return DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(timeText)) return date.Value;

        if (TimeSpan.TryParse(timeText.Trim(), CultureInfo.InvariantCulture, out var ts))
            return date.Value.Date.Add(ts);

        foreach (var fmt in new[] { "HH:mm:ss", "H:mm:ss", "hh:mm tt", "h:mm tt" })
        {
            if (DateTime.TryParseExact(timeText.Trim(), fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return date.Value.Date.Add(parsed.TimeOfDay);
        }

        return date.Value;
    }

    public static string? ExtractGstin(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = GstinRegex.Match(text.ToUpperInvariant());
        return m.Success ? m.Groups[1].Value : null;
    }

    public static string MedWinMedicineNote(int medWinId) => $"MedWinId:{medWinId}";

    /// <summary>
    /// MedWin purchase header: <c>pcredit</c> is the amount still due on credit;
    /// <c>pcheqamt</c> is the amount paid. When both are zero the bill is unpaid
    /// (do not treat <c>pcredit = 0</c> alone as fully paid).
    /// </summary>
    public static decimal ResolveMedWinPurchasePaidAmount(decimal grandTotal, decimal creditDue, decimal chequePaid)
    {
        if (grandTotal <= 0) return 0m;
        if (creditDue > 0)
            return Math.Clamp(grandTotal - creditDue, 0m, grandTotal);
        if (chequePaid > 0)
            return Math.Min(grandTotal, chequePaid);
        return 0m;
    }

    public static int ResolveMedWinPurchasePaymentStatus(decimal grandTotal, decimal paidAmount)
    {
        if (grandTotal <= 0) return 2;
        paidAmount = Math.Clamp(paidAmount, 0m, grandTotal);
        if (paidAmount >= grandTotal) return 2;
        if (paidAmount > 0) return 1;
        return 0;
    }

    public static int? ParseMedWinMedicineId(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return null;
        const string prefix = "MedWinId:";
        var idx = notes.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var tail = notes[(idx + prefix.Length)..];
        var digits = new string(tail.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var id) ? id : null;
    }

    public static string NormalizeName(string? name)
        => (name ?? string.Empty).Trim().Replace(" ", string.Empty).ToUpperInvariant();

    private static readonly string[] MatchSuffixes =
    [
        "CAPSULES", "CAPSULE", "CAPS", "CAP",
        "TABLETS", "TABLET", "TABS", "TAB",
        "LOZENGES", "LOZENGE", "LOZ",
        "SUSPENSION", "SUSP",
        "INJECTION", "INJ",
        "SOLUTION", "SOLN",
        "SYRUP", "SYR", "SYP",
        "POWDER", "POWD", "PDR",
        "OINTMENT", "OINT",
        "GRANULES", "GRANULE", "GRAN",
        "SPRAY",
        "DROPS", "DROP", "DRP",
        "CREAM", "GEL", "LOTION",
        "VIAL", "AMPOULE", "AMPUL",
        "INHALER", "RESPULES", "RESPULE"
    ];

    private static readonly string[] FormulationCodes =
    [
        "FORTE", "PLUS", "DS", "LS", "SR", "CR", "XR", "XL", "ER", "MR", "DT", "IP", "AT", "SP", "OD", "PA", "DX", "AX"
    ];

    private static readonly HashSet<string> DosageFormTokens = new(MatchSuffixes, StringComparer.OrdinalIgnoreCase);

    private static readonly Regex StrengthTokenRegex = new(
        @"^(\d+(?:\.\d+)?)(?:MG|ML|GM|MCG|G)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex TrailingStrengthRegex = new(
        @"(\d+(?:\.\d+)?)(?:MG|ML|GM|MCG|G)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SeparatorRegex = new(@"[\s\(\)\[\],;/\-]+", RegexOptions.Compiled);

    /// <summary>Normalize medicine names for cross-catalog matching (MedWin vs OneMG).</summary>
    public static string NormalizeForMatch(string? name)
    {
        var tokens = ExtractMatchTokens(name);
        if (tokens.Count == 0) return string.Empty;
        tokens.Sort(StringComparer.Ordinal);
        return string.Join("|", tokens);
    }

    private static List<string> ExtractMatchTokens(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return [];

        var text = name.Trim();
        var rawTokens = text.Any(char.IsWhiteSpace) || text.Contains('(') || text.Contains('-')
            ? SeparatorRegex.Split(text)
            : TokenizeCompact(text);

        var tokens = new List<string>();
        foreach (var raw in rawTokens)
        {
            var token = NormalizeToken(raw);
            if (token.Length == 0 || IsDosageFormToken(token)) continue;
            tokens.Add(token);
        }

        return tokens;
    }

    private static string[] TokenizeCompact(string compact)
    {
        var work = compact.ToUpperInvariant();
        var numbers = new List<string>();

        var changed = true;
        while (changed)
        {
            changed = false;

            var afterForm = StripTrailingFormSuffixes(work);
            if (afterForm != work)
            {
                work = afterForm;
                changed = true;
                continue;
            }

            var m = TrailingStrengthRegex.Match(work);
            if (!m.Success) continue;

            numbers.Add(m.Groups[1].Value);
            work = work[..m.Index];
            changed = true;
        }

        var tokens = new List<string>();
        if (work.Length > 0)
            tokens.AddRange(SplitEmbeddedFormulationCodes(work));
        tokens.AddRange(numbers);
        return tokens.ToArray();
    }

    private static IEnumerable<string> SplitEmbeddedFormulationCodes(string text)
    {
        var remaining = text;
        while (remaining.Length > 0)
        {
            var splitAt = -1;
            var code = string.Empty;
            foreach (var form in FormulationCodes.OrderByDescending(c => c.Length))
            {
                var idx = remaining.IndexOf(form, StringComparison.Ordinal);
                if (idx <= 0) continue;
                if (splitAt == -1 || idx < splitAt)
                {
                    splitAt = idx;
                    code = form;
                }
            }

            if (splitAt > 0)
            {
                yield return remaining[..splitAt];
                yield return code;
                remaining = remaining[(splitAt + code.Length)..];
                continue;
            }

            yield return remaining;
            break;
        }
    }

    private static string NormalizeToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return string.Empty;

        var t = token.Trim().ToUpperInvariant();
        t = StripTrailingFormSuffixes(t);

        var strength = StrengthTokenRegex.Match(t);
        if (strength.Success) return strength.Groups[1].Value;

        return t;
    }

    private static string StripTrailingFormSuffixes(string value)
    {
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var suffix in MatchSuffixes)
            {
                if (!value.EndsWith(suffix, StringComparison.Ordinal)) continue;
                value = value[..^suffix.Length];
                changed = true;
                break;
            }
        }
        return value;
    }

    private static bool IsDosageFormToken(string token)
        => DosageFormTokens.Contains(token);
}
