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
/// Regression tests from the Porta Decumana DRG log (2026-07-03, 16:22:42): Life Surge was
/// weaved after Vorpal Thrust while the boss was out of melee reach, so the guaranteed crit
/// landed on Piercing Talon — the weakest GCD in the kit — instead of Full Thrust. Life Surge
/// must hold until the target is back in reach (RSR gates the same block on HasHostilesInRange).
/// </summary>
public class BuffModuleLifeSurgeTests
{
    private readonly BuffModule _module = new();

    [Fact]
    public void InMeleeReach_AfterVorpal_PushesLifeSurge()
    {
        var (context, scheduler) = Setup(targetDistance: 2f);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == ZeusAbilities.LifeSurge);
    }

    [Fact]
    public void OutOfMeleeReach_AfterVorpal_HoldsLifeSurge()
    {
        // The bug: next GCD out of reach is Piercing Talon, which must never eat the crit.
        var (context, scheduler) = Setup(targetDistance: 15f);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == ZeusAbilities.LifeSurge);
    }

    [Fact]
    public void NoTarget_HoldsLifeSurge()
    {
        var (context, scheduler) = Setup(targetDistance: 2f, hasTarget: false);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == ZeusAbilities.LifeSurge);
    }

    [Fact]
    public void AlreadyActive_NotPushedAgain()
    {
        var (context, scheduler) = Setup(targetDistance: 2f, hasLifeSurge: true);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == ZeusAbilities.LifeSurge);
    }

    [Fact]
    public void NotAfterHighPotencySetup_HeldEvenInReach()
    {
        // Combo idle (no Vorpal/proc pending) — the existing "wait for high-potency GCD" hold.
        var (context, scheduler) = Setup(targetDistance: 2f, lastComboAction: 0);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == ZeusAbilities.LifeSurge);
    }

    private static (Daedalus.Rotation.ZeusCore.Context.IZeusContext context,
        Daedalus.Rotation.Common.Scheduling.RotationScheduler scheduler) Setup(
        float targetDistance,
        bool hasTarget = true,
        bool hasLifeSurge = false,
        uint? lastComboAction = null)
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(777UL);
        enemy.Setup(x => x.CurrentHp).Returns(100000u);
        enemy.Setup(x => x.MaxHp).Returns(100000u);
        enemy.Setup(x => x.Position).Returns(new System.Numerics.Vector3(targetDistance, 0f, 0f));
        enemy.Setup(x => x.HitboxRadius).Returns(0.5f);

        var targeting = MockBuilders.CreateMockTargetingService(countEnemiesInRange: 1);
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(hasTarget ? enemy.Object : null);

        var actionService = MockBuilders.CreateMockActionService();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = ZeusTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            level: 50,
            lastComboAction: lastComboAction ?? DRGActions.VorpalThrust.ActionId,
            comboTimeRemaining: 20f,
            hasLifeSurge: hasLifeSurge);

        return (context, scheduler);
    }
}
