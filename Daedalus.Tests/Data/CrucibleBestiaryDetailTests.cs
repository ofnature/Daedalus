using Daedalus.Data;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>Reading a beast's true rank off the Master's Bestiary detail panel (screenshot 2026-10-25: Cu Sith).</summary>
public class CrucibleBestiaryDetailTests
{
    [Fact]
    public void CuSithIsRank3With21Exp()
        => Assert.Equal((1, 3, 21), CrucibleBestiaryDetail.Parse("NO. 1", "RANK 3", "21/100"));

    [Fact]
    public void TwoDigitNumbersAndRanksRead()
        => Assert.Equal((44, 12, 0), CrucibleBestiaryDetail.Parse("NO. 44", "RANK 12", "0/100"));

    /// <summary>An empty panel (nothing selected yet) is not a beast.</summary>
    [Fact]
    public void AnEmptyPanelIsNothing() => Assert.Null(CrucibleBestiaryDetail.Parse("", "", ""));

    [Theory]
    [InlineData("NO. 51", "RANK 3")]  // past the 50 beasts
    [InlineData("NO. 1", "RANK 26")]  // past the rank cap
    [InlineData("NO. 1", "RANK")]     // no number
    public void ImpossibleValuesAreRejected(string number, string rank)
        => Assert.Null(CrucibleBestiaryDetail.Parse(number, rank, ""));

    [Fact]
    public void MissingExpReadsAsZero() => Assert.Equal((5, 4, 0), CrucibleBestiaryDetail.Parse("NO. 5", "RANK 4", ""));
}
