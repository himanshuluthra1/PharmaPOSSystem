using PharmaPOS.Application.Features.SaleReturns;

namespace PharmaPOS.UnitTests.Features;

public class SaleReturnPricingTests
{
    [Fact]
    public void ComputeLineAmounts_partial_return_reverses_proportionally()
    {
        var amounts = SaleReturnPricing.ComputeLineAmounts(
            soldQuantity: 10,
            returnQuantity: 2,
            mrp: 100,
            unitPrice: 90,
            storedDiscountAmount: 100,
            gstPercent: 12,
            storedLineTotal: 1008,
            storedTaxable: 900,
            storedTax: 108);

        Assert.Equal(201.6m, amounts.LineTotal);
        Assert.Equal(20m, amounts.DiscountAmount);
        Assert.True(amounts.TaxAmount > 0);
        Assert.True(amounts.TaxableAmount > 0);
    }

    [Fact]
    public void ComputeLineAmounts_zero_return_yields_zeros()
    {
        var amounts = SaleReturnPricing.ComputeLineAmounts(5, 0, 50, 45, 0, 5, 225, 214, 11);
        Assert.Equal(0, amounts.LineTotal);
        Assert.Equal(0, amounts.DiscountAmount);
        Assert.Equal(0, amounts.TaxAmount);
    }

    [Fact]
    public void ProportionalPoints_rounds_away_from_zero()
    {
        Assert.Equal(2, SaleReturnPricing.ProportionalPoints(10, 5, 1));
        Assert.Equal(0, SaleReturnPricing.ProportionalPoints(0, 5, 1));
        Assert.Equal(0, SaleReturnPricing.ProportionalPoints(10, 5, 0));
    }

    [Fact]
    public void SplitTax_divides_equally()
    {
        var (cgst, sgst) = SaleReturnPricing.SplitTax(118);
        Assert.Equal(59m, cgst);
        Assert.Equal(59m, sgst);
    }
}
