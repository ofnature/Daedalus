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
/// Regression from the Lv68 Inferno log (2026-07-04): every combo capped at 3 hits — Fang and
/// Claw / Wheeling Thrust / Drakesbane NEVER fired at any level 56+. They were gated on the
/// Fang-and-Claw-Bared / Wheel-in-Motion proc statuses, which Dawntrail 7.0 REMOVED (the steps
/// are plain combo continuations now — RSR drives them via ComboIds), and the behaviors carried
/// ProcBuff gates on the same dead statuses. Drakesbane was also mislabeled "Lv92 replacement"
/// when it is the Lv64 step-5 finisher following either positional.
/// </summary>
public class DamageModulePositionalStepTests
{
    private readonly DamageModule _module = new();

    [Fact]
    public void Lv68_AfterFullThrust_PushesFangAndClaw()
    {
        var (context, scheduler) = Setup(level: 68, lastComboAction: DRGActions.FullThrust.ActionId);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == ZeusAbilities.FangAndClaw);
    }

    [Fact]
    public void Lv100_AfterHeavensThrust_PushesFangAndClaw()
    {
        // The game reports the EXECUTED step-3 id — at 86+ that is Heavens' Thrust, not Full Thrust.
        var (context, scheduler) = Setup(level: 100, lastComboAction: DRGActions.HeavensThrust.ActionId);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == ZeusAbilities.FangAndClaw);
    }

    [Fact]
    public void Lv68_AfterChaosThrust_PushesWheelingThrust()
    {
        var (context, scheduler) = Setup(level: 68, lastComboAction: DRGActions.ChaosThrust.ActionId);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == ZeusAbilities.WheelingThrust);
    }

    [Fact]
    public void Lv68_AfterFangAndClaw_PushesDrakesbane()
    {
        var (context, scheduler) = Setup(level: 68, lastComboAction: DRGActions.FangAndClaw.ActionId);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == ZeusAbilities.Drakesbane);
    }

    [Fact]
    public void Lv58_AfterWheelingThrust_NoDrakesbane_StarterRestarts()
    {
        // Below Lv64 the combo is 4 hits — after the positional, restart with the starter.
        var (context, scheduler) = Setup(level: 58, lastComboAction: DRGActions.WheelingThrust.ActionId);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var gcds = scheduler.InspectGcdQueue();
        Assert.DoesNotContain(gcds, c => c.Behavior == ZeusAbilities.Drakesbane);
        Assert.Contains(gcds, c => c.Behavior == ZeusAbilities.TrueThrust);
    }

    [Fact]
    public void Lv68_FangAndClawQuestLocked_StarterStillQueued()
    {
        // Both positionals are HW job-quest locked — level-met but unlearned must not stall.
        var (context, scheduler) = Setup(
            level: 68, lastComboAction: DRGActions.FullThrust.ActionId,
            unlearnedActionId: DRGActions.FangAndClaw.ActionId);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var gcds = scheduler.InspectGcdQueue();
        Assert.DoesNotContain(gcds, c => c.Behavior == ZeusAbilities.FangAndClaw);
        Assert.Contains(gcds, c => c.Behavior == ZeusAbilities.TrueThrust);
    }

    [Fact]
    public void Lv50_AfterFullThrust_NoPositionalStep()
    {
        var (context, scheduler) = Setup(level: 50, lastComboAction: DRGActions.FullThrust.ActionId);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectGcdQueue(),
            c => c.Behavior == ZeusAbilities.FangAndClaw || c.Behavior == ZeusAbilities.WheelingThrust);
    }

    private static (Daedalus.Rotation.ZeusCore.Context.IZeusContext context,
        Daedalus.Rotation.Common.Scheduling.RotationScheduler scheduler) Setup(
        byte level,
        uint lastComboAction,
        uint unlearnedActionId = 0)
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
        if (unlearnedActionId != 0)
            actionService.Setup(x => x.IsActionLearned(unlearnedActionId)).Returns(false);

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
