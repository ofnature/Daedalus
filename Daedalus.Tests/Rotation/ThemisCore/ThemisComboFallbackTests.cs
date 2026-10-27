using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Moq;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.ThemisCore.Abilities;
using Daedalus.Rotation.ThemisCore.Context;
using Daedalus.Rotation.ThemisCore.Modules;
using Daedalus.Services.Action;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.ThemisCore;

/// <summary>
/// Regression guards for combo continuation deadlocks: when the module pushes a
/// combo finisher but the scheduler ActionStatus gate blocks it, the starter
/// fallback at lower priority must still dispatch.
/// </summary>
public sealed class ThemisComboFallbackTests
{
    private readonly DamageModule _module = new();

    [Fact]
    public void CollectCandidates_AoEComboStep2_OnlyProminencePushed()
    {
        var targeting = BuildMeleeTargeting(enemyCount: 3);
        var actionService = MockBuilders.CreateMockActionService();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = CreateAoEComboContext(targeting, actionService, comboStep: 2);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var gcd = scheduler.InspectGcdQueue();
        Assert.Contains(gcd, c => c.Behavior == ThemisAbilities.Prominence && c.Priority == 2);
        Assert.DoesNotContain(gcd, c => c.Behavior == ThemisAbilities.TotalEclipse);
    }

    [Fact]
    public void CollectCandidates_ProminenceNotLearned_MultiTarget_PushesTotalEclipseFiller()
    {
        var targeting = BuildMeleeTargeting(enemyCount: 3);
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionLearned(PLDActions.Prominence.ActionId)).Returns(false);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = ThemisTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            objectTable: BuildObjectTableForMelee(targeting, enemyId: 12345UL),
            level: 80,
            comboStep: 0,
            lastComboAction: 0,
            comboTimeRemaining: 0f);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var gcd = scheduler.InspectGcdQueue();
        Assert.Contains(gcd, c => c.Behavior == ThemisAbilities.TotalEclipse);
        Assert.DoesNotContain(gcd, c => c.Behavior == ThemisAbilities.Prominence);
    }

    [Fact]
    public void CollectCandidates_ProminenceNotLearned_StuckTeCombo_FallsBackToSingleTarget()
    {
        var targeting = BuildMeleeTargeting(enemyCount: 3);
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionLearned(PLDActions.Prominence.ActionId)).Returns(false);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = ThemisTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            objectTable: BuildObjectTableForMelee(targeting, enemyId: 12345UL),
            level: 80,
            comboStep: 2,
            lastComboAction: PLDActions.TotalEclipse.ActionId,
            comboTimeRemaining: 30f);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var gcd = scheduler.InspectGcdQueue();
        Assert.Contains(gcd, c => c.Behavior == ThemisAbilities.FastBlade);
        Assert.DoesNotContain(gcd, c => c.Behavior == ThemisAbilities.Prominence);
        Assert.DoesNotContain(gcd, c => c.Behavior == ThemisAbilities.TotalEclipse);
    }

    [Fact]
    public void CollectCandidates_AoEComboStep2_LowEnemyCount_StillPushesProminenceNotSingleTarget()
    {
        var targeting = BuildMeleeTargeting(enemyCount: 1);
        var actionService = MockBuilders.CreateMockActionService();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = CreateAoEComboContext(targeting, actionService, comboStep: 2);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var gcd = scheduler.InspectGcdQueue();
        Assert.Contains(gcd, c => c.Behavior == ThemisAbilities.Prominence);
        Assert.DoesNotContain(gcd, c => c.Behavior == ThemisAbilities.FastBlade);
        Assert.DoesNotContain(gcd, c => c.Behavior == ThemisAbilities.TotalEclipse);
    }

    [Fact]
    public void CollectCandidates_AoEStep2_PushesProminence_WithoutHotbarSubstitution()
    {
        // In-game regression: PLD AoE is not a button-replacement combo, so GetAdjustedActionId never
        // resolves Total Eclipse -> Prominence. Detection must work off pure combo state. Without this,
        // the rotation spammed Total Eclipse forever and never fired the second AoE (Prominence).
        var targeting = BuildMeleeTargeting(enemyCount: 3);
        var actionService = MockBuilders.CreateMockActionService();
        // Deliberately do NOT mock any Total Eclipse -> Prominence adjustment: GetAdjustedActionId returns
        // the input unchanged (mirrors the real game for this non-replacement combo).
        var enemy = CreateMockEnemy(12345UL);
        var objectTable = BuildObjectTableForMelee(targeting, enemyId: 12345UL);
        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = ThemisTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            objectTable: objectTable,
            level: 100,
            comboStep: 2,
            lastComboAction: PLDActions.TotalEclipse.ActionId,
            comboTimeRemaining: 30f);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var gcd = scheduler.InspectGcdQueue();
        Assert.Contains(gcd, c => c.Behavior == ThemisAbilities.Prominence);
        Assert.DoesNotContain(gcd, c => c.Behavior == ThemisAbilities.TotalEclipse);
    }

    [Fact]
    public void Dispatch_AoEComboStep2_ProminenceDispatchesByOwnId_NotViaTotalEclipseBaseId()
    {
        // PLD AoE is NOT a button-replacement combo: Prominence is a separate, combo-gated action and
        // must dispatch by its own id. Routing through a Total Eclipse ReplacementBaseId would re-cast
        // Total Eclipse and the AoE combo would never finish (observed in-game as endless Total Eclipse).
        var targeting = BuildMeleeTargeting(enemyCount: 3);
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.CanExecuteActionId(It.IsAny<uint>())).Returns(true);
        actionService.Setup(x => x.ExecuteGcd(
                It.Is<ActionDefinition>(a => a.ActionId == PLDActions.Prominence.ActionId), It.IsAny<ulong>()))
            .Returns(true);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = CreateAoEComboContext(targeting, actionService, comboStep: 2);

        _module.CollectCandidates(context, scheduler, isMoving: false);
        var result = scheduler.DispatchGcd(context);

        Assert.True(result.Dispatched);
        actionService.Verify(
            x => x.ExecuteGcd(
                It.Is<ActionDefinition>(a => a.ActionId == PLDActions.Prominence.ActionId),
                It.IsAny<ulong>()),
            Times.Once);
        // Never routed through the Total Eclipse base id (no ReplacementBaseId).
        actionService.Verify(
            x => x.ExecuteGcdRaw(
                It.IsAny<ActionDefinition>(), PLDActions.TotalEclipse.ActionId, It.IsAny<ulong>()),
            Times.Never);
    }

    [Fact]
    public void CollectCandidates_AoEComboStep2_AfterProminenceLastGcd_PushesSingleTargetNotProminence()
    {
        var targeting = BuildMeleeTargeting(enemyCount: 1);
        var actionService = MockBuilders.CreateMockActionService();
        SetupProminenceComboReplacement(actionService);
        actionService.Setup(x => x.WasLastGcd(PLDActions.Prominence.ActionId)).Returns(true);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = CreateAoEComboContext(targeting, actionService, comboStep: 2);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var gcd = scheduler.InspectGcdQueue();
        Assert.DoesNotContain(gcd, c => c.Behavior == ThemisAbilities.Prominence);
        Assert.Contains(gcd, c => c.Behavior == ThemisAbilities.FastBlade);
    }

    [Fact]
    public void CollectCandidates_AoEComboStep2_SkipsAtonementChain()
    {
        var targeting = BuildMeleeTargeting(enemyCount: 3);
        var actionService = MockBuilders.CreateMockActionService();
        SetupProminenceComboReplacement(actionService);

        var context = CreateMockThemisContextForAoECombo(
            targeting, actionService, atonementStep: 1, goringReady: true);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        _module.CollectCandidates(context, scheduler, isMoving: false);

        var gcd = scheduler.InspectGcdQueue();
        Assert.Contains(gcd, c => c.Behavior == ThemisAbilities.Prominence);
        Assert.DoesNotContain(gcd, c => c.Behavior == ThemisAbilities.Atonement);
        Assert.DoesNotContain(gcd, c => c.Behavior == ThemisAbilities.GoringBlade);
    }

    [Fact]
    public void CollectCandidates_MidSingleTargetCombo_WithPack_BreaksToAoE()
    {
        // w2w: arriving on a fresh pack while mid 1-2-3 (last GCD Fast Blade) must break the ST combo and
        // AoE immediately, not finish the combo — AutoDuty only parks ~2 GCDs per pack.
        var targeting = BuildMeleeTargeting(enemyCount: 3);
        var actionService = MockBuilders.CreateMockActionService();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = ThemisTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            objectTable: BuildObjectTableForMelee(targeting, enemyId: 12345UL),
            level: 100,
            comboStep: 2,
            lastComboAction: PLDActions.FastBlade.ActionId, // mid single-target combo
            comboTimeRemaining: 30f);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var gcd = scheduler.InspectGcdQueue();
        Assert.Contains(gcd, c => c.Behavior == ThemisAbilities.TotalEclipse);
        Assert.DoesNotContain(gcd, c => c.Behavior == ThemisAbilities.RiotBlade);
    }

    [Fact]
    public void CollectCandidates_STComboStep2_BothRiotBladeAndFastBladePushed()
    {
        var targeting = BuildMeleeTargeting(enemyCount: 1);
        var actionService = MockBuilders.CreateMockActionService();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = ThemisTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            objectTable: BuildObjectTableForMelee(targeting, enemyId: 12345UL),
            level: 100,
            comboStep: 2,
            lastComboAction: PLDActions.FastBlade.ActionId,
            comboTimeRemaining: 30f);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var gcd = scheduler.InspectGcdQueue();
        Assert.Contains(gcd, c => c.Behavior == ThemisAbilities.RiotBlade && c.Priority == 6);
        Assert.Contains(gcd, c => c.Behavior == ThemisAbilities.FastBlade && c.Priority == 7);
    }

    [Fact]
    public void CollectCandidates_STComboStep3_BothFinisherAndFastBladePushed()
    {
        var targeting = BuildMeleeTargeting(enemyCount: 1);
        var actionService = MockBuilders.CreateMockActionService();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = ThemisTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            objectTable: BuildObjectTableForMelee(targeting, enemyId: 12345UL),
            level: 100,
            comboStep: 3,
            lastComboAction: PLDActions.RiotBlade.ActionId,
            comboTimeRemaining: 30f);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var gcd = scheduler.InspectGcdQueue();
        Assert.Contains(gcd, c => c.Behavior == ThemisAbilities.RoyalAuthority && c.Priority == 6);
        Assert.Contains(gcd, c => c.Behavior == ThemisAbilities.FastBlade && c.Priority == 7);
    }

    private static void SetupProminenceComboReplacement(Mock<IActionService> actionService)
    {
        actionService.Setup(x => x.GetAdjustedActionId(PLDActions.TotalEclipse.ActionId))
            .Returns(PLDActions.Prominence.ActionId);
    }

    private static IThemisContext CreateMockThemisContextForAoECombo(
        Mock<ITargetingService> targeting,
        Mock<IActionService> actionService,
        int atonementStep,
        bool goringReady)
    {
        var config = ThemisTestContext.CreateDefaultPaladinConfiguration();
        var player = MockBuilders.CreateMockPlayerCharacter(level: 100);
        var enemy = CreateMockEnemy(12345UL);
        var objectTable = MockBuilders.CreateMockObjectTable();
        objectTable.Setup(x => x.SearchById(player.Object.GameObjectId)).Returns(player.Object);
        objectTable.Setup(x => x.SearchById(enemy.Object.GameObjectId)).Returns(enemy.Object);

        var mock = new Mock<IThemisContext>();
        mock.Setup(x => x.Player).Returns(player.Object);
        mock.Setup(x => x.InCombat).Returns(true);
        mock.Setup(x => x.IsMoving).Returns(false);
        mock.Setup(x => x.CanExecuteGcd).Returns(true);
        mock.Setup(x => x.CanExecuteOgcd).Returns(false);
        mock.Setup(x => x.Configuration).Returns(config);
        mock.Setup(x => x.ActionService).Returns(actionService.Object);
        mock.Setup(x => x.TargetingService).Returns(targeting.Object);
        mock.Setup(x => x.ObjectTable).Returns(objectTable.Object);
        mock.Setup(x => x.PartyHelper).Returns((Daedalus.Rotation.ThemisCore.Helpers.ThemisPartyHelper)null!);
        mock.Setup(x => x.TrainingService).Returns((Daedalus.Services.Training.ITrainingService?)null);
        mock.Setup(x => x.TimeToKillService).Returns((Daedalus.Services.Combat.ITimeToKillService?)null);
        mock.Setup(x => x.ComboStep).Returns(2);
        mock.Setup(x => x.LastComboAction).Returns(PLDActions.TotalEclipse.ActionId);
        mock.Setup(x => x.ComboTimeRemaining).Returns(30f);
        mock.Setup(x => x.AtonementStep).Returns(atonementStep);
        mock.Setup(x => x.HasGoringBladeReady).Returns(goringReady);
        mock.Setup(x => x.HasFightOrFlight).Returns(true);
        mock.Setup(x => x.FightOrFlightRemaining).Returns(10f);
        mock.Setup(x => x.HasRequiescat).Returns(false);
        mock.Setup(x => x.ConfiteorStep).Returns(0);
        mock.Setup(x => x.HasBladeOfHonor).Returns(false);
        mock.Setup(x => x.Debug).Returns(new ThemisDebugState());

        return mock.Object;
    }

    private static ThemisContext CreateAoEComboContext(
        Mock<ITargetingService> targeting,
        Mock<IActionService> actionService,
        int comboStep)
    {
        SetupProminenceComboReplacement(actionService);

        var enemy = CreateMockEnemy(12345UL);
        var player = MockBuilders.CreateMockPlayerCharacter();
        var objectTable = MockBuilders.CreateMockObjectTable();
        objectTable.Setup(x => x.SearchById(player.Object.GameObjectId)).Returns(player.Object);
        objectTable.Setup(x => x.SearchById(enemy.Object.GameObjectId)).Returns(enemy.Object);

        return ThemisTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            objectTable: objectTable,
            level: 100,
            comboStep: comboStep,
            lastComboAction: PLDActions.TotalEclipse.ActionId,
            comboTimeRemaining: 30f);
    }

    private static Mock<ITargetingService> BuildMeleeTargeting(int enemyCount)
    {
        var enemy = CreateMockEnemy(12345UL);
        var targeting = MockBuilders.CreateMockTargetingService(countEnemiesInRange: enemyCount);
        targeting.Setup(x => x.CountEnemiesInRangeOfTarget(
                It.IsAny<float>(), It.IsAny<IBattleNpc>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemyCount);
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        return targeting;
    }

    private static Mock<IBattleNpc> CreateMockEnemy(ulong objectId)
    {
        var mock = new Mock<IBattleNpc>();
        mock.Setup(x => x.GameObjectId).Returns(objectId);
        mock.Setup(x => x.CurrentHp).Returns(10000u);
        mock.Setup(x => x.MaxHp).Returns(10000u);
        return mock;
    }

    private static Mock<IObjectTable> BuildObjectTableForMelee(Mock<ITargetingService> targeting, ulong enemyId)
    {
        var enemy = CreateMockEnemy(enemyId);
        var player = MockBuilders.CreateMockPlayerCharacter();
        var objectTable = MockBuilders.CreateMockObjectTable();
        objectTable.Setup(x => x.SearchById(player.Object.GameObjectId)).Returns(player.Object);
        objectTable.Setup(x => x.SearchById(enemy.Object.GameObjectId)).Returns(enemy.Object);
        return objectTable;
    }
}
