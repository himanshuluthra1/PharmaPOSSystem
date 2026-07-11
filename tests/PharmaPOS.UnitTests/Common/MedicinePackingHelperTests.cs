using PharmaPOS.Application.Common;
using PharmaPOS.Domain.Enums;

namespace PharmaPOS.UnitTests.Common;

public class MedicinePackingHelperTests
{
    [Theory]
    [InlineData("August Dry Syrup", "Syp")]
    [InlineData("Dolo 650 Tablet", "Tab")]
    [InlineData("DOLO 650MG TAB", "Tab")]
    [InlineData("Ampoxin 500 Capsule", "Cap")]
    [InlineData("ACIGON SYP 170ML", "Syp")]
    [InlineData("AMPOXIN 500 CAPS", "Cap")]
    [InlineData("Arcinet 60mg Injection", "Inj")]
    [InlineData("Amaize Cream", "Cream")]
    [InlineData("Alkavil Syrup Ice Cream Sugar Free", "Syp")]
    public void GetPackingType_InfersFromName(string name, string expected)
        => Assert.Equal(expected, MedicinePackingHelper.GetPackingType(name, DosageForm.Tablet));

    [Fact]
    public void GetPackingType_UsesNotesWhenNameHasNoForm()
        => Assert.Equal("Syp", MedicinePackingHelper.GetPackingType("Combiflam", "bottle of 60 ml Syrup", DosageForm.Tablet));
}
