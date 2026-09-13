using System.Globalization;
using System.Text.RegularExpressions;

namespace PharmaPOS.MedWinImport;

public static class ImportHelpers
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

    /// <summary>
    /// MedWin quantities are loose units (e.g. tablets). PharmaPOS stock/sale/purchase qty is in packs/strips.
    /// Convert with pack size from <c>stksize</c> / <c>dpsize</c> / <c>sizefact</c> (e.g. 45 ÷ 15 → 3).
    /// </summary>
    public static decimal ToPackQuantity(decimal medWinUnitQty, int packSize)
    {
        var pack = Math.Max(1, packSize);
        if (pack == 1 || medWinUnitQty == 0m) return medWinUnitQty;
        return Math.Round(medWinUnitQty / pack, 4, MidpointRounding.AwayFromZero);
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

    /// <summary>Removes every <c>MedWinId:</c> segment from Notes (keeps OneMG / pack segments).</summary>
    public static string? StripAllMedWinIds(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return null;

        var kept = notes
            .Split(" | ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.StartsWith("MedWinId:", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return kept.Length == 0 ? null : string.Join(" | ", kept);
    }

    /// <summary>
    /// Resolves taxable vs tax from MedWin purchase header fields.
    /// <c>purtaxam</c> is usually the taxable amount (~bill total); occasionally it is the tax amount.
    /// </summary>
    public static (decimal Taxable, decimal Tax) ResolveMedWinPurchaseTax(
        decimal grandTotal, decimal grossAmount, decimal purtaxam)
    {
        if (grandTotal <= 0 && grossAmount <= 0)
            return (0m, 0m);

        var bill = grandTotal > 0 ? grandTotal : grossAmount;

        if (purtaxam <= 0)
        {
            var taxable = grossAmount > 0 ? Math.Min(grossAmount, bill) : bill;
            return (taxable, Math.Max(0m, bill - taxable));
        }

        // Large value relative to bill → taxable; small → tax amount.
        if (purtaxam >= bill * 0.5m)
        {
            var taxable = Math.Min(purtaxam, bill);
            return (taxable, Math.Max(0m, Math.Round(bill - taxable, 2)));
        }

        var tax = Math.Min(purtaxam, bill);
        return (Math.Max(0m, Math.Round(bill - tax, 2)), tax);
    }

    /// <summary>
    /// MedWin purchase header settlement:
    /// <c>pcheqamt</c> = cash/cheque paid; <c>pcredit</c> = debit-note / non-cash settlement
    /// (not remaining due — MedWin keeps the split on the header after bills are cleared).
    /// Unpaid bills have both fields zero; partial cash-only bills have <c>pcredit = 0</c>
    /// and <c>pcheqamt</c> &lt; bill.
    /// </summary>
    public static decimal ResolveMedWinPurchasePaidAmount(decimal grandTotal, decimal debitNoteSettled, decimal chequePaid)
    {
        if (grandTotal <= 0) return 0m;
        var paid = chequePaid + debitNoteSettled;
        if (paid <= 0) return 0m;
        return Math.Min(grandTotal, paid);
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

    /// <summary>
    /// Longest-first dosage form suffixes stripped from names.
    /// Synonyms map to one canonical form so TAB≈TABLET but TAB≠CREAM.
    /// </summary>
    private static readonly (string Suffix, string Canonical)[] FormSuffixMap =
    [
        ("CAPSULES", "CAP"), ("CAPSULE", "CAP"), ("CAPS", "CAP"), ("CAP", "CAP"),
        ("TABLETS", "TAB"), ("TABLET", "TAB"), ("TABS", "TAB"), ("TAB", "TAB"),
        ("LOZENGES", "LOZ"), ("LOZENGE", "LOZ"), ("LOZ", "LOZ"),
        ("SUSPENSION", "SUSP"), ("SUSP", "SUSP"),
        ("INJECTION", "INJ"), ("INJ", "INJ"),
        ("SOLUTION", "SOLN"), ("SOLN", "SOLN"),
        ("SYRUP", "SYR"), ("SYR", "SYR"), ("SYP", "SYR"),
        ("POWDER", "PDR"), ("POWD", "PDR"), ("PDR", "PDR"),
        ("OINTMENT", "OINT"), ("OINT", "OINT"),
        ("GRANULES", "GRAN"), ("GRANULE", "GRAN"), ("GRAN", "GRAN"),
        ("SPRAY", "SPRAY"),
        ("DROPS", "DROP"), ("DROP", "DROP"), ("DRP", "DROP"),
        ("CREAM", "CREAM"), ("GEL", "GEL"), ("LOTION", "LOTION"),
        ("VIAL", "VIAL"), ("AMPOULE", "AMP"), ("AMPUL", "AMP"),
        ("INHALER", "INH"), ("RESPULES", "RESP"), ("RESPULE", "RESP")
    ];

    private static readonly string[] MatchSuffixes =
        FormSuffixMap.Select(x => x.Suffix).ToArray();

    private static readonly Dictionary<string, string> CanonicalFormByToken =
        FormSuffixMap.ToDictionary(x => x.Suffix, x => x.Canonical, StringComparer.OrdinalIgnoreCase);

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

    /// <summary>
    /// Normalize medicine names for cross-catalog matching (MedWin vs OneMG).
    /// Includes canonical dosage form (e.g. #TAB / #CREAM) so forms cannot collide.
    /// </summary>
    public static string NormalizeForMatch(string? name)
    {
        var (tokens, form) = ExtractMatchTokensAndForm(name);
        if (tokens.Count == 0) return string.Empty;
        tokens.Sort(StringComparer.Ordinal);
        var key = string.Join("|", tokens);
        return form is null ? key : $"{key}#{form}";
    }

    /// <summary>Canonical dosage form when present (TAB, CREAM, …); otherwise null.</summary>
    public static string? ExtractCanonicalDosageForm(string? name)
        => ExtractMatchTokensAndForm(name).Form;

    private static (List<string> Tokens, string? Form) ExtractMatchTokensAndForm(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return ([], null);

        var text = name.Trim();
        string? form = null;
        string[] rawTokens;
        if (text.Any(char.IsWhiteSpace) || text.Contains('(') || text.Contains('-'))
        {
            rawTokens = SeparatorRegex.Split(text);
        }
        else
        {
            rawTokens = TokenizeCompact(text, out form);
        }

        var tokens = new List<string>();
        foreach (var raw in rawTokens)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var upper = raw.Trim().ToUpperInvariant();
            if (TryCanonicalForm(upper, out var tokenForm))
            {
                form ??= tokenForm;
                continue;
            }

            var token = NormalizeToken(raw, ref form);
            if (token.Length == 0 || IsDosageFormToken(token)) continue;
            tokens.Add(token);
        }

        return (tokens, form);
    }

    private static string[] TokenizeCompact(string compact, out string? form)
    {
        form = null;
        var work = compact.ToUpperInvariant();
        var numbers = new List<string>();

        var changed = true;
        while (changed)
        {
            changed = false;

            var afterForm = StripTrailingFormSuffixes(work, out var strippedForm);
            if (afterForm != work)
            {
                form ??= strippedForm;
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

    private static string NormalizeToken(string token, ref string? form)
    {
        if (string.IsNullOrWhiteSpace(token)) return string.Empty;

        var t = token.Trim().ToUpperInvariant();
        t = StripTrailingFormSuffixes(t, out var strippedForm);
        form ??= strippedForm;

        var strength = StrengthTokenRegex.Match(t);
        if (strength.Success) return strength.Groups[1].Value;

        return t;
    }

    private static string StripTrailingFormSuffixes(string value, out string? form)
    {
        form = null;
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var (suffix, canonical) in FormSuffixMap)
            {
                if (!value.EndsWith(suffix, StringComparison.Ordinal)) continue;
                form ??= canonical;
                value = value[..^suffix.Length];
                changed = true;
                break;
            }
        }
        return value;
    }

    private static bool TryCanonicalForm(string token, out string canonical)
        => CanonicalFormByToken.TryGetValue(token, out canonical!);

    private static bool IsDosageFormToken(string token)
        => DosageFormTokens.Contains(token);
}
