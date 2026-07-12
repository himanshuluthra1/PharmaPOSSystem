namespace PharmaPOS.UnitTests.Common;

/// <summary>Tests the MedWin purchase paid-amount rules (mirrors ImportHelpers.ResolveMedWinPurchasePaidAmount).</summary>
public class MedWinPurchasePaymentHelperTests
{
    private static decimal ResolvePaid(decimal grandTotal, decimal creditDue, decimal chequePaid)
    {
        if (grandTotal <= 0) return 0m;
        if (creditDue > 0) return Math.Clamp(grandTotal - creditDue, 0m, grandTotal);
        if (chequePaid > 0) return Math.Min(grandTotal, chequePaid);
        return 0m;
    }

    [Theory]
    [InlineData(12799, 0, 0, 0)]       // bill 2223 — unpaid
    [InlineData(15135, 0, 15135, 15135)] // full cheque payment
    [InlineData(13261, 4946, 8315, 8315)] // partial credit
    [InlineData(895, 582, 313, 313)]
    public void ResolvePaid_matches_medwin_header_patterns(
        decimal grandTotal, decimal creditDue, decimal chequePaid, decimal expectedPaid)
    {
        Assert.Equal(expectedPaid, ResolvePaid(grandTotal, creditDue, chequePaid));
        Assert.Equal(grandTotal - expectedPaid, grandTotal - ResolvePaid(grandTotal, creditDue, chequePaid));
    }
}
