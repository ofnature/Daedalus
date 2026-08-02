using Daedalus.Rotation.Common.Modules;
using Xunit;

namespace Daedalus.Tests.Rotation.Common;

/// <summary>
/// The raise finder filters by spell range, so an out-of-reach corpse and an empty party both
/// produced "No target" — and nothing walks a healer toward a body, so in an open zone that
/// state could hold for a whole fight while reading as though nobody needed raising.
/// </summary>
public sealed class RaiseTargetDescriptionTests
{
    private static string Describe(float? nearest, float range)
        => RaiseTargetDescription.Describe(nearest, range);

    [Fact]
    public void NobodyDead_StillReadsAsNoTarget()
    {
        Assert.Equal("No target", Describe(null, 30f));
    }

    [Fact]
    public void CorpseBeyondRange_SaysSoAndGivesTheDistance()
    {
        var description = Describe(240f, 30f);

        Assert.Contains("240y", description);
        Assert.Contains("out of 30y raise range", description);
    }

    /// <summary>
    /// In range but not returned means something else rejected it — a pending Raise, or the
    /// alliance gate. Claiming "out of range" there would send the next reader down the wrong path.
    /// </summary>
    [Fact]
    public void CorpseInRange_DoesNotClaimARangeProblem()
    {
        Assert.Equal("No target", Describe(12f, 30f));
        Assert.Equal("No target", Describe(30f, 30f));
    }
}
