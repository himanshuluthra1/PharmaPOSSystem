using System.Globalization;
using System.Text.RegularExpressions;
using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Application.Common;

/// <summary>Parses OneMG pack_info text (e.g. "strip of 10 tablets").</summary>
public static class PackInfoParser
{
    private static readonly Regex PackRegex = new(
        @"^(?:strip|bottle|vial|tube|jar|packet|pump|box|cartridge|sachet)\s+of\s+(\d+(?:\.\d+)?)\s*(ml|gm|g|mcg|mg)?\s*(.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public sealed record ParsedPackInfo(int UnitsPerPack, string UnitOfMeasure, DosageForm DosageForm);

    public static bool TryParse(string? packInfo, out ParsedPackInfo result)
    {
        result = new ParsedPackInfo(1, "Nos", DosageForm.Tablet);
        if (string.IsNullOrWhiteSpace(packInfo)) return false;

        var text = packInfo.Trim();
        var match = PackRegex.Match(text);
        if (!match.Success) return false;

        var qty = decimal.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var unitsPerPack = qty >= 1 ? (int)Math.Round(qty, MidpointRounding.AwayFromZero) : 1;
        if (unitsPerPack <= 0) unitsPerPack = 1;

        var unitToken = match.Groups[2].Value;
        var formText = match.Groups[3].Value.Trim();

        var unitOfMeasure = string.IsNullOrWhiteSpace(unitToken)
            ? "Nos"
            : unitToken.ToLowerInvariant() switch
            {
                "ml" => "ml",
                "gm" or "g" => "gm",
                "mg" => "mg",
                "mcg" => "mcg",
                _ => "Nos"
            };

        var packingLabel = MedicinePackingHelper.GetPackingType(formText, text, DosageForm.Tablet);
        var dosageForm = MapDosageForm(packingLabel, formText, text);

        result = new ParsedPackInfo(unitsPerPack, unitOfMeasure, dosageForm);
        return true;
    }

    private static DosageForm MapDosageForm(string packingLabel, string formText, string fullText)
    {
        if (!string.IsNullOrWhiteSpace(formText)
            && formText.Contains("suspension", StringComparison.OrdinalIgnoreCase))
            return DosageForm.Suspension;

        if (!string.IsNullOrWhiteSpace(formText)
            && formText.Contains("dry syrup", StringComparison.OrdinalIgnoreCase))
            return DosageForm.Syrup;

        return packingLabel switch
        {
            "Tab" => DosageForm.Tablet,
            "Cap" => DosageForm.Capsule,
            "Syp" => DosageForm.Syrup,
            "Susp" => DosageForm.Suspension,
            "Inj" => DosageForm.Injection,
            "Oint" => DosageForm.Ointment,
            "Cream" => DosageForm.Cream,
            "Drops" => DosageForm.Drops,
            "Inhaler" => DosageForm.Inhaler,
            "Powder" => DosageForm.Powder,
            "Gel" => DosageForm.Gel,
            "Lotion" => DosageForm.Lotion,
            "Spray" => DosageForm.Spray,
            "Supp" => DosageForm.Suppository,
            "Soln" => DosageForm.Syrup,
            _ => InferFromFullText(fullText)
        };
    }

    private static DosageForm InferFromFullText(string text)
    {
        var label = MedicinePackingHelper.GetPackingType(text, null, DosageForm.Tablet);
        return label switch
        {
            "Cap" => DosageForm.Capsule,
            "Syp" => DosageForm.Syrup,
            "Susp" => DosageForm.Suspension,
            "Inj" => DosageForm.Injection,
            "Oint" => DosageForm.Ointment,
            "Cream" => DosageForm.Cream,
            "Drops" => DosageForm.Drops,
            "Inhaler" => DosageForm.Inhaler,
            "Powder" => DosageForm.Powder,
            "Gel" => DosageForm.Gel,
            "Lotion" => DosageForm.Lotion,
            "Spray" => DosageForm.Spray,
            "Supp" => DosageForm.Suppository,
            "Soln" => DosageForm.Syrup,
            _ => DosageForm.Tablet
        };
    }
}
