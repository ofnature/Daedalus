using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
using Daedalus.Rotation.CalliopeCore.Abilities;
using Daedalus.Rotation.CalliopeCore.Modules;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.CalliopeCore.Modules;

/// <summary>
/// BRD field regression (2026-07-04, Lv49 Aurum Vale): below Iron Jaws (Lv56) DoTs were only
/// reapplied once the status fully dropped, costing 3-8s of DoT downtime every 45s cycle
/// (status-poll latency + GCD queue position). The hardcast IS the refresh sub-56 — it must
/// start while the DoT is about to expire (&lt;= <see cref="DamageModule.HardcastDotRefreshSeconds"/>),
/// while 56+ keeps apply-when-missing only (Iron Jaws owns refresh there).
/// </summary>
public class CalliopeDotRefreshTests
{
    [Fact]
    public void Sub56_DotsExpiring_RefreshesEarly()
    {
        // Both DoTs at 2s remaining — the hardcast refresh must already be queued.
        var (context, scheduler) = Setup(level: 49, dotRemaining: 2f);

        new DamageModule().CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == CalliopeAbilities.Windbite);
    }

    [Fact]
    public void Sub56_DotsHealthy_NoRefreshChurn()
    {
        // Plenty of duration left — recasting now would waste the remaining ticks.
        var (context, scheduler) = Setup(level: 49, dotRemaining: 20f);

        var gcds = CollectGcds(context, scheduler);
        Assert.DoesNotContain(gcds, c =>
            c.Behavior == CalliopeAbilities.Windbite || c.Behavior == CalliopeAbilities.VenomousBite);
    }

    [Fact]
    public void Sub56_DotMissing_StillApplies()
    {
        // The original apply-when-missing behavior is preserved.
        var (context, scheduler) = Setup(level: 49, dotRemaining: 0f, dotsPresent: false);

        new DamageModule().CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == CalliopeAbilities.Windbite);
    }

    [Fact]
    public void Sub56_OnlyCausticExpiring_RefreshesVenomousBiteNotWindbite()
    {
        // Stormbite healthy, Caustic at 2s — only the poison DoT recasts.
        var (context, scheduler) = Setup(level: 49, dotRemaining: 20f, causticRemaining: 2f);

        var gcds = CollectGcds(context, scheduler);
        Assert.Contains(gcds, c => c.Behavior == CalliopeAbilities.VenomousBite);
        Assert.DoesNotContain(gcds, c => c.Behavior == CalliopeAbilities.Windbite);
    }

    [Fact]
    public void IronJawsLevel_DotsExpiring_NoHardcastRefresh()
    {
        // 56+: Iron Jaws owns refresh — the hardcast path must not clip in front of it.
        var (context, scheduler) = Setup(level: 60, dotRemaining: 2f);

        var gcds = CollectGcds(context, scheduler);
        Assert.DoesNotContain(gcds, c =>
            c.Behavior == CalliopeAbilities.Windbite || c.Behavior == CalliopeAbilities.Stormbite
            || c.Behavior == CalliopeAbilities.VenomousBite || c.Behavior == CalliopeAbilities.CausticBite);
    }

    // ── Iron Jaws priority (2026-07-20 RSR-parity audit) ──

    [Fact]
    public void IronJaws_TopGcdPriority_WhenRefreshNeeded()
    {
        // RSR: Iron Jaws is the FIRST GCD priority. At its old priority 6 the Hawk's Eye proc
        // parade (1-4) plus Apex (5) could starve a ≤3s refresh past expiry mid-burst.
        var (context, scheduler) = Setup(level: 100, dotRemaining: 2f);

        new DamageModule().CollectCandidates(context, scheduler, isMoving: false);

        var ironJaws = Assert.Single(
            scheduler.InspectGcdQueue(), c => c.Behavior == CalliopeAbilities.IronJaws);
        Assert.Equal(0, ironJaws.Priority);
    }

    [Fact]
    public void IronJaws_NotQueued_WhenDotsHealthy()
    {
        var (context, scheduler) = Setup(level: 100, dotRemaining: 25f);

        new DamageModule().CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == CalliopeAbilities.IronJaws);
    }

    // ── Setup ──

    private static System.Collections.Generic.IReadOnlyList<
        Daedalus.Rotation.Common.Scheduling.AbilityCandidate> CollectGcds(
        Daedalus.Rotation.CalliopeCore.Context.ICalliopeContext context,
        Daedalus.Rotation.Common.Scheduling.RotationScheduler scheduler)
    {
        new DamageModule().CollectCandidates(context, scheduler, isMoving: false);
        return scheduler.InspectGcdQueue();
    }

    private static (Daedalus.Rotation.CalliopeCore.Context.ICalliopeContext context,
        Daedalus.Rotation.Common.Scheduling.RotationScheduler scheduler) Setup(
        byte level, float dotRemaining, float? causticRemaining = null, bool dotsPresent = true)
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(777UL);
        enemy.Setup(x => x.CurrentHp).Returns(100000u);
        enemy.Setup(x => x.MaxHp).Returns(100000u);

        var targeting = MockBuilders.CreateMockTargetingService(
            findEnemy: (_, _, _) => enemy.Object,
            countEnemiesInRange: 1);

        var actionService = MockBuilders.CreateMockActionService();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = CalliopeTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            level: level,
            hasCausticBite: dotsPresent, causticBiteRemaining: causticRemaining ?? dotRemaining,
            hasStormbite: dotsPresent, stormbiteRemaining: dotRemaining);

        return (context, scheduler);
    }
}
