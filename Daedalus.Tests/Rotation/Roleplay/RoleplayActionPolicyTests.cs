using System.Numerics;
using Daedalus.Rotation.Roleplay;
using Xunit;

namespace Daedalus.Tests.Rotation.Roleplay;

/// <summary>
/// Pressing the kit of the character a quest battle makes you play, as the boss engine names it (The Will of the Moon,
/// 2026-09-29: as Y'shtola, Hien had to be healed by hand). Each test changes one fact of a request that should be
/// queued.
/// </summary>
public sealed class RoleplayActionPolicyTests
{
    private const uint CureII = 13425;
    private const ulong Hien = 0x4002AA19;

    private static RoleplayRequest Heal() => new(CureII, Hien, 3000f, default, float.NaN);
    private static RoleplayActionFacts HardcastGcd() => new(IsGcd: true, CastTime: 1.5f, Ready: true);

    [Fact]
    public void NamedInCombat_TakesTheFrameOver()
        => Assert.True(RoleplayActionPolicy.TakesOver(enabled: true, inCombat: true, requestCount: 3));

    [Fact]
    public void ToggledOffOutOfCombatOrNothingNamed_LeavesTheJobRotation()
    {
        Assert.False(RoleplayActionPolicy.TakesOver(enabled: false, inCombat: true, requestCount: 3));
        Assert.False(RoleplayActionPolicy.TakesOver(enabled: true, inCombat: false, requestCount: 3));
        Assert.False(RoleplayActionPolicy.TakesOver(enabled: true, inCombat: true, requestCount: 0));
    }

    [Fact]
    public void TheEnginesOrderIsTheKitsPreference()
    {
        Assert.True(RoleplayActionPolicy.SchedulerPriority(0) < RoleplayActionPolicy.SchedulerPriority(1));
        Assert.True(RoleplayActionPolicy.SchedulerPriority(1) < RoleplayActionPolicy.SchedulerPriority(5));
    }

    [Fact]
    public void AHealOnTheAlly_IsQueued()
        => Assert.Null(RoleplayActionPolicy.Reject(Heal(), HardcastGcd(), isMoving: false));

    [Fact]
    public void AHardcastOnTheMove_Waits_ButAnInstantDoesNot()
    {
        Assert.NotNull(RoleplayActionPolicy.Reject(Heal(), HardcastGcd(), isMoving: true));
        Assert.Null(RoleplayActionPolicy.Reject(Heal(), HardcastGcd() with { CastTime = 0f }, isMoving: true));
    }

    [Fact]
    public void AnOgcdOnCooldown_Waits_WhileAGcdIsLeftToTheQueueWindow()
    {
        Assert.NotNull(RoleplayActionPolicy.Reject(Heal(), new RoleplayActionFacts(IsGcd: false, CastTime: 0f, Ready: false), isMoving: false));
        Assert.Null(RoleplayActionPolicy.Reject(Heal(), HardcastGcd() with { Ready = false }, isMoving: false));
    }

    [Fact]
    public void NoTargetAndNoGround_IsNotPressed()
        => Assert.NotNull(RoleplayActionPolicy.Reject(Heal() with { TargetId = 0 }, HardcastGcd(), isMoving: false));

    [Fact]
    public void WhileAKitIsPlayed_TheJobDoesNotMove()
    {
        // The Will of the Moon, 2026-09-30: a ninja's max-melee upkeep walked Y'shtola off her Stone IV casts
        Assert.False(RoleplayActionPolicy.JobMovementAllowed(jobAllows: true, kitActive: true));
    }

    [Fact]
    public void WithoutAKit_TheJobsOwnAnswerStands()
    {
        Assert.True(RoleplayActionPolicy.JobMovementAllowed(jobAllows: true, kitActive: false));
        Assert.False(RoleplayActionPolicy.JobMovementAllowed(jobAllows: false, kitActive: false));
    }

    [Fact]
    public void AKitNeverTurnsMovementOn()
        => Assert.False(RoleplayActionPolicy.JobMovementAllowed(jobAllows: false, kitActive: true));

    [Fact]
    public void AGroundTarget_GoesForAnOgcd_NotYetForAGcd()
    {
        var cannon = Heal() with { TargetId = 0, TargetPos = new Vector3(10f, 0f, 5f) };
        Assert.True(cannon.GroundTargeted);
        Assert.Null(RoleplayActionPolicy.Reject(cannon, new RoleplayActionFacts(IsGcd: false, CastTime: 0f, Ready: true), isMoving: false));
        Assert.NotNull(RoleplayActionPolicy.Reject(cannon, new RoleplayActionFacts(IsGcd: true, CastTime: 0f, Ready: true), isMoving: false));
    }
}
