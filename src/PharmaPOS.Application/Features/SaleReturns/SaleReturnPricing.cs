using PharmaPOS.Application.Features.Sales;

namespace PharmaPOS.Application.Features.SaleReturns;

/// <summary>Proportional GST/discount reversal for partial sale returns.</summary>
public static class SaleReturnPricing
{
    public static SaleReturnLineAmounts ComputeLineAmounts(
        decimal soldQuantity,
        decimal returnQuantity,
        decimal mrp,
        decimal unitPrice,
        decimal storedDiscountAmount,
        decimal gstPercent,
        decimal storedLineTotal,
        decimal storedTaxable,
        decimal storedTax)
    {
        if (returnQuantity <= 0 || soldQuantity <= 0)
            return new SaleReturnLineAmounts();

        var ratio = returnQuantity / soldQuantity;
        var lineTotal = Math.Round(storedLineTotal * ratio, 2);
        var discount = Math.Round(
            (storedDiscountAmount > 0
                ? storedDiscountAmount
                : SaleLinePricing.DiscountAmount(mrp, unitPrice, soldQuantity)) * ratio, 2);

        var (taxable, tax) = SaleLinePricing.ResolveLineTax(
            lineTotal, gstPercent,
            storedTaxable > 0 ? Math.Round(storedTaxable * ratio, 2) : 0m,
            storedTax > 0 ? Math.Round(storedTax * ratio, 2) : 0m);

        return new SaleReturnLineAmounts
        {
            LineTotal = lineTotal,
            DiscountAmount = discount,
            TaxableAmount = taxable,
            TaxAmount = tax,
            Mrp = mrp,
            UnitPrice = unitPrice,
            GstPercent = gstPercent
        };
    }

    public static (decimal Cgst, decimal Sgst) SplitTax(decimal totalTax)
    {
        var (cgst, sgst) = SaleLinePricing.SplitCgstSgst(totalTax);
        return (cgst, sgst);
    }

    public static int ProportionalPoints(int totalPoints, decimal soldQty, decimal returnQty)
    {
        if (totalPoints <= 0 || soldQty <= 0 || returnQty <= 0) return 0;
        return (int)Math.Round(totalPoints * (returnQty / soldQty), MidpointRounding.AwayFromZero);
    }
}

public sealed class SaleReturnLineAmounts
{
    public decimal Mrp { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal GstPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
}
