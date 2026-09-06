namespace PharmaPOS.Application.Common;

public static class StockLocation
{
    public static string? Format(string? rack, string? bin)
    {
        rack = string.IsNullOrWhiteSpace(rack) ? null : rack.Trim();
        bin = string.IsNullOrWhiteSpace(bin) ? null : bin.Trim();
        if (rack is null && bin is null) return null;
        if (bin is null) return rack;
        if (rack is null) return bin;
        return $"{rack} / {bin}";
    }
}
