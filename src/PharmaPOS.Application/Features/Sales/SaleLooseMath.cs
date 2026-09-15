using System.Globalization;
using System.Text.RegularExpressions;
using PharmaPOS.Application.Common;

namespace PharmaPOS.Application.Features.Sales;

/// <summary>
/// Pack vs loose (tablet) conversion for sales. MRP/Sale are pack rates;
/// loose units are charged and stocked at rate / units-per-pack.
/// </summary>
public static class SaleLooseMath
{
    // MedWin-style labels often use backtick: 10`S / 10'S / 10s / x10
    private static readonly Regex SimplePackRegex = new(
        @"^(?:x\s*)?(\d{1,4})\s*(?:[`'′]?\s*s)?(?:\s*(?:tab|tabs|tablet|tablets|cap|caps|capsule|capsules))?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static int ResolveUnitsPerPack(int unitsPerPack, string? packInfo = null, string? packLabel = null)
    {
        if (unitsPerPack > 1) return unitsPerPack;

        if (PackInfoParser.TryParse(packInfo, out var parsed) && parsed.UnitsPerPack > 1)
            return parsed.UnitsPerPack;

        if (TryParseSimplePack(packInfo, out var fromInfo) && fromInfo > 1)
            return fromInfo;

        if (TryParseSimplePack(packLabel, out var fromLabel) && fromLabel > 1)
            return fromLabel;

        return unitsPerPack > 0 ? unitsPerPack : 1;
    }

    public static bool TryParseSimplePack(string? text, out int unitsPerPack)
    {
        unitsPerPack = 1;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var m = SimplePackRegex.Match(text.Trim());
        if (!m.Success) return false;
        if (!int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n <= 0)
            return false;
        unitsPerPack = n;
        return true;
    }

    /// <summary>Pack-equivalent qty for stock + pricing (packs + loose/packSize).</summary>
    public static decimal ToStockQuantity(decimal packQuantity, decimal looseQuantity, int unitsPerPack)
    {
        var packs = Math.Max(0m, packQuantity);
        var loose = Math.Max(0m, looseQuantity);
        var upp = Math.Max(1, unitsPerPack);
        if (loose <= 0m) return Math.Round(packs, 4);
        // Loose is always a fraction of a pack — never a full strip.
        return Math.Round(packs + loose / upp, 4);
    }

    public static decimal LooseUnitRate(decimal packRate, int unitsPerPack)
    {
        var upp = Math.Max(1, unitsPerPack);
        return Math.Round(packRate / upp, 4);
    }

    public static (decimal Gross, decimal DiscountAmount, decimal LineTotal) ComputeAmounts(
        decimal packMrp,
        decimal packUnitPrice,
        decimal packQuantity,
        decimal looseQuantity,
        int unitsPerPack)
    {
        var upp = Math.Max(1, unitsPerPack);
        var packs = Math.Max(0m, packQuantity);
        var loose = Math.Max(0m, looseQuantity);

        var gross = Math.Round(packMrp * packs + LooseUnitRate(packMrp, upp) * loose, 2);
        var lineTotal = Math.Round(packUnitPrice * packs + LooseUnitRate(packUnitPrice, upp) * loose, 2);
        var discount = Math.Round(Math.Max(0m, gross - lineTotal), 2);
        return (gross, discount, lineTotal);
    }
}
