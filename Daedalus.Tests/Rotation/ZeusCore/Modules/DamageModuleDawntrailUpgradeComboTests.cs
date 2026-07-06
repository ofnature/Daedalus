using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
using Daedalus.Rotation.ZeusCore.Abilities;
using Daedalus.Rotation.ZeusCore.Modules;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.ZeusCore.Modules;

/// <summary>
/// Lv100 field regression (Origenics 2026-07-05, 46-74% uptime): the whole dungeon looped
/// True Thrust → Disembowel forever. At Lv96 the step-2 buttons upgrade — Disembowel becomes
/// Spiral Blow (36955), Vorpal Thrust becomes Lance Barrage (36954) — and the game reports
/// the EXECUTED id in the combo state. Neither the combo-step map nor the step-3 branch
/// selection knew the upgraded ids, so the combo read step 0 after every second hit and
/// restarted. Same morph-id lesson as steps 4-5 (a202bc1), one step earlier in the chain.
/// </summary>
public class DamageModuleDawntrailUpgradeComboTests
{
    private readonly DamageModule _module = new();

    [Fact]
    public void ComputeComboStep_SpiralBlow_IsStep2()
    {
        Assert.Equal(2, Daedalus.Rotation.Zeus.ComputeComboStep(DRGActions.SpiralBlow.ActionId, 20f));
    }

    [Fact]
    public void ComputeComboStep_LanceBarrage_IsStep2()
    {
        Assert.Equal(2, Daedalus.Rotation.Zeus.ComputeComboStep(DRGActions.LanceBarrage.ActionId, 20f));
    }

    [Fact]
    public void Lv100_AfterSpiralBlow_PushesChaoticSpring_NotStarter()
    {
        // The exact field failure: game reports Spiral Blow, step 3 must continue the combo.
        var (context, scheduler) = Setup(level: 100, lastComboAction: DRGActions.SpiralBlow.ActionId);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == ZeusAbilities.DisembowelFinisher);
    }

    [Fact]
    public void Lv100_AfterLanceBarrage_PushesHeavensThrust()
    {
        var (context, scheduler) = Setup(level: 100, lastComboAction: DRGActions.LanceBarrage.ActionId);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == ZeusAbilities.VorpalFinisher);
    }

    [Fact]
    public void Lv90_BaseIds_StillMapToStep2()
    {
        // Pre-96 behavior unchanged: the base ids keep driving the combo.
        Assert.Equal(2, Daedalus.Rotation.Zeus.ComputeComboStep(DRGActions.Disembowel.ActionId, 20f));
        Assert.Equal(2, Daedalus.Rotation.Zeus.ComputeComboStep(DRGActions.VorpalThrust.ActionId, 20f));
    }

    private static (Daedalus.Rotation.ZeusCore.Context.IZeusContext context,
        Daedalus.Rotation.Common.Scheduling.RotationScheduler scheduler) Setup(
        byte level,
        uint lastComboAction)
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(777UL);
        enemy.Setup(x => x.CurrentHp).Returns(100000u);
        enemy.Setup(x => x.MaxHp).Returns(100000u);
        enemy.Setup(x => x.Position).Returns(new System.Numerics.Vector3(2f, 0f, 0f));
        enemy.Setup(x => x.HitboxRadius).Returns(0.5f);

        var targeting = MockBuilders.CreateMockTargetingService(countEnemiesInRange: 1);
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var actionService = MockBuilders.CreateMockActionService();

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = ZeusTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            level: level,
            lastComboAction: lastComboAction,
            comboTimeRemaining: 20f,
            isAtFlank: true,
            isAtRear: true,
            hasPowerSurge: true,
            powerSurgeRemaining: 25f);

        return (context, scheduler);
    }
}
