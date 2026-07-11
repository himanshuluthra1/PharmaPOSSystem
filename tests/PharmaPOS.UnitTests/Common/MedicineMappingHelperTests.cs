using PharmaPOS.Application.Common;
using PharmaPOS.Application.Features.Masters;

namespace PharmaPOS.UnitTests.Common;

public class MedicineMappingHelperTests
{
    [Theory]
    [InlineData("ATEN 50MG TAB", "ATEN")]
    [InlineData("VASAFE-20MG TAB", "VASAFE")]
    [InlineData("CROCIN 500 MG", "CROCIN")]
    [InlineData("ACIVIR-400 DT TAB", "ACIVIR")]
    [InlineData("5-FU INJ", "5-FU")]
    [InlineData("  combiflam tablet", "COMBIFLAM")]
    public void GetFirstWordPrefix_ReturnsNormalizedFirstWord(string name, string expected)
        => Assert.Equal(expected, MedicineMappingHelper.GetFirstWordPrefix(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetFirstWordPrefix_ReturnsNullWhenMissing(string? name)
        => Assert.Null(MedicineMappingHelper.GetFirstWordPrefix(name));

    [Theory]
    [InlineData("ATEN 50MG TAB", "50MG")]
    [InlineData("VASAFE-20MG TAB", "20MG")]
    [InlineData("CROCIN 500 MG", "500MG")]
    public void ExtractStrengthKey_ReturnsDoseToken(string name, string expected)
        => Assert.Equal(expected, MedicineMappingHelper.ExtractStrengthKey(name));

    [Fact]
    public void PickBestOneMgMatch_ReturnsSingleCandidate()
    {
        var candidates = new[]
        {
            new MedicineMappingListItemDto(1, "ATEN 50MG TAB", "Atenolol", null, "123", false)
        };

        var result = MedicineMappingHelper.PickBestOneMgMatch("ATEN 50MG TAB", "Atenolol", candidates);
        Assert.Equal(1, result?.Id);
    }

    [Fact]
    public void PickBestOneMgMatch_PrefersMatchingStrength()
    {
        var candidates = new[]
        {
            new MedicineMappingListItemDto(1, "ATEN 25MG TAB", "Atenolol", null, "1", false),
            new MedicineMappingListItemDto(2, "ATEN 50MG TAB", "Atenolol", null, "2", false),
            new MedicineMappingListItemDto(3, "ATEN AM 50MG TAB", "Atenolol", null, "3", false),
        };

        var result = MedicineMappingHelper.PickBestOneMgMatch("ATEN 50MG TAB", "Atenolol", candidates);
        Assert.Equal(2, result?.Id);
    }

    [Fact]
    public void PickBestOneMgMatch_ReturnsNullWhenAmbiguous()
    {
        var candidates = new[]
        {
            new MedicineMappingListItemDto(1, "ATEN 50MG TAB", "Atenolol", null, "1", false),
            new MedicineMappingListItemDto(2, "ATEN 50 MG TAB", "Atenolol", null, "2", false),
        };

        Assert.Null(MedicineMappingHelper.PickBestOneMgMatch("ATEN 50MG TAB", "Atenolol", candidates));
    }
}
