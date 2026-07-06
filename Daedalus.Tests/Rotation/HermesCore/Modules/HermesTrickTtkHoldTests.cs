using System;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Rotation.HermesCore.Abilities;
using Daedalus.Rotation.HermesCore.Modules;
using Daedalus.Rotation.PrometheusCore.Helpers;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.HermesCore.Modules;

/// <summary>
/// End-of-pack Trick waste (Xelphatol Lv60 logs 2026-07-05/06): four occurrences of Trick Attack
/// landing on sub-10%-HP mobs as combat ended — a +10% debuff on a dying pack burns the 60s
/// cooldown for nothing, while Shadow Walker (20s) could ride the transit and open the NEXT pack
/// at full value. The MCH Queen pack-TTK estimator now gates the Trick/Kunai's Bane push.
/// </summary>
public class HermesTrickTtkHoldTests
{
    private static (BuffModule module, Daedalus.Rotation.HermesCore.Context.IHermesContext context,
        Daedalus.Rotation.Common.Scheduling.RotationScheduler scheduler) Setup(
        PackTtkEstimator estimator, long currentPackHp, int? ttkThreshold = null)
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(4242UL);
        enemy.Setup(x => x.CurrentHp).Returns(10000u);
        enemy.Setup(x => x.MaxHp).Returns(100000u);

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.SumEnemyCurrentHpInRange(It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(currentPackHp);

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = HermesTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            level: 60,
            hasSuiton: true,
            suitonRemaining: 15f);
        if (ttkThreshold is { } t)
            context.Configuration.Ninja.TrickMinPackTtkSeconds = t;

        return (new BuffModule(packTtk: estimator), context, scheduler);
    }

    private static PackTtkEstimator FedEstimator(long hpFourSecondsAgo, long hpNow)
    {
        var estimator = new PackTtkEstimator();
        var now = DateTime.UtcNow;
        estimator.Sample(hpFourSecondsAgo, now.AddSeconds(-4));
        estimator.Sample(hpNow, now);
        return estimator;
    }

    [Fact]
    public void DyingPack_HoldsTrickAttack()
    {
        // 100k -> 10k over 4s = pack dead in ~0.4s: exactly the field waste. Hold.
        var (module, context, scheduler) = Setup(FedEstimator(100_000, 10_000), currentPackHp: 10_000);

        module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == HermesAbilities.TrickAttack);
        Assert.Contains("pack TTK", context.Debug.BuffState);
    }

    [Fact]
    public void HealthyPack_FiresTrickAttack()
    {
        // Slow kill rate = long TTK: burst normally.
        var (module, context, scheduler) = Setup(FedEstimator(100_000, 99_000), currentPackHp: 99_000);

        module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == HermesAbilities.TrickAttack);
    }

    [Fact]
    public void FreshPull_NoEstimate_FiresTrickAttack()
    {
        // No samples yet (fresh pull) → estimator returns null → never hold the opener burst.
        var (module, context, scheduler) = Setup(new PackTtkEstimator(), currentPackHp: 500_000);

        module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == HermesAbilities.TrickAttack);
    }

    [Fact]
    public void ThresholdZero_DisablesTheHold()
    {
        var (module, context, scheduler) = Setup(FedEstimator(100_000, 10_000), currentPackHp: 10_000, ttkThreshold: 0);

        module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == HermesAbilities.TrickAttack);
    }

    [Fact]
    public void TrickMinPackTtk_DefaultsToSixSeconds()
    {
        Assert.Equal(6, new Daedalus.Configuration().Ninja.TrickMinPackTtkSeconds);
    }
}
