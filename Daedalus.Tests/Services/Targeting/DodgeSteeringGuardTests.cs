using System;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Moq;
using Daedalus;
using Daedalus.Rotation.Phantom;
using Daedalus.Services.Targeting;
using Xunit;

namespace Daedalus.Tests.Services.Targeting;

/// <summary>
/// Two ways to be put back into an AOE the boss engine was walking you out of: dash toward the
/// boss, or root yourself in place. Both are answered from the same movement facts, read through
/// BossHandlingRouter so BossMod and Minerva alike can supply them.
/// </summary>
public sealed class DodgeSteeringGuardTests
{
    // ── gap closers ─────────────────────────────────────────────────────────────────────

    private static GapCloserSafetyService Service(
        Func<bool>? steering, out Mock<IBattleNpc> target, bool safeGapCloser = true)
    {
        var config = new Configuration();
        config.Targeting.SafeGapCloser = safeGapCloser;
        config.Targeting.PauseWhenNoTarget = false;

        // The service resolves the user's target as IBattleNpc, so the mock has to be one.
        target = new Mock<IBattleNpc>();
        var targets = new Mock<ITargetManager>();
        targets.SetupGet(t => t.Target).Returns(target.Object);

        return new GapCloserSafetyService(config, targets.Object) { ExternalSteering = steering };
    }

    private static GapCloserSafetyService ServiceOnGround(float secondsSafe, out Mock<IBattleNpc> target)
    {
        var service = Service(() => false, out target);
        service.SecondsSafeHere = () => secondsSafe;
        return service;
    }

    [Fact]
    public void SteeringADodge_HoldsTheGapCloser()
    {
        var service = Service(() => true, out var target);
        Assert.True(service.ShouldBlockGapCloser(target.Object, new Mock<IPlayerCharacter>().Object));
    }

    /// <summary>
    /// Not steering yet is not the same as safe. Accept No Imitators, 2026-09-06: Onslaught fired a tenth
    /// of a second into a cast, rooted the character for its dash, and the dodge could not move the five
    /// yalms it wanted. The engine was not steering when the button went out -- but the ground already knew.
    /// </summary>
    [Fact]
    public void GroundThatExpiresBeforeTheDashEnds_HoldsTheGapCloser()
    {
        var service = ServiceOnGround(0.4f, out var target);
        Assert.True(service.ShouldBlockGapCloser(target.Object, new Mock<IPlayerCharacter>().Object));
        Assert.Contains("roots", service.LastBlockReason);
    }

    [Fact]
    public void GroundThatOutlastsTheDash_LetsItThrough()
    {
        var service = ServiceOnGround(6f, out var target);
        Assert.False(service.ShouldBlockGapCloser(target.Object, new Mock<IPlayerCharacter>().Object));
    }

    /// <summary>
    /// Safe to stand on is not safe to land on. Vigil for the Lost, 2026-09-25: the dodge walked a Samurai out of a
    /// Shockwave and stopped steering as it arrived; on that frame the character was not steering and its own ground
    /// was safe for seconds, and Gyoten dashed it back onto the boss and into the Shockwave.
    /// </summary>
    [Fact]
    public void JustArrivedSomewhereSafe_StillWillNotDashIntoTheBoss()
    {
        var service = ServiceOnGround(6f, out var target);
        service.LandingSafe = (_, _) => false;
        Assert.True(service.ShouldBlockGapCloser(target.Object, new Mock<IPlayerCharacter>().Object));
        Assert.Contains("land", service.LastBlockReason ?? string.Empty);
    }

    [Fact]
    public void AClearLanding_LetsItThrough()
    {
        var service = ServiceOnGround(6f, out var target);
        service.LandingSafe = (_, _) => true;
        Assert.False(service.ShouldBlockGapCloser(target.Object, new Mock<IPlayerCharacter>().Object));
    }

    /// <summary>The landing asked about is the target's, measured from the player: the dash goes to it.</summary>
    [Fact]
    public void TheLandingAskedAboutIsTheTargets()
    {
        var service = ServiceOnGround(6f, out var target);
        var player = new Mock<IPlayerCharacter>();
        player.SetupGet(p => p.Position).Returns(new System.Numerics.Vector3(1f, 0f, 2f));
        target.SetupGet(t => t.Position).Returns(new System.Numerics.Vector3(10f, 0f, 20f));
        (System.Numerics.Vector3 From, System.Numerics.Vector3 To)? asked = null;
        service.LandingSafe = (from, to) => { asked = (from, to); return true; };

        service.ShouldBlockGapCloser(target.Object, player.Object);

        Assert.Equal((new System.Numerics.Vector3(1f, 0f, 2f), new System.Numerics.Vector3(10f, 0f, 20f)), asked);
    }

    /// <summary>With no engine wired there is nothing to ask, and the gate behaves exactly as it did before.</summary>
    [Fact]
    public void NoLandingCheckWired_ChangesNothing()
    {
        var service = ServiceOnGround(6f, out var target);
        Assert.False(service.ShouldBlockGapCloser(target.Object, new Mock<IPlayerCharacter>().Object));
    }

    /// <summary>A hold nobody can explain reads as a broken button.</summary>
    [Fact]
    public void TheHoldSaysWhy()
    {
        var service = Service(() => true, out var target);
        service.ShouldBlockGapCloser(target.Object, new Mock<IPlayerCharacter>().Object);
        Assert.Contains("steering", service.LastBlockReason ?? string.Empty);
    }

    /// <summary>
    /// No engine wired means no opinion. Losing every gap closer because a mechanics plugin is
    /// absent would be a worse failure than the dash it guards against.
    /// </summary>
    [Fact]
    public void NoEngineWired_DoesNotHold()
    {
        var service = Service(null, out var target);
        Assert.False(service.ShouldBlockGapCloser(target.Object, new Mock<IPlayerCharacter>().Object));
    }

    [Fact]
    public void NotSteering_DoesNotHold()
    {
        var service = Service(() => false, out var target);
        Assert.False(service.ShouldBlockGapCloser(target.Object, new Mock<IPlayerCharacter>().Object));
    }

    /// <summary>The user's own master switch still wins over everything below it.</summary>
    [Fact]
    public void MasterToggleOff_NeverHolds()
    {
        var service = Service(() => true, out var target, safeGapCloser: false);
        Assert.False(service.ShouldBlockGapCloser(target.Object, new Mock<IPlayerCharacter>().Object));
    }

    // ── rooting instants ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Occult Jump has no cast bar and is not free: two seconds in the air, unable to move.
    /// </summary>
    [Fact]
    public void OccultJump_RootsForTwoSeconds()
        => Assert.Equal(2.0f, PhantomBandRules.RootSeconds(49077));

    /// <summary>
    /// Everything else is genuinely instant. A non-zero default here would put every phantom
    /// action behind the stand-still gate, which is the opposite of the intent.
    /// </summary>
    [Theory]
    [InlineData(49079u)]   // Lance — the other Dragoon damage action, no root
    [InlineData(41595u)]   // Phantom Kick
    [InlineData(49086u)]   // Occult Missile
    [InlineData(0u)]
    public void EverythingElse_IsNotRooted(uint actionId)
        => Assert.Equal(0f, PhantomBandRules.RootSeconds(actionId));

    /// <summary>
    /// A root is held the same way a cast is: the stand-still window covers the GCD wait plus the
    /// lock, not the lock alone, or the character starts moving again mid-air.
    /// </summary>
    [Fact]
    public void TheStandCoversTheGcdWaitAndTheRoot()
    {
        var root = PhantomBandRules.RootSeconds(49077);
        Assert.Equal(1.4f + root, PhantomBandRules.StillSecondsForCast(1.4f, isGcd: true, castSeconds: root), 3);
    }
}
