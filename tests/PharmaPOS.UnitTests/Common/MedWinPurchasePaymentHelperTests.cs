namespace PharmaPOS.UnitTests.Common;

/// <summary>Tests the MedWin purchase paid-amount rules (mirrors ImportHelpers.ResolveMedWinPurchasePaidAmount).</summary>
public class MedWinPurchasePaymentHelperTests
{
    private static decimal ResolvePaid(decimal grandTotal, decimal debitNoteSettled, decimal chequePaid)
    {
        if (grandTotal <= 0) return 0m;
        var paid = chequePaid + debitNoteSettled;
        if (paid <= 0) return 0m;
        return Math.Min(grandTotal, paid);
    }

    [Theory]
    [InlineData(12799, 0, 0, 0)]             // unpaid — both settlement fields zero
    [InlineData(15135, 0, 15135, 15135)]     // full cash/cheque
    [InlineData(4853, 2558, 2295, 4853)]     // cash + DB/NOTE (D.R. SB-26-17236)
    [InlineData(13887, 3712, 10175, 13887)]  // cash + DB/NOTE
    [InlineData(13759, 0, 683, 683)]         // partial cash only, remainder still due
    public void ResolvePaid_matches_medwin_header_patterns(
        decimal grandTotal, decimal debitNoteSettled, decimal chequePaid, decimal expectedPaid)
    {
        Assert.Equal(expectedPaid, ResolvePaid(grandTotal, debitNoteSettled, chequePaid));
    }
}
