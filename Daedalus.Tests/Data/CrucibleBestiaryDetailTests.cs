using Daedalus.Data;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>Reading a beast's rank and EXP off the Master's Bestiary detail panel (screenshot 2026-10-25: Cu Sith).</summary>
public class CrucibleBestiaryDetailTests
{
    [Fact]
    public void CuSithIsRank3With21Exp()
        => Assert.Equal((1, 3, (int?)21), CrucibleBestiaryDetail.Parse("NO. 1", "RANK 3", "21/100"));

    [Fact]
    public void TwoDigitNumbersAndRanksRead()
        => Assert.Equal((44, 12, (int?)0), CrucibleBestiaryDetail.Parse("NO. 44", "RANK 12", "0/100"));

    /// <summary>An empty panel (nothing selected yet) is not a beast.</summary>
    [Fact]
    public void AnEmptyPanelIsNothing() => Assert.Null(CrucibleBestiaryDetail.Parse("", "", ""));

    [Theory]
    [InlineData("NO. 51", "RANK 3")]  // past the 50 beasts
    [InlineData("NO. 1", "RANK 26")]  // past the rank cap
    [InlineData("NO. 1", "RANK")]     // no number
    public void ImpossibleValuesAreRejected(string number, string rank)
        => Assert.Null(CrucibleBestiaryDetail.Parse(number, rank, ""));

    /// <summary>No EXP text is unknown EXP, not 0 — a 0 would look like a rank-up.</summary>
    [Fact]
    public void MissingExpIsUnknown() => Assert.Equal((5, 4, (int?)null), CrucibleBestiaryDetail.Parse("NO. 5", "RANK 4", ""));

    /// <summary>Screenshot 2026-10-31: a rank-25 Damselfly on a Sync-10 board reads "RANK 10" with EXP "-/-".</summary>
    [Fact]
    public void ExpDashesAreRank25WhateverTheShownRank()
        => Assert.Equal((44, 25, (int?)null), CrucibleBestiaryDetail.Parse("NO. 44", "RANK 10", "-/-"));

    [Fact]
    public void ASyncedRankNeverLowersTheKnownRank()
        => Assert.Equal((19, (int?)40), CrucibleBestiaryDetail.Merge(19, 34, 10, 40));

    [Fact]
    public void AHigherReadingRaisesTheRank()
        => Assert.Equal((16, (int?)5), CrucibleBestiaryDetail.Merge(15, 80, 16, 5));

    /// <summary>Behemoth, true 15 at 10 EXP, later shown synced at 10 with 50, then 20: it passed 100 and is 16.</summary>
    [Fact]
    public void ExpFallingUnderASyncedRankIsARankUp()
    {
        var (rank, exp) = CrucibleBestiaryDetail.Merge(15, 10, 10, 50);
        Assert.Equal((15, (int?)50), (rank, exp));
        Assert.Equal((16, (int?)20), CrucibleBestiaryDetail.Merge(rank, exp, 10, 20));
    }

    [Fact]
    public void UnknownExpKeepsTheKnownExp()
        => Assert.Equal((15, (int?)50), CrucibleBestiaryDetail.Merge(15, 50, 10, null));

    [Fact]
    public void Rank25DoesNotRankUpPastTheCap()
        => Assert.Equal((25, (int?)0), CrucibleBestiaryDetail.Merge(25, 50, 10, 0));

    [Fact]
    public void AFirstReadingIsTakenAsShown()
        => Assert.Equal((10, (int?)7), CrucibleBestiaryDetail.Merge(null, null, 10, 7));
}
