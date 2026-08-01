using Daedalus.Services.Occult;
using Xunit;

namespace Daedalus.Tests.Services.Occult;

/// <summary>
/// Each Horn runs a northern and a southern pot spot, and the coffer tier looks spot-bound —
/// North Horn's northern spot produced gold, its southern one bronze. So the HUD labels which
/// spot each FATE is, not just its name.
/// </summary>
public sealed class PotFateSpotLabelTests
{
    [Theory]
    [InlineData("Daylight Pottery", "north pots")]
    [InlineData("In a Pot of Bother", "south pots")]
    [InlineData("Persistent Pots", "north pots")]
    [InlineData("Pleading Pots", "south pots")]
    public void DescribeSpot_LabelsEveryKnownPotFate(string fateName, string expected)
    {
        Assert.Equal(expected, PotFateTracker.DescribeSpot(fateName));
    }

    /// <summary>Names arrive from the live FATE table, so casing shouldn't matter.</summary>
    [Fact]
    public void DescribeSpot_IsCaseInsensitive()
    {
        Assert.Equal("north pots", PotFateTracker.DescribeSpot("daylight pottery"));
    }

    [Theory]
    [InlineData("Some Other FATE")]
    [InlineData("")]
    [InlineData("   ")]
    public void DescribeSpot_IsEmptyForAnythingElse(string fateName)
    {
        Assert.Equal(string.Empty, PotFateTracker.DescribeSpot(fateName));
    }

    [Fact]
    public void NameWithSpot_AppendsTheLabel()
    {
        Assert.Equal("Daylight Pottery (north pots)", PotFateTracker.NameWithSpot("Daylight Pottery"));
        Assert.Equal("In a Pot of Bother (south pots)", PotFateTracker.NameWithSpot("In a Pot of Bother"));
    }

    /// <summary>An unknown FATE must render as its bare name, never "Name ()".</summary>
    [Fact]
    public void NameWithSpot_LeavesUnknownNamesUntouched()
    {
        Assert.Equal("Some Other FATE", PotFateTracker.NameWithSpot("Some Other FATE"));
    }

    [Fact]
    public void SpotLabels_CoverEveryTrackedPotFate()
    {
        foreach (var names in PotFateTracker.PotFatesByZone.Values)
        {
            foreach (var name in names)
                Assert.NotEqual(string.Empty, PotFateTracker.DescribeSpot(name));
        }
    }
}
