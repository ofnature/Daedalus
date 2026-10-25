using Daedalus.Data;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>Reading beast ranks off the team screen, with the values from the 2026-10-24 recording.</summary>
public class CrucibleTeamScreenTests
{
    [Fact]
    public void GoobbueIsRank5()
        => Assert.Equal((30, 5), CrucibleTeamScreen.Member("5", 242030));

    /// <summary>
    /// Field 2026-10-24: the rank text is the paw glyph U+E036 then the number — a plain parse failed for every beast,
    /// so no rank was ever read and the horns fell back to the newest-caught.
    /// </summary>
    [Fact]
    public void ThePawGlyphBeforeTheRankIsSkipped()
        => Assert.Equal((30, 6), CrucibleTeamScreen.Member("6", 242030));

    [Fact]
    public void TwoDigitRanksRead()
        => Assert.Equal((44, 12), CrucibleTeamScreen.Member("12", 242044));

    /// <summary>The second beast's block starts 77 values on: Buffalo's icon sat at 84.</summary>
    [Fact]
    public void BlocksAre77ValuesApart()
    {
        Assert.Equal(6, CrucibleTeamScreen.RankIndex(0));
        Assert.Equal(84, CrucibleTeamScreen.IconIndex(1));
        Assert.Equal(161, CrucibleTeamScreen.IconIndex(2));
    }

    [Theory]
    [InlineData("", 242030)]     // no rank text
    [InlineData("5", 0)]         // no icon
    [InlineData("5", 243138)]    // an item icon, not a beast
    [InlineData("26", 242030)]   // past the rank cap
    public void AnythingElseIsNotABeast(string rank, long icon)
        => Assert.Null(CrucibleTeamScreen.Member(rank, icon));
}
