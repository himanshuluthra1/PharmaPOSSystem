namespace PharmaPOS.Application.Features.Sales;

/// <summary>MRP vs sale-price discount math shared by billing UI and server.</summary>
public static class SaleLinePricing
{
    public static decimal DiscountPercent(decimal mrp, decimal unitPrice)
    {
        if (mrp <= 0) return 0m;
        var pct = (mrp - unitPrice) / mrp * 100m;
        return Math.Round(Math.Clamp(pct, 0m, 100m), 2);
    }

    public static decimal GrossAtMrp(decimal mrp, decimal quantity)
        => Math.Round(mrp * quantity, 2);

    public static decimal DiscountAmount(decimal mrp, decimal unitPrice, decimal quantity)
        => Math.Round(Math.Max(0m, (mrp - unitPrice) * quantity), 2);

    public static decimal LineTotal(decimal unitPrice, decimal quantity)
        => Math.Round(unitPrice * quantity, 2);

    public static decimal UnitPriceFromDiscount(decimal mrp, decimal discountPercent)
        => mrp <= 0 ? 0m : Math.Round(mrp * (1 - Math.Clamp(discountPercent, 0m, 100m) / 100m), 2);

    public static decimal TaxableAmount(decimal lineTotal, decimal gstPercent)
        => gstPercent <= 0 ? lineTotal : Math.Round(lineTotal / (1 + gstPercent / 100m), 2);

    public static decimal TaxAmount(decimal lineTotal, decimal gstPercent, decimal taxableAmount = 0m)
    {
        if (gstPercent <= 0) return 0m;
        var taxable = taxableAmount > 0 ? taxableAmount : TaxableAmount(lineTotal, gstPercent);
        return lineTotal - taxable;
    }

    public static (decimal Cgst, decimal Sgst) SplitCgstSgst(decimal totalTax)
    {
        if (totalTax <= 0) return (0m, 0m);
        var cgst = Math.Round(totalTax / 2m, 2, MidpointRounding.AwayFromZero);
        return (cgst, totalTax - cgst);
    }

    /// <summary>Derives GST-exclusive taxable value and tax from an inclusive line total.</summary>
    public static (decimal Taxable, decimal Tax) ResolveLineTax(
        decimal lineTotal, decimal gstPercent, decimal storedTaxable = 0m, decimal storedTax = 0m)
    {
        if (lineTotal <= 0) return (0m, 0m);

        if (gstPercent > 0)
        {
            var taxable = TaxableAmount(lineTotal, gstPercent);
            return (taxable, lineTotal - taxable);
        }

        if (storedTaxable > 0 && storedTax > 0)
            return (storedTaxable, storedTax);
        if (storedTax > 0)
            return (Math.Max(0, lineTotal - storedTax), storedTax);
        if (storedTaxable > 0)
            return (storedTaxable, Math.Max(0, lineTotal - storedTaxable));

        return (lineTotal, 0m);
    }

    public static (decimal SubTotalMrp, decimal Discount, decimal Taxable, decimal Cgst, decimal Sgst) ComputeReceiptTotals(
        IEnumerable<(decimal Mrp, decimal Quantity, decimal UnitPrice, decimal LineTotal, decimal GstPercent, decimal StoredTaxable, decimal StoredTax, decimal StoredDiscount)> lines)
    {
        decimal subTotalMrp = 0m, discount = 0m, taxable = 0m, tax = 0m;
        foreach (var line in lines)
        {
            subTotalMrp += GrossAtMrp(line.Mrp, line.Quantity);
            discount += line.StoredDiscount > 0
                ? line.StoredDiscount
                : DiscountAmount(line.Mrp, line.UnitPrice, line.Quantity);
            var (lineTaxable, lineTax) = ResolveLineTax(
                line.LineTotal, line.GstPercent, line.StoredTaxable, line.StoredTax);
            taxable += lineTaxable;
            tax += lineTax;
        }

        var (cgst, sgst) = SplitCgstSgst(tax);
        return (subTotalMrp, discount, taxable, cgst, sgst);
    }
}
