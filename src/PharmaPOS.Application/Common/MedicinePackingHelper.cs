using System.Text.RegularExpressions;
using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Application.Common;

/// <summary>Resolves packing type labels from medicine names when master data lacks DosageForm.</summary>
public static class MedicinePackingHelper
{
    private static readonly HashSet<string> IgnoredTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "FREE", "SUGAR", "ICE", "SR", "XR", "ER", "MR", "CR", "DS", "LS", "DT", "FORTE", "PLUS",
        "ML", "MG", "GM", "MCG", "NOS", "OF", "AND", "WITH", "FOR", "BOTTLE", "STRIP", "VIAL", "PAIR"
    };

    private static readonly Dictionary<string, string> FormTokenLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TABLETS"] = "Tab",
        ["TABLET"] = "Tab",
        ["TABS"] = "Tab",
        ["TAB"] = "Tab",
        ["CAPSULES"] = "Cap",
        ["CAPSULE"] = "Cap",
        ["CAPS"] = "Cap",
        ["CAP"] = "Cap",
        ["SOFTGEL"] = "Cap",
        ["SOFTGELATIN"] = "Cap",
        ["SYRUP"] = "Syp",
        ["SYR"] = "Syp",
        ["SYP"] = "Syp",
        ["SUSPENSION"] = "Susp",
        ["SUSP"] = "Susp",
        ["INJECTION"] = "Inj",
        ["INJ"] = "Inj",
        ["OINTMENT"] = "Oint",
        ["OINT"] = "Oint",
        ["CREAM"] = "Cream",
        ["GEL"] = "Gel",
        ["LOTION"] = "Lotion",
        ["DROPS"] = "Drops",
        ["DROP"] = "Drops",
        ["DRP"] = "Drops",
        ["POWDER"] = "Powder",
        ["POWD"] = "Powder",
        ["PDR"] = "Powder",
        ["SPRAY"] = "Spray",
        ["INHALER"] = "Inhaler",
        ["RESPULE"] = "Inhaler",
        ["RESPULES"] = "Inhaler",
        ["SUPPOSITORY"] = "Supp",
        ["SUPPOSITORIES"] = "Supp",
        ["LOZENGE"] = "Loz",
        ["LOZENGES"] = "Loz",
        ["LOZ"] = "Loz",
        ["SOLUTION"] = "Soln",
        ["SOLN"] = "Soln"
    };

    private static readonly (string Suffix, string Label)[] CompactSuffixes =
    [
        ("TABLETS", "Tab"), ("TABLET", "Tab"), ("CAPSULES", "Cap"), ("CAPSULE", "Cap"),
        ("CAPS", "Cap"), ("SYRUP", "Syp"), ("SUSPENSION", "Susp"), ("INJECTION", "Inj"),
        ("OINTMENT", "Oint"), ("CREAM", "Cream"), ("POWDER", "Powder"), ("SPRAY", "Spray"),
        ("DROPS", "Drops"), ("DROP", "Drops"), ("LOTION", "Lotion"), ("GEL", "Gel"),
        ("TABS", "Tab"), ("TAB", "Tab"), ("CAP", "Cap"), ("SYP", "Syp"), ("SYR", "Syp"),
        ("SUSP", "Susp"), ("INJ", "Inj"), ("OINT", "Oint"), ("POWD", "Powder"), ("DRP", "Drops"),
        ("LOZ", "Loz"), ("SOLN", "Soln")
    ];

    public static string GetPackingType(string? medicineName, DosageForm dosageForm)
        => GetPackingType(medicineName, notes: null, dosageForm);

    public static string GetPackingType(string? medicineName, string? notes, DosageForm dosageForm)
    {
        var fromName = InferFromText(medicineName);
        if (fromName is not null) return fromName;

        var fromNotes = InferFromText(notes);
        if (fromNotes is not null) return fromNotes;

        if (dosageForm != DosageForm.Tablet) return FormatDosageForm(dosageForm);
        return "-";
    }

    private static string? InferFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var fromTokens = InferFromTokens(text);
        if (fromTokens is not null) return fromTokens;

        return InferFromCompactSuffix(text);
    }

    private static readonly string[] LabelPriority =
    [
        "Syp", "Susp", "Soln", "Tab", "Cap", "Inj", "Drops", "Oint", "Cream", "Gel",
        "Lotion", "Powder", "Spray", "Inhaler", "Supp", "Loz", "Other"
    ];

    private static string? InferFromTokens(string text)
    {
        var tokens = Tokenize(text).ToList();
        string? best = null;
        var bestRank = int.MaxValue;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (IgnoredTokens.Contains(token)) continue;
            if (IsIceCreamToken(tokens, i)) continue;

            var normalized = NormalizeStrengthToken(token);
            if (normalized.Length == 0) continue;
            if (!FormTokenLabels.TryGetValue(normalized, out var label)) continue;

            var rank = Array.IndexOf(LabelPriority, label);
            if (rank < 0) rank = LabelPriority.Length;
            if (rank < bestRank)
            {
                best = label;
                bestRank = rank;
            }
        }

        return best;
    }

    private static bool IsIceCreamToken(IReadOnlyList<string> tokens, int index)
        => index > 0
           && tokens[index].Equals("CREAM", StringComparison.OrdinalIgnoreCase)
           && tokens[index - 1].Equals("ICE", StringComparison.OrdinalIgnoreCase);

    private static string? InferFromCompactSuffix(string text)
    {
        var compact = Regex.Replace(text.Trim().ToUpperInvariant(), @"[^A-Z0-9]", string.Empty);
        foreach (var (suffix, label) in CompactSuffixes)
        {
            if (compact.EndsWith(suffix, StringComparison.Ordinal))
                return label;
        }

        return null;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        return Regex.Split(text.Trim(), @"[\s\(\)\[\],;/\-]+")
            .Select(t => t.Trim().TrimEnd('.'))
            .Where(t => t.Length > 0);
    }

    private static string NormalizeStrengthToken(string token)
    {
        var t = token.ToUpperInvariant();
        var m = Regex.Match(t, @"^(\d+(?:\.\d+)?)(MG|ML|GM|MCG|G)?$", RegexOptions.CultureInvariant);
        return m.Success ? string.Empty : t;
    }

    private static string FormatDosageForm(DosageForm form) => form switch
    {
        DosageForm.Tablet => "Tab",
        DosageForm.Capsule => "Cap",
        DosageForm.Syrup => "Syp",
        DosageForm.Injection => "Inj",
        DosageForm.Ointment => "Oint",
        DosageForm.Cream => "Cream",
        DosageForm.Drops => "Drops",
        DosageForm.Inhaler => "Inhaler",
        DosageForm.Powder => "Powder",
        DosageForm.Gel => "Gel",
        DosageForm.Lotion => "Lotion",
        DosageForm.Suspension => "Susp",
        DosageForm.Suppository => "Supp",
        DosageForm.Spray => "Spray",
        _ => "Other"
    };
}
