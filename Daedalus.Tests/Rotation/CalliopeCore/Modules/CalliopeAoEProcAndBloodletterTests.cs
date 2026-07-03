using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
using Daedalus.Rotation.CalliopeCore.Abilities;
using Daedalus.Rotation.CalliopeCore.Modules;
using Daedalus.Services.Action;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.CalliopeCore.Modules;

/// <summary>
/// BRD review regressions (2026-07-03):
/// 1. Wide Volley (Lv25, the pre-72 AoE Hawk's Eye spender) was never referenced — packs at
///    25-71 dumped procs into the single-target shot.
/// 2. Bloodletter's overcap dump was hardcoded "charges >= 3", but the cap is 2 below the
///    Lv84 trait — charges sat capped outside Mage's Ballad / Raging Strikes for the whole
///    leveling range.
/// 3. The AoE filler push early-returned without queueing the single-target filler fallback.
/// </summary>
public class CalliopeAoEProcAndBloodletterTests
{
    // ── AoE Hawk's Eye spender chain ──

    [Fact]
    public void Level50_Pack_WithProc_UsesWideVolley()
    {
        var (context, scheduler) = DamageSetup(level: 50, enemies: 4, hasHawksEye: true);

        new DamageModule().CollectCandidates(context, scheduler, isMoving: false);

        var gcds = scheduler.InspectGcdQueue();
        Assert.Contains(gcds, c => c.Behavior == CalliopeAbilities.WideVolley);
        Assert.DoesNotContain(gcds, c => c.Behavior == CalliopeAbilities.Shadowbite);
    }

    [Fact]
    public void Level80_Pack_WithProc_UsesShadowbite()
    {
        var (context, scheduler) = DamageSetup(level: 80, enemies: 4, hasHawksEye: true);

        new DamageModule().CollectCandidates(context, scheduler, isMoving: false);

        var gcds = scheduler.InspectGcdQueue();
        Assert.Contains(gcds, c => c.Behavior == CalliopeAbilities.Shadowbite);
        Assert.DoesNotContain(gcds, c => c.Behavior == CalliopeAbilities.WideVolley);
    }

    [Fact]
    public void Level20_Pack_WithProc_FallsBackToSingleTargetShot()
    {
        // Below Wide Volley (25) there is no AoE spender — the proc goes to Straight Shot.
        var (context, scheduler) = DamageSetup(level: 20, enemies: 4, hasHawksEye: true);

        new DamageModule().CollectCandidates(context, scheduler, isMoving: false);

        var gcds = scheduler.InspectGcdQueue();
        Assert.Contains(gcds, c => c.Behavior == CalliopeAbilities.StraightShot);
        Assert.DoesNotContain(gcds, c => c.Behavior == CalliopeAbilities.WideVolley);
    }

    [Fact]
    public void AoePack_QueuesSingleTargetFillerAsFallback()
    {
        // The AoE filler must never be the ONLY queued filler — a dispatch rejection
        // would stall the GCD (the combo-starter-fallback rule).
        var (context, scheduler) = DamageSetup(level: 50, enemies: 4, hasHawksEye: false);

        new DamageModule().CollectCandidates(context, scheduler, isMoving: false);

        var gcds = scheduler.InspectGcdQueue();
        Assert.Contains(gcds, c => c.Behavior == CalliopeAbilities.QuickNock);
        Assert.Contains(gcds, c => c.Behavior == CalliopeAbilities.HeavyShot);
    }

    // ── Bloodletter overcap at the REAL charge cap ──

    [Fact]
    public void Bloodletter_TwoChargeCap_DumpsAtTwo()
    {
        // Lv80: trait cap is 2 — sitting at 2/2 outside MB/RS must dump.
        var (context, scheduler) = BuffSetup(level: 80, charges: 2, maxCharges: 2);

        new BuffModule().CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == CalliopeAbilities.Bloodletter);
    }

    [Fact]
    public void Bloodletter_BelowCap_NoMbNoRs_Held()
    {
        var (context, scheduler) = BuffSetup(level: 90, charges: 2, maxCharges: 3);

        new BuffModule().CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(),
            c => c.Behavior == CalliopeAbilities.Bloodletter || c.Behavior == CalliopeAbilities.HeartbreakShot);
    }

    [Fact]
    public void Bloodletter_UnknownCap_FallsBackToThree()
    {
        // GetMaxCharges returning 0 (unknown) must not dump constantly — falls back to 3.
        var (context, scheduler) = BuffSetup(level: 90, charges: 2, maxCharges: 0);

        new BuffModule().CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(),
            c => c.Behavior == CalliopeAbilities.Bloodletter || c.Behavior == CalliopeAbilities.HeartbreakShot);
    }

    // ── Setup ──

    private static Mock<IBattleNpc> CreateEnemy()
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(777UL);
        enemy.Setup(x => x.CurrentHp).Returns(100000u);
        enemy.Setup(x => x.MaxHp).Returns(100000u);
        return enemy;
    }

    private static (Daedalus.Rotation.CalliopeCore.Context.ICalliopeContext context,
        Daedalus.Rotation.Common.Scheduling.RotationScheduler scheduler) DamageSetup(
        byte level, int enemies, bool hasHawksEye)
    {
        var enemy = CreateEnemy();
        var targeting = MockBuilders.CreateMockTargetingService(
            findEnemy: (_, _, _) => enemy.Object,
            countEnemiesInRange: enemies);

        var actionService = MockBuilders.CreateMockActionService();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = CalliopeTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            level: level,
            hasHawksEye: hasHawksEye,
            hasCausticBite: true, causticBiteRemaining: 20f,
            hasStormbite: true, stormbiteRemaining: 20f);

        return (context, scheduler);
    }

    private static (Daedalus.Rotation.CalliopeCore.Context.ICalliopeContext context,
        Daedalus.Rotation.Common.Scheduling.RotationScheduler scheduler) BuffSetup(
        byte level, int charges, ushort maxCharges)
    {
        var enemy = CreateEnemy();
        var targeting = MockBuilders.CreateMockTargetingService(
            findEnemy: (_, _, _) => enemy.Object,
            countEnemiesInRange: 1);

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.GetMaxCharges(It.IsAny<uint>(), It.IsAny<uint>())).Returns(maxCharges);
        // Only the Bloodletter path is under test — keep every other oGCD quiet.
        actionService.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(false);
        actionService.Setup(x => x.IsActionReady(BRDActions.Bloodletter.ActionId)).Returns(true);
        actionService.Setup(x => x.IsActionReady(BRDActions.HeartbreakShot.ActionId)).Returns(true);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = CalliopeTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            level: level,
            bloodletterCharges: charges);

        return (context, scheduler);
    }
}
