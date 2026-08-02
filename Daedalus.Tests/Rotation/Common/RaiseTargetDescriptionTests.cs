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
    private static string Describe(float? nearest, float range, bool blocked = false)
        => RaiseTargetDescription.Describe(nearest, range, blocked);

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

    /// <summary>
    /// Occult content can forbid ordinary raises (statuses 4262/4263). A healer stood over the
    /// corpse with everything ready then does nothing, and every other explanation — range,
    /// no target — is a distraction, so the block is reported ahead of them.
    /// </summary>
    [Fact]
    public void ResurrectionBlocked_OutranksEveryOtherExplanation()
    {
        var adjacent = Describe(2f, 30f, blocked: true);
        var distant = Describe(500f, 30f, blocked: true);
        var nobody = Describe(null, 30f, blocked: true);

        Assert.Contains("Resurrection blocked", adjacent);
        Assert.Equal(adjacent, distant);
        Assert.Equal(adjacent, nobody);
    }

    [Fact]
    public void NotBlocked_KeepsTheOrdinaryExplanations()
    {
        Assert.Equal("No target", Describe(null, 30f, blocked: false));
        Assert.Contains("out of 30y raise range", Describe(200f, 30f, blocked: false));
    }
}
