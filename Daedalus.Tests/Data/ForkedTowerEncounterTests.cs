using System.Linq;
using Daedalus.Data;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>
/// The Forked Tower raids run INSIDE the Horn territories rather than in a map of their own, so
/// the weakness log already records their enemies with no change. What matters is that they are
/// filed as raids and not as critical encounters — a raid boss counted as a CE inflates the
/// coverage bucket that is already the least meaningful.
/// </summary>
public sealed class ForkedTowerEncounterTests
{
    [Theory]
    [InlineData("The Forked Tower: Blood")]
    [InlineData("The Forked Tower: Magic")]
    [InlineData("The Forked Tower: Magic (Extreme)")]
    public void KnownRaids_AreRecognised(string name)
        => Assert.True(OccultEncounters.IsForkedTower(name));

    /// <summary>A future tier must not silently land in the critical-encounter bucket.</summary>
    [Fact]
    public void UnknownFutureTier_IsStillRecognisedByFamilyName()
        => Assert.True(OccultEncounters.IsForkedTower("The Forked Tower: Something New"));

    [Theory]
    [InlineData("Scourge of the Mind")]
    [InlineData("Many Mouths to Feed")]
    [InlineData("The Dalriada")]
    [InlineData("")]
    [InlineData(null)]
    public void CriticalEncountersAndNonsense_AreNot(string? name)
        => Assert.False(OccultEncounters.IsForkedTower(name));

    /// <summary>
    /// The raids must never appear in either zone's critical-encounter roster — that roster
    /// drives the "never seen" list, and a 48-player raid is not something to chase for coverage.
    /// </summary>
    [Fact]
    public void RaidsAreNotInEitherCriticalEncounterRoster()
    {
        foreach (var raid in OccultEncounters.ForkedTowerEvents)
        {
            Assert.DoesNotContain(raid, OccultEncounters.SouthHornCriticalEncounters);
            Assert.DoesNotContain(raid, OccultEncounters.NorthHornCriticalEncounters);
        }
    }

    /// <summary>
    /// Both rosters stay at fifteen. The raids sit either side of North Horn's block in the
    /// DynamicEvent sheet (rows 48 and 64-65 against 33-47 and 49-63), so an off-by-one when
    /// transcribing would show up here.
    /// </summary>
    [Fact]
    public void RostersRemainFifteenEach()
    {
        Assert.Equal(15, OccultEncounters.SouthHornCriticalEncounters.Count);
        Assert.Equal(15, OccultEncounters.NorthHornCriticalEncounters.Count);
        Assert.Equal(3, OccultEncounters.ForkedTowerEvents.Count);
        Assert.Equal(3, OccultEncounters.ForkedTowerEvents.Distinct().Count());
    }
}
