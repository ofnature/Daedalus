using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Moq;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.NyxCore.Abilities;
using Daedalus.Rotation.NyxCore.Context;
using Daedalus.Rotation.NyxCore.Modules;
using Daedalus.Services;
using Daedalus.Services.Action;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.NyxCore.Modules;

/// <summary>
/// The Edge/Flood behaviours used to carry the Lv74 Edge/Flood of Shadow as their base action with no
/// level replacement, so below Lv74 the scheduler's level gate dropped every push: no Darkside, so no
/// Delirium at 68-73, and Dark Arts never spent. They now start from Edge of Darkness (16467, Lv40) /
/// Flood of Darkness (16466, Lv30) and upgrade at 74; at Lv30-39 Flood also covers single target.
/// </summary>
public sealed class NyxLowLevelDarksideTests
{
    private const ulong EnemyId = 99999UL;

    [Fact]
    public void Lv60_DarksideDown_DispatchesEdgeOfDarkness()
    {
        var (scheduler, context, actionService) = Collect(level: 60);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == NyxAbilities.EdgeOfShadow);
        var result = scheduler.DispatchOgcd(context);

        Assert.True(result.Dispatched);
        actionService.Verify(x => x.ExecuteOgcd(
            It.Is<ActionDefinition>(a => a.ActionId == DRKActions.EdgeOfDarkness.ActionId), EnemyId), Times.Once);
    }

    [Fact]
    public void Lv40_Boundary_DispatchesEdgeOfDarkness()
    {
        var (scheduler, context, actionService) = Collect(level: 40);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == NyxAbilities.EdgeOfShadow);
        scheduler.DispatchOgcd(context);

        actionService.Verify(x => x.ExecuteOgcd(
            It.Is<ActionDefinition>(a => a.ActionId == DRKActions.EdgeOfDarkness.ActionId), EnemyId), Times.Once);
    }

    [Fact]
    public void Lv80_DarksideDown_DispatchesEdgeOfShadow()
    {
        var (scheduler, context, actionService) = Collect(level: 80);

        scheduler.DispatchOgcd(context);

        actionService.Verify(x => x.ExecuteOgcd(
            It.Is<ActionDefinition>(a => a.ActionId == DRKActions.EdgeOfShadow.ActionId), EnemyId), Times.Once);
    }

    [Fact]
    public void Lv35_SingleTarget_DispatchesFloodOfDarkness()
    {
        // Edge of Darkness is Lv40; Flood of Darkness (Lv30) is the only Darkside source below it.
        var (scheduler, context, actionService) = Collect(level: 35);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == NyxAbilities.FloodOfShadow);
        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == NyxAbilities.EdgeOfShadow);
        scheduler.DispatchOgcd(context);

        actionService.Verify(x => x.ExecuteOgcd(
            It.Is<ActionDefinition>(a => a.ActionId == DRKActions.FloodOfDarkness.ActionId), EnemyId), Times.Once);
    }

    [Fact]
    public void Lv45_EdgeNotLearned_SingleTarget_UsesFlood()
    {
        var (scheduler, _, _) = Collect(level: 45, edgeLearned: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == NyxAbilities.FloodOfShadow);
        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == NyxAbilities.EdgeOfShadow);
    }

    [Fact]
    public void Lv70_DarksideUp_PushesDelirium()
    {
        // Guard: once Edge keeps Darkside up below 74, Delirium (Lv68) follows.
        var actionService = MockBuilders.CreateMockActionService();
        var context = NyxTestContext.Create(actionService: actionService, level: 70, bloodGauge: 50,
            darksideTimer: 25f, inCombat: true);
        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);

        new BuffModule().CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == NyxAbilities.Delirium);
    }

    private static (RotationScheduler scheduler, NyxContext context, Mock<IActionService> actionService) Collect(
        byte level, bool edgeLearned = true)
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(EnemyId);
        enemy.Setup(x => x.CurrentHp).Returns(10000u);
        enemy.Setup(x => x.MaxHp).Returns(10000u);

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        MockBuilders.SetupEnemyPackCount(targeting, 1);

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionLearned(DRKActions.EdgeOfDarkness.ActionId)).Returns(edgeLearned);
        actionService.Setup(x => x.ExecuteOgcd(It.IsAny<ActionDefinition>(), It.IsAny<ulong>())).Returns(true);

        var config = NyxTestContext.CreateDefaultDarkKnightConfiguration();
        config.Tank.EnableSaltedEarth = false; // keep the self-cast p2 oGCD out of the dispatch order

        var context = NyxTestContext.Create(config: config, actionService: actionService,
            targetingService: targeting, level: level, currentMp: 10000, darksideTimer: 0f, inCombat: true);
        Mock.Get(context.ObjectTable).Setup(x => x.SearchById(EnemyId)).Returns(enemy.Object);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService, config: config);
        new DamageModule().CollectCandidates(context, scheduler, isMoving: false);
        return (scheduler, context, actionService);
    }
}
