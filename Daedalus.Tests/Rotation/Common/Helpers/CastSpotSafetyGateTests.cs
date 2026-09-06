using System;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Moq;
using Daedalus;
using Daedalus.Config;
using Daedalus.Rotation.Common;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Services.Positional.Navigation;
using Daedalus.Timeline;
using Xunit;

namespace Daedalus.Tests.Rotation.Common.Helpers;

/// <summary>
/// "Is it safe to cast from this spot?" — the question the raise path has always asked and every
/// ordinary hard cast never did. Fire IV, Glare, Broil and Verthunder checked the fight timeline
/// and whether the toon was moving, and nothing about the ground under it.
/// <para>
/// The answer comes from whichever mechanics engine is selected, so these tests cover the
/// Minerva path as well as BossMod's — the gate must not care which is driving.
/// </para>
/// </summary>
public sealed class CastSpotSafetyGateTests : IDisposable
{
    private static readonly Vector3 Here = new(10f, 0f, 20f);

    public CastSpotSafetyGateTests() => MechanicCastGate.CastSpotSafety = null;

    public void Dispose() => MechanicCastGate.CastSpotSafety = null;

    private static Mock<IRotationContext> Context(bool enableGate = true, bool enablePredictions = true)
    {
        var config = new Configuration();
        config.Timeline.EnableMechanicAwareCasting = enableGate;
        config.Timeline.EnableTimelinePredictions = enablePredictions;

        var player = new Mock<IPlayerCharacter>();
        player.SetupGet(p => p.Position).Returns(Here);

        var ctx = new Mock<IRotationContext>();
        ctx.SetupGet(c => c.Configuration).Returns(config);
        ctx.SetupGet(c => c.IsMoving).Returns(false);
        ctx.SetupGet(c => c.Player).Returns(player.Object);
        ctx.SetupGet(c => c.TimelineService).Returns((ITimelineService?)null);
        return ctx;
    }

    [Fact]
    public void UnsafeSpot_HoldsTheCast()
    {
        MechanicCastGate.CastSpotSafety = (_, _) => false;
        Assert.True(MechanicCastGate.ShouldBlock(Context().Object, castTime: 3f));
    }

    [Fact]
    public void SafeSpot_CastsOn()
    {
        MechanicCastGate.CastSpotSafety = (_, _) => true;
        Assert.False(MechanicCastGate.ShouldBlock(Context().Object, castTime: 3f));
    }

    /// <summary>An instant has no window to be unsafe over, and must never pay for the query.</summary>
    [Fact]
    public void InstantCast_NeverAsks()
    {
        var asked = false;
        MechanicCastGate.CastSpotSafety = (_, _) => { asked = true; return false; };

        Assert.False(MechanicCastGate.ShouldBlock(Context().Object, castTime: 0f));
        Assert.False(asked);
    }

    /// <summary>The window is the cast plus slack: landing exactly as something hits is not safe.</summary>
    [Fact]
    public void TheWindowCoversTheWholeCast()
    {
        float? asked = null;
        MechanicCastGate.CastSpotSafety = (_, window) => { asked = window; return true; };

        MechanicCastGate.ShouldBlock(Context().Object, castTime: 3f);

        Assert.NotNull(asked);
        Assert.True(asked > 3f, $"asked about {asked}s for a 3s cast — the slack is missing");
    }

    [Fact]
    public void TheSpotAskedAbout_IsWhereTheCasterStands()
    {
        Vector3? asked = null;
        MechanicCastGate.CastSpotSafety = (pos, _) => { asked = pos; return true; };

        MechanicCastGate.ShouldBlock(Context().Object, castTime: 3f);

        Assert.Equal(Here, asked);
    }

    /// <summary>
    /// EnableTimelinePredictions governs our own timeline data and its confidence score. The
    /// engine's answer about this spot is neither, so it must not be switched off by that toggle.
    /// </summary>
    [Fact]
    public void TimelinePredictionsOff_StillRespectsTheEngine()
    {
        MechanicCastGate.CastSpotSafety = (_, _) => false;
        Assert.True(MechanicCastGate.ShouldBlock(Context(enablePredictions: false).Object, castTime: 3f));
    }

    [Fact]
    public void MechanicAwareCastingOff_DoesNotAsk()
    {
        var asked = false;
        MechanicCastGate.CastSpotSafety = (_, _) => { asked = true; return false; };

        Assert.False(MechanicCastGate.ShouldBlock(Context(enableGate: false).Object, castTime: 3f));
        Assert.False(asked);
    }

    /// <summary>
    /// No engine wired reads as safe. A rotation that stops casting because a movement plugin is
    /// missing is worse than one that eats an avoidable puddle.
    /// </summary>
    [Fact]
    public void NoEngine_CastsOn()
    {
        Assert.False(MechanicCastGate.ShouldBlock(Context().Object, castTime: 3f));
    }

    [Fact]
    public void EngineThatThrows_CastsOn()
    {
        MechanicCastGate.CastSpotSafety = (_, _) => throw new InvalidOperationException("IPC gone");
        Assert.False(MechanicCastGate.ShouldBlock(Context().Object, castTime: 3f));
    }

    /// <summary>A hold nobody can explain reads as a broken rotation.</summary>
    [Fact]
    public void TheHoldSaysWhy()
    {
        MechanicCastGate.CastSpotSafety = (_, _) => false;
        Assert.Contains("spot", MechanicCastGate.FormatBlockedState(Context().Object, castTime: 3f));
    }

    // ── the selected engine, not a hardcoded one ─────────────────────────────────────────

    private static Mock<IBossModSafetyService> Engine(PositionSafety verdict)
    {
        var m = new Mock<IBossModSafetyService>();
        m.SetupGet(x => x.IsAvailable).Returns(true);
        m.Setup(x => x.QueryPositionSafety(It.IsAny<Vector3>(), It.IsAny<float>())).Returns(verdict);
        return m;
    }

    /// <summary>
    /// With Minerva selected it is MINERVA's verdict that holds the cast, even while BossMod would
    /// have said the spot was fine. Under Minerva this maps onto MaxCastTime — literally "how long
    /// may I stand here casting" — which is the exact question, not an approximation of it.
    /// </summary>
    [Theory]
    [InlineData(PositionSafety.Unsafe)]
    [InlineData(PositionSafety.Imminent)]
    public void MinervaSelected_MinervaDecides(PositionSafety minervaSays)
    {
        var router = new BossHandlingRouter(
            Engine(PositionSafety.Safe).Object, Engine(minervaSays).Object, () => BossHandling.Minerva);
        MechanicCastGate.CastSpotSafety = (pos, window) =>
            router.QueryPositionSafety(pos, window) == PositionSafety.Safe;

        Assert.True(MechanicCastGate.ShouldBlock(Context().Object, castTime: 3f));
    }

    /// <summary>And the mirror: BossMod selected, BossMod decides.</summary>
    [Fact]
    public void BossModSelected_BossModDecides()
    {
        var router = new BossHandlingRouter(
            Engine(PositionSafety.Unsafe).Object, Engine(PositionSafety.Safe).Object, () => BossHandling.BossMod);
        MechanicCastGate.CastSpotSafety = (pos, window) =>
            router.QueryPositionSafety(pos, window) == PositionSafety.Safe;

        Assert.True(MechanicCastGate.ShouldBlock(Context().Object, castTime: 3f));
    }
}
