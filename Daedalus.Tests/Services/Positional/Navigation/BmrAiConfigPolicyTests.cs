using Daedalus.Data;
using Daedalus.Services.Positional;
using Daedalus.Services.Positional.Navigation;
using Xunit;

namespace Daedalus.Tests.Services.Positional.Navigation;

/// <summary>
/// Tests for the BMR AI auto-manage policy: role → stand distance, and Daedalus's next-GCD positional →
/// BMR's Positional enum name (the dynamic-positional improvement over a single static value).
/// </summary>
public sealed class BmrAiConfigPolicyTests
{
    [Theory]
    [InlineData(JobRegistry.WhiteMage, true)]
    [InlineData(JobRegistry.Sage, true)]
    [InlineData(JobRegistry.Bard, true)]
    [InlineData(JobRegistry.BlackMage, true)]
    [InlineData(JobRegistry.Samurai, false)]
    [InlineData(JobRegistry.Paladin, false)]
    public void IsBacklineJob_ClassifiesRoles(uint jobId, bool expected) =>
        Assert.Equal(expected, BmrAiConfigPolicy.IsBacklineJob(jobId));

    [Fact]
    public void ResolveMaxDistance_Backline_UsesRangedDistance()
    {
        Assert.Equal(15f, BmrAiConfigPolicy.ResolveMaxDistance(JobRegistry.WhiteMage, 15f));
        Assert.Equal(12f, BmrAiConfigPolicy.ResolveMaxDistance(JobRegistry.BlackMage, 12f));
    }

    [Fact]
    public void ResolveMaxDistance_Melee_HugsTheTarget()
    {
        Assert.Equal(BmrAiConfigPolicy.MeleeStandDistance, BmrAiConfigPolicy.ResolveMaxDistance(JobRegistry.Samurai, 15f));
        Assert.Equal(BmrAiConfigPolicy.MeleeStandDistance, BmrAiConfigPolicy.ResolveMaxDistance(JobRegistry.Paladin, 15f));
    }

    [Theory]
    [InlineData(PositionalType.Rear, "Rear")]
    [InlineData(PositionalType.Flank, "Flank")]
    [InlineData(PositionalType.Front, "Front")]
    public void ResolveDesiredPositional_Melee_FollowsNextGcd(PositionalType required, string expected) =>
        Assert.Equal(expected, BmrAiConfigPolicy.ResolveDesiredPositional(JobRegistry.Reaper, required, boundaryCampingActive: false));

    [Fact]
    public void ResolveDesiredPositional_Melee_NoRequirement_IsAny() =>
        Assert.Equal("Any", BmrAiConfigPolicy.ResolveDesiredPositional(JobRegistry.Reaper, null, boundaryCampingActive: false));

    [Fact]
    public void ResolveDesiredPositional_Backline_AlwaysAny()
    {
        // Backline jobs have no positionals — never force one even if a value slips through.
        Assert.Equal("Any", BmrAiConfigPolicy.ResolveDesiredPositional(JobRegistry.WhiteMage, PositionalType.Rear, boundaryCampingActive: false));
        Assert.Equal("Any", BmrAiConfigPolicy.ResolveDesiredPositional(JobRegistry.Bard, null, boundaryCampingActive: false));
    }

    [Theory]
    [InlineData(PositionalType.Rear)]
    [InlineData(PositionalType.Flank)]
    [InlineData(PositionalType.Front)]
    public void ResolveDesiredPositional_MeleeCamping_ReturnsAny(PositionalType required) =>
        // Boundary camping live: Daedalus owns the angle via positional arcs, BMR only keeps range —
        // a pushed positional would have BMR fight us over the standing angle.
        Assert.Equal("Any", BmrAiConfigPolicy.ResolveDesiredPositional(JobRegistry.Ninja, required, boundaryCampingActive: true));

    [Fact]
    public void ResolveDesiredPositional_MeleeNotCamping_KeepsLivePositional() =>
        Assert.Equal("Rear", BmrAiConfigPolicy.ResolveDesiredPositional(JobRegistry.Ninja, PositionalType.Rear, boundaryCampingActive: false));

    [Fact]
    public void ResolveDesiredPositional_BacklineCamping_StillAny() =>
        Assert.Equal("Any", BmrAiConfigPolicy.ResolveDesiredPositional(JobRegistry.WhiteMage, PositionalType.Rear, boundaryCampingActive: true));
}
