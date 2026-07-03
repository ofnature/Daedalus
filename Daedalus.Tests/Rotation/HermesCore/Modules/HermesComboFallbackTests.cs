using System.Numerics;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.HermesCore.Abilities;
using Daedalus.Rotation.HermesCore.Context;
using Daedalus.Rotation.HermesCore.Modules;
using Daedalus.Services;
using Daedalus.Services.Action;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.HermesCore.Modules;

/// <summary>
/// Combo finisher at p6 with Spinning Edge fallback at p7 (Nike/Themis parity).
/// </summary>
public sealed class HermesComboFallbackTests
{
    private readonly DamageModule _module = new();

    [Fact]
    public void CollectCandidates_ComboStep2_QueuesFinishersAt6AndSpinningEdgeAt7()
    {
        var (scheduler, context) = CreateComboStep2Context(kazematoi: 2);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var gcd = scheduler.InspectGcdQueue();
        Assert.Contains(gcd, c => c.Behavior == HermesAbilities.AeolianEdge && c.Priority == 6);
        Assert.Contains(gcd, c => c.Behavior == HermesAbilities.ArmorCrush && c.Priority == 6);
        Assert.Contains(gcd, c => c.Behavior == HermesAbilities.SpinningEdge && c.Priority == 7);
    }

    [Fact]
    public void CollectCandidates_ComboStep2_KazematoiZero_QueuesArmorCrushAt6AndSpinningEdgeAt7()
    {
        var (scheduler, context) = CreateComboStep2Context(kazematoi: 0);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var gcd = scheduler.InspectGcdQueue();
        Assert.Contains(gcd, c => c.Behavior == HermesAbilities.ArmorCrush && c.Priority == 6);
        Assert.DoesNotContain(gcd, c => c.Behavior == HermesAbilities.AeolianEdge);
        Assert.Contains(gcd, c => c.Behavior == HermesAbilities.SpinningEdge && c.Priority == 7);
    }

    [Fact]
    public void Dispatch_ComboStep2_BothFinishersActionStatusBlocked_DispatchesSpinningEdgeAt7()
    {
        var targeting = BuildTargeting();
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.CanExecuteActionId(It.IsAny<uint>()))
            .Returns((uint id) => id != NINActions.AeolianEdge.ActionId && id != NINActions.ArmorCrush.ActionId);
        actionService.Setup(x => x.ExecuteGcd(It.IsAny<ActionDefinition>(), It.IsAny<ulong>()))
            .Returns(true);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = HermesTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            level: 100,
            ninki: 0,
            kazematoi: 2,
            comboStep: 2,
            lastComboAction: NINActions.GustSlash.ActionId,
            comboTimeRemaining: 30f,
            hasRaijuReady: false,
            hasPhantomKamaitachiReady: false);

        _module.CollectCandidates(context, scheduler, isMoving: false);
        var result = scheduler.DispatchGcd(context);

        Assert.True(result.Dispatched);
        Assert.Same(HermesAbilities.SpinningEdge, result.Winner);
        Assert.Equal("Stalled (combo step 2 — no finisher)", context.Debug.DamageState);
        actionService.Verify(
            x => x.ExecuteGcd(
                It.Is<ActionDefinition>(a => a.ActionId == NINActions.SpinningEdge.ActionId),
                It.IsAny<ulong>()),
            Times.Once);
        actionService.Verify(
            x => x.ExecuteGcd(
                It.Is<ActionDefinition>(a => a.ActionId == NINActions.AeolianEdge.ActionId),
                It.IsAny<ulong>()),
            Times.Never);
    }

    [Fact]
    public void CollectCandidates_TargetOutOfMeleeRange_QueuesThrowingDaggerNotCombo()
    {
        var enemy = CreateMockEnemy();
        enemy.Setup(x => x.Position).Returns(new Vector3(0, 0, 30)); // 30y away — out of melee

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.CountEnemiesInRange(It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(1);

        var actionService = MockBuilders.CreateMockActionService();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = HermesTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            level: 100,
            ninki: 0,
            kazematoi: 2,
            comboStep: 2,
            lastComboAction: NINActions.GustSlash.ActionId,
            comboTimeRemaining: 30f,
            hasRaijuReady: false,
            hasPhantomKamaitachiReady: false);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var gcd = scheduler.InspectGcdQueue();
        Assert.Contains(gcd, c => c.Behavior == HermesAbilities.ThrowingDagger && c.Priority == 5);
        Assert.DoesNotContain(gcd, c => c.Behavior == HermesAbilities.AeolianEdge);
        Assert.DoesNotContain(gcd, c => c.Behavior == HermesAbilities.ArmorCrush);
        Assert.DoesNotContain(gcd, c => c.Behavior == HermesAbilities.SpinningEdge);
    }

    [Fact]
    public void CollectCandidates_LowLevelAoEPack_RunsSingleTargetCombo()
    {
        // Death Blossom is Lv38 and NIN starts at 30 — an AoE pack at 30-37 must run the ST
        // combo instead of pushing an unlearned AoE starter forever (the DRG Lv40 lesson).
        var enemy = CreateMockEnemy();
        var targeting = MockBuilders.CreateMockTargetingService(countEnemiesInRange: 4);
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        var actionService = MockBuilders.CreateMockActionService();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = HermesTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            level: 32);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var gcd = scheduler.InspectGcdQueue();
        Assert.Contains(gcd, c => c.Behavior == HermesAbilities.SpinningEdge);
        Assert.DoesNotContain(gcd, c => c.Behavior == HermesAbilities.DeathBlossom);
    }

    private static (RotationScheduler Scheduler, IHermesContext Context) CreateComboStep2Context(int kazematoi)
    {
        var targeting = BuildTargeting();
        var actionService = MockBuilders.CreateMockActionService();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = HermesTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            level: 100,
            ninki: 0,
            kazematoi: kazematoi,
            comboStep: 2,
            lastComboAction: NINActions.GustSlash.ActionId,
            comboTimeRemaining: 30f,
            hasRaijuReady: false,
            hasPhantomKamaitachiReady: false);

        return (scheduler, context);
    }

    private static Mock<ITargetingService> BuildTargeting()
    {
        var enemy = CreateMockEnemy();
        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.CountEnemiesInRange(It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(1);
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
