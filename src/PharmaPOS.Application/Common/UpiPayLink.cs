using System.Globalization;

namespace PharmaPOS.Application.Common;

public static class UpiPayLink
{
    /// <summary>NPCI upi://pay URI for a static VPA. Amount is included so the customer pays this bill.</summary>
    public static string? TryCreate(string? vpa, string payeeName, decimal amount, string? invoiceNumber)
    {
        vpa = vpa?.Trim();
        if (string.IsNullOrWhiteSpace(vpa) || !vpa.Contains('@'))
            return null;

        var pn = Uri.EscapeDataString(string.IsNullOrWhiteSpace(payeeName) ? "Pharmacy" : payeeName.Trim());
        var tn = Uri.EscapeDataString(string.IsNullOrWhiteSpace(invoiceNumber) ? "Bill" : "Bill " + invoiceNumber.Trim());
        var am = Math.Max(0, amount).ToString("0.00", CultureInfo.InvariantCulture);
        return $"upi://pay?pa={vpa}&pn={pn}&am={am}&cu=INR&tn={tn}";
    }
}
