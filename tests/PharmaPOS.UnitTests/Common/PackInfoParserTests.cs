using PharmaPOS.Application.Common;
using PharmaPOS.Domain.Enums;

namespace PharmaPOS.UnitTests.Common;

public class PackInfoParserTests
{
    [Theory]
    [InlineData("strip of 10 tablets", 10, "Nos", DosageForm.Tablet)]
    [InlineData("strip of 10 capsules", 10, "Nos", DosageForm.Capsule)]
    [InlineData("vial of 1 Injection", 1, "Nos", DosageForm.Injection)]
    [InlineData("bottle of 60 ml Syrup", 60, "ml", DosageForm.Syrup)]
    [InlineData("bottle of 15 ml Oral Suspension", 15, "ml", DosageForm.Suspension)]
    [InlineData("bottle of 30 ml Dry Syrup", 30, "ml", DosageForm.Syrup)]
    [InlineData("strip of 10 tablet sr", 10, "Nos", DosageForm.Tablet)]
    public void TryParse_PackInfo(string packInfo, int units, string uom, DosageForm form)
    {
        Assert.True(PackInfoParser.TryParse(packInfo, out var parsed));
        Assert.Equal(units, parsed.UnitsPerPack);
        Assert.Equal(uom, parsed.UnitOfMeasure);
        Assert.Equal(form, parsed.DosageForm);
    }
}
