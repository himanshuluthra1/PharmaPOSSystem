namespace PharmaPOS.Application.Common;

/// <summary>Creates unique pharmacy barcode values (Code128-friendly).</summary>
public static class BarcodeValueGenerator
{
    /// <summary>
    /// Generates a unique internal barcode: PP + yyMMddHHmmss + 3 random digits.
    /// Compatible with Code128 printing and USB wedge scanners.
    /// </summary>
    public static string CreateUnique()
        => $"PP{DateTime.UtcNow:yyMMddHHmmss}{Random.Shared.Next(0, 1000):D3}";
}
