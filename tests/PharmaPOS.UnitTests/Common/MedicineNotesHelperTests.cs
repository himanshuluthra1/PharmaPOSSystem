using PharmaPOS.Application.Common;

namespace PharmaPOS.UnitTests.Common;

public class MedicineNotesHelperTests
{
    [Theory]
    [InlineData("strip of 10 tablets | OneMG-ID:12345 | https://1mg.com/x", "strip of 10 tablets")]
    [InlineData("bottle of 60 ml Syrup | OneMG-ID:99", "bottle of 60 ml Syrup")]
    [InlineData("vial of 2 ml Injection | OneMG-ID:1 | https://x | MedWinId:42", "vial of 2 ml Injection")]
    public void ExtractPackInfo_ReturnsFirstSegment(string notes, string expected)
        => Assert.Equal(expected, MedicineNotesHelper.ExtractPackInfo(notes));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("MedWinId:42")]
    [InlineData("OneMG-ID:12345")]
    public void ExtractPackInfo_ReturnsNullWhenMissing(string? notes)
        => Assert.Null(MedicineNotesHelper.ExtractPackInfo(notes));

    [Theory]
    [InlineData("strip of 10 tablets | OneMG-ID:12345", "12345")]
    [InlineData("OneMG-ID:99 | MedWinId:42", "99")]
    public void ParseOneMgId_ReturnsId(string notes, string expected)
        => Assert.Equal(expected, MedicineNotesHelper.ParseOneMgId(notes));

    [Theory]
    [InlineData("MedWinId:42", 42)]
    [InlineData("strip of 10 tablets | OneMG-ID:1 | MedWinId:9982", 9982)]
    public void ParseMedWinId_ReturnsId(string notes, int expected)
        => Assert.Equal(expected, MedicineNotesHelper.ParseMedWinId(notes));

    [Theory]
    [InlineData("MedWinId:42", true)]
    [InlineData("strip of 10 tablets | OneMG-ID:1", false)]
    public void IsMedWinOnlyOrphan_DetectsOrphans(string notes, bool expected)
        => Assert.Equal(expected, MedicineNotesHelper.IsMedWinOnlyOrphan(notes));

    [Fact]
    public void AppendMedWinNote_AppendsWithoutDuplicating()
    {
        var notes = "strip of 10 tablets | OneMG-ID:1";
        var linked = MedicineNotesHelper.AppendMedWinNote(notes, 42);
        Assert.Equal("strip of 10 tablets | OneMG-ID:1 | MedWinId:42", linked);
        Assert.Equal(linked, MedicineNotesHelper.AppendMedWinNote(linked, 42));
    }

    [Theory]
    [InlineData("MedWinId:13", 13, true)]
    [InlineData("strip of 10 tablets | OneMG-ID:1 | MedWinId:13", 13, true)]
    [InlineData("strip of 10 tablets | OneMG-ID:1 | MedWinId:1380 | MedWinId:1382", 13, false)]
    [InlineData("strip of 10 tablets | OneMG-ID:1 | MedWinId:1380", 13, false)]
    [InlineData("strip of 10 tablets | OneMG-ID:1 | MedWinId:131", 13, false)]
    public void NotesContainsMedWinId_MatchesExactIdOnly(string notes, int medWinId, bool expected)
        => Assert.Equal(expected, MedicineNotesHelper.NotesContainsMedWinId(notes, medWinId));

    [Fact]
    public void ParseAllMedWinIds_ReturnsEveryLinkedId()
    {
        var ids = MedicineNotesHelper.ParseAllMedWinIds(
            "pack | OneMG-ID:1 | MedWinId:1380 | MedWinId:1382").ToList();
        Assert.Equal([1380, 1382], ids);
    }

    [Theory]
    [InlineData("MedWinId:42", true, 99)]
    [InlineData("MedWinId:42", false, 42)]
    [InlineData("strip of 10 tablets | OneMG-ID:1 | MedWinId:42", false, 42)]
    public void IsPendingMedWinMap_ExcludesAlreadyLinkedIds(string notes, bool expected, int linkedId)
    {
        var linked = new HashSet<int> { linkedId };
        Assert.Equal(expected, MedicineNotesHelper.IsPendingMedWinMap(notes, linked));
    }
}
