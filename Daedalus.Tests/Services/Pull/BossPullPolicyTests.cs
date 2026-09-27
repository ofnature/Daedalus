using Daedalus.Services.Pull;
using Xunit;

namespace Daedalus.Tests.Services.Pull;

/// <summary>
/// Pulling the boss nobody else will (The Porta Decumana with Duty Support, 2026-09-27: a Machinist sat idle until his
/// target was set by hand). Each test changes one fact of a situation where the target should be taken.
/// </summary>
public sealed class BossPullPolicyTests
{
    private const ulong Ultima = 0x400015E5;

    private static BossPullSituation Idle() => new(
        Enabled: true,
        PullTargetId: Ultima,
        PullTargetFound: true,
        SelfInCombat: false,
        Mounted: false,
        CurrentTargetId: 0,
        CurrentTargetIsLiveEnemy: false,
        SecondsSinceLastSet: 10);

    [Fact]
    public void NobodyElseWillPull_TargetsTheBoss()
        => Assert.True(BossPullPolicy.ShouldTarget(Idle()));

    [Fact]
    public void ToggledOffOrNothingNamed_LeavesTheTargetAlone()
    {
        Assert.False(BossPullPolicy.ShouldTarget(Idle() with { Enabled = false }));
        Assert.False(BossPullPolicy.ShouldTarget(Idle() with { PullTargetId = 0 }));
        Assert.False(BossPullPolicy.ShouldTarget(Idle() with { PullTargetFound = false }));
    }

    [Fact]
    public void InCombatOrMounted_LeavesTheTargetAlone()
    {
        Assert.False(BossPullPolicy.ShouldTarget(Idle() with { SelfInCombat = true }));
        Assert.False(BossPullPolicy.ShouldTarget(Idle() with { Mounted = true }));
    }

    [Fact]
    public void AlreadyOnTheBoss_DoesNotSetItAgain()
        => Assert.False(BossPullPolicy.ShouldTarget(Idle() with { CurrentTargetId = Ultima, CurrentTargetIsLiveEnemy = true }));

    [Fact]
    public void OnAnotherEnemyOnPurpose_IsNotOverridden()
        => Assert.False(BossPullPolicy.ShouldTarget(Idle() with { CurrentTargetId = 0x40001234, CurrentTargetIsLiveEnemy = true }));

    [Fact]
    public void AFriendlyTarget_IsReplaced()
        => Assert.True(BossPullPolicy.ShouldTarget(Idle() with { CurrentTargetId = 0x40001111, CurrentTargetIsLiveEnemy = false }));

    [Fact]
    public void JustSet_WaitsBeforeSettingAgain()
    {
        Assert.False(BossPullPolicy.ShouldTarget(Idle() with { SecondsSinceLastSet = BossPullPolicy.RetargetSeconds - 0.1 }));
        Assert.True(BossPullPolicy.ShouldTarget(Idle() with { SecondsSinceLastSet = BossPullPolicy.RetargetSeconds }));
    }

    [Fact]
    public void Opens_OnlyOnTheNamedBossWhenEnabled()
    {
        Assert.True(BossPullPolicy.Opens(enabled: true, pullTargetId: Ultima, hardTargetId: Ultima));
        Assert.False(BossPullPolicy.Opens(enabled: false, pullTargetId: Ultima, hardTargetId: Ultima));
        Assert.False(BossPullPolicy.Opens(enabled: true, pullTargetId: 0, hardTargetId: Ultima));
        Assert.False(BossPullPolicy.Opens(enabled: true, pullTargetId: Ultima, hardTargetId: 0x40001234));
    }
}
