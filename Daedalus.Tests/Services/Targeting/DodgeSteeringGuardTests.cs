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

    [Fact]
    public void SteeringADodge_HoldsTheGapCloser()
    {
        var service = Service(() => true, out var target);
        Assert.True(service.ShouldBlockGapCloser(target.Object, new Mock<IPlayerCharacter>().Object));
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
