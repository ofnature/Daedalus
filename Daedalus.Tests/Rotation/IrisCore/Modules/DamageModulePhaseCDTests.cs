using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Config.DPS;
using Daedalus.Data;
using Daedalus.Models;
using Daedalus.Rotation.IrisCore.Abilities;
using Daedalus.Rotation.IrisCore.Modules;
using Daedalus.Services;
using Daedalus.Services.Action;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Daedalus.Tests.Rotation.IrisCore;
using Xunit;

namespace Daedalus.Tests.Rotation.IrisCore.Modules;

public class DamageModulePhaseCDTests
{
    [Fact]
    public void MogPortrait_NotPushed_WhenBurstPoolingAndBurstImminent()
    {
        var enemy = CreateMockEnemy();
        var targeting = BuildTargeting(enemy);
        var burst = new Mock<IBurstWindowService>();
        burst.Setup(x => x.IsBurstImminent(8f)).Returns(true);
        burst.Setup(x => x.IsInBurstWindow).Returns(false);

        var actionService = MockBuilders.CreateMockActionService();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var module = new BuffModule(burst.Object);
        var context = IrisTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            level: 100,
            inCombat: true,
            mogReady: true);

        module.CollectCandidates(context, scheduler, isMoving: false);

        var ogcd = scheduler.InspectOgcdQueue();
        Assert.DoesNotContain(ogcd, c => c.Behavior == IrisAbilities.MogOfTheAges);
    }

    [Fact]
    public void StrikingMuse_NotPushed_WhenStarryMuseWithinSixtySeconds()
    {
        var burst = new Mock<IBurstWindowService>();
        burst.Setup(x => x.IsInBurstWindow).Returns(false);

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.GetCooldownRemaining(PCTActions.StarryMuse.ActionId)).Returns(30f);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var module = new BuffModule(burst.Object);
        var context = IrisTestContext.Create(
            actionService: actionService,
            level: 100,
            inCombat: true,
            hasWeaponCanvas: true,
            strikingMuseReady: true);

        module.CollectCandidates(context, scheduler, isMoving: false);

        var ogcd = scheduler.InspectOgcdQueue();
        Assert.DoesNotContain(ogcd, c => c.Behavior == IrisAbilities.StrikingMuse);
    }

    [Fact]
    public void RepaintMotif_NotPushed_WhenBurstImminent()
    {
        var enemy = CreateMockEnemy();
        var targeting = BuildTargeting(enemy);
        var burst = new Mock<IBurstWindowService>();
        burst.Setup(x => x.IsBurstImminent(10f)).Returns(true);
        burst.Setup(x => x.IsInBurstWindow).Returns(false);

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.GetAdjustedActionId(PCTActions.WeaponMotif.ActionId))
            .Returns(PCTActions.HammerMotif.ActionId);
        actionService.Setup(x => x.GetCooldownRemaining(PCTActions.StarryMuse.ActionId)).Returns(120f);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var module = new DamageModule(burst.Object);
        var context = IrisTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            level: 100,
            inCombat: true,
            needsWeaponMotif: true,
            needsCreatureMotif: false,
            needsLandscapeMotif: false);

        module.CollectCandidates(context, scheduler, isMoving: false);

        var gcd = scheduler.InspectGcdQueue();
        Assert.DoesNotContain(gcd, c => c.Behavior == IrisAbilities.HammerMotif);
    }

    [Fact]
    public void LandscapeMotif_PaintedInCombat_AbovetheCombo_WhenScenicMuseImminent()
    {
        // Regression: in-combat motif painting was priority 9 (below the color combo) and never fired, so
        // the canvas/muse/Starry system stayed dormant in AutoDuty pulls. It must now paint Starry Sky when
        // Scenic Muse is within ~15s, at a priority that beats the combo (so the canvas is ready for burst).
        var enemy = CreateMockEnemy();
        var targeting = BuildTargeting(enemy);

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.GetAdjustedActionId(PCTActions.LandscapeMotif.ActionId))
            .Returns(PCTActions.StarrySkyMotif.ActionId);
        actionService.Setup(x => x.GetCooldownRemaining(PCTActions.ScenicMuse.ActionId)).Returns(10f);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var module = new DamageModule();
        var context = IrisTestContext.Create(
            actionService: actionService, targetingService: targeting, level: 100, inCombat: true,
            needsLandscapeMotif: true, hasLandscapeCanvas: false);

        module.CollectCandidates(context, scheduler, isMoving: false);

        var motif = Assert.Single(scheduler.InspectGcdQueue(), c => c.Behavior == IrisAbilities.StarrySkyMotif);
        Assert.True(motif.Priority < 7, $"motif priority {motif.Priority} must beat the color combo (7/8)");
    }

    [Fact]
    public void LandscapeMotif_NotPainted_WhenScenicMuseFarAway()
    {
        // Muse-timed: don't waste a GCD painting Landscape when Scenic Muse is still ~a minute out.
        var enemy = CreateMockEnemy();
        var targeting = BuildTargeting(enemy);

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.GetAdjustedActionId(PCTActions.LandscapeMotif.ActionId))
            .Returns(PCTActions.StarrySkyMotif.ActionId);
        actionService.Setup(x => x.GetCooldownRemaining(PCTActions.ScenicMuse.ActionId)).Returns(60f);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var module = new DamageModule();
        var context = IrisTestContext.Create(
            actionService: actionService, targetingService: targeting, level: 100, inCombat: true,
            needsLandscapeMotif: true, hasLandscapeCanvas: false);

        module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == IrisAbilities.StarrySkyMotif);
    }

    [Fact]
    public void CreatureMotif_PaintedInCombat_WhenLivingMuseReady()
    {
        var enemy = CreateMockEnemy();
        var targeting = BuildTargeting(enemy);

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionReady(PCTActions.LivingMuse.ActionId)).Returns(true);
        actionService.Setup(x => x.GetAdjustedActionId(PCTActions.CreatureMotif.ActionId))
            .Returns(PCTActions.PomMotif.ActionId);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var module = new DamageModule();
        var context = IrisTestContext.Create(
            actionService: actionService, targetingService: targeting, level: 100, inCombat: true,
            needsCreatureMotif: true, hasCreatureCanvas: false,
            needsLandscapeMotif: false, needsWeaponMotif: false);

        module.CollectCandidates(context, scheduler, isMoving: false);

        var creatureMotifs = new[]
        {
            IrisAbilities.PomMotif, IrisAbilities.WingMotif, IrisAbilities.ClawMotif, IrisAbilities.MawMotif,
        };
        Assert.Contains(scheduler.InspectGcdQueue(), c => System.Array.IndexOf(creatureMotifs, c.Behavior) >= 0);
    }

    [Fact]
    public void BaseCombo_UsesSmartAoETarget_WhenShouldUseAoe()
    {
        var enemy = CreateMockEnemy(100UL);
        var smartTarget = CreateMockEnemy(200UL);
        var targeting = BuildTargeting(enemy);

        var smartAoE = new Mock<ISmartAoEService>();
        smartAoE.Setup(x => x.FindBestAoETarget(
                PCTActions.Fire2InRed.ActionId,
                It.IsAny<float>(),
                It.IsAny<IPlayerCharacter>(),
                It.IsAny<bool>()))
            .Returns(new AoEResult(smartTarget.Object, 4, 0f, AoEShape.Circle));

        var actionService = MockBuilders.CreateMockActionService();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var module = new DamageModule(smartAoEService: smartAoE.Object);
        var context = IrisTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            level: 100,
            inCombat: true,
            shouldUseAoe: true,
            nearbyEnemyCount: 3);

        module.CollectCandidates(context, scheduler, isMoving: false);

        var gcd = scheduler.InspectGcdQueue();
        var fire2 = Assert.Single(gcd, c => c.Behavior == IrisAbilities.Fire2InRed);
        Assert.Equal(200UL, fire2.TargetId);
    }

    private static Configuration Config(Action<PictomancerConfig>? configure = null)
    {
        var config = IrisTestContext.CreateDefaultPctConfiguration();
        configure?.Invoke(config.Pictomancer);
        return config;
    }

    private static Mock<ITargetingService> BuildTargeting(Mock<IBattleNpc> enemy)
    {
        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        return targeting;
    }

    private static Mock<IBattleNpc> CreateMockEnemy(ulong objectId = 99999UL)
    {
        var mock = new Mock<IBattleNpc>();
        mock.Setup(x => x.GameObjectId).Returns(objectId);
        mock.Setup(x => x.CurrentHp).Returns(10000u);
        mock.Setup(x => x.MaxHp).Returns(10000u);
        return mock;
    }
}
