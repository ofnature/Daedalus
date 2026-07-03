using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Daedalus.Config.DPS;
using Daedalus.Data;
using Daedalus.Rotation.KratosCore.Context;
using Daedalus.Rotation.KratosCore.Modules;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Moq;
using Xunit;

namespace Daedalus.Tests.Rotation.KratosCore.Modules;

public class DamageModuleFormSelectionTests
{
    private readonly DamageModule _module = new();

    [Fact]
    public void CollectCandidates_OutOfMelee_PushesRangedChakraSpender()
    {
        var enemy = CreateMockEnemy();
        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns((IBattleNpc?)null);
        targeting.Setup(x => x.FindNearbyEnemy(25f, It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var actionService = MockBuilders.CreateMockActionService(canExecuteGcd: true, canExecuteOgcd: true);
        actionService.Setup(x => x.IsActionReady(MNKActions.Enlightenment.ActionId)).Returns(true);
        actionService.Setup(x => x.IsActionReady(MNKActions.Thunderclap.ActionId)).Returns(false);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = KratosTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            level: 100,
            chakra: 5,
            currentForm: MonkForm.OpoOpo);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var ogcd = scheduler.InspectOgcdQueue();
        Assert.Contains(ogcd, c => c.Behavior.Action.ActionId == MNKActions.Enlightenment.ActionId);
    }

    [Fact]
    public void CollectCandidates_LowLevel_OpoAoE_FallsBackToBootshine()
    {
        // Arm of the Destroyer is Lv26 — below it an AoE pack still takes the ST opo GCD
        // instead of pushing an unlearned AoE action.
        var enemy = CreateMockEnemy();
        var targeting = MockBuilders.CreateMockTargetingService(countEnemiesInRange: 5);
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        var actionService = MockBuilders.CreateMockActionService();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = KratosTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            level: 20,
            currentForm: MonkForm.OpoOpo);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var gcd = scheduler.InspectGcdQueue();
        Assert.Contains(gcd, c => c.Behavior.Action.ActionId == MNKActions.Bootshine.ActionId);
        Assert.DoesNotContain(gcd, c => c.Behavior.Action.ActionId == MNKActions.ArmOfTheDestroyer.ActionId);
    }

    [Fact]
    public void CollectCandidates_RaptorsFury_PrefersTrueStrikeOverTwinSnakes()
    {
        var enemy = CreateMockEnemy();
        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var actionService = MockBuilders.CreateMockActionService(canExecuteGcd: true);
        var context = KratosTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            level: 100,
            currentForm: MonkForm.Raptor,
            hasDisciplinedFist: true,
            disciplinedFistRemaining: 20f,
            hasRaptorsFury: true,
            isAtRear: true);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        _module.CollectCandidates(context, scheduler, isMoving: false);

        var gcd = scheduler.InspectGcdQueue();
        Assert.Contains(gcd, c => c.Behavior.Action.ActionId == MNKActions.RisingRaptor.ActionId
                                  || c.Behavior.Action.ActionId == MNKActions.TrueStrike.ActionId);
        Assert.DoesNotContain(gcd, c => c.Behavior.Action.ActionId == MNKActions.TwinSnakes.ActionId);
    }

    [Fact]
    public void CollectCandidates_ModeratePositional_AllowsOffPositionCast()
    {
        var enemy = CreateMockEnemy();
        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var config = KratosTestContext.CreateDefaultMonkConfiguration();
        config.Monk.PositionalStrictness = PositionalStrictness.Moderate;

        var actionService = MockBuilders.CreateMockActionService(canExecuteGcd: true);
        var context = KratosTestContext.Create(
            config: config,
            actionService: actionService,
            targetingService: targeting,
            level: 100,
            currentForm: MonkForm.OpoOpo,
            isAtRear: false,
            isAtFlank: false);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService, config: config);
        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.NotEmpty(scheduler.InspectGcdQueue());
    }

    [Fact]
    public void CollectCandidates_StrictWithDefaultEnforcePositionals_AllowsOffPositionCast()
    {
        var enemy = CreateMockEnemy();
        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var config = KratosTestContext.CreateDefaultMonkConfiguration();
        config.Monk.PositionalStrictness = PositionalStrictness.Strict;

        var actionService = MockBuilders.CreateMockActionService(canExecuteGcd: true);
        var context = KratosTestContext.Create(
            config: config,
            actionService: actionService,
            targetingService: targeting,
            level: 100,
            currentForm: MonkForm.OpoOpo,
            isAtRear: false,
            isAtFlank: false);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService, config: config);
        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.NotEmpty(scheduler.InspectGcdQueue());
    }

    [Fact]
    public void CollectCandidates_StrictEnforcedBlocksOffPositionCast()
    {
        var enemy = CreateMockEnemy();
        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var config = KratosTestContext.CreateDefaultMonkConfiguration();
        config.Monk.PositionalStrictness = PositionalStrictness.Strict;
        config.Monk.EnforcePositionals = true;
        config.Monk.AllowPositionalLoss = false;

        var actionService = MockBuilders.CreateMockActionService(canExecuteGcd: true);
        actionService.Setup(x => x.CanExecuteActionId(It.IsAny<uint>())).Returns(false);
        var debug = new KratosDebugState();
        var context = KratosTestContext.Create(
            config: config,
            actionService: actionService,
            targetingService: targeting,
            debugState: debug,
            level: 100,
            currentForm: MonkForm.OpoOpo,
            isAtRear: false,
            isAtFlank: false);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService, config: config);
        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Empty(scheduler.InspectGcdQueue());
        Assert.Contains("Moving to", debug.DamageState, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("flank", debug.DamageState, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CollectCandidates_MultiEnemy_EnforcedOffPositionStillCasts()
    {
        var enemy = CreateMockEnemy();
        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        MockBuilders.SetupEnemyPackCount(targeting, 3);

        var config = KratosTestContext.CreateDefaultMonkConfiguration();
        config.Monk.PositionalStrictness = PositionalStrictness.Strict;
        config.Monk.EnforcePositionals = true;
        config.Monk.AllowPositionalLoss = false;

        var actionService = MockBuilders.CreateMockActionService(canExecuteGcd: true);
        actionService.Setup(x => x.CanExecuteActionId(It.IsAny<uint>())).Returns(false);
        var context = KratosTestContext.Create(
            config: config,
            actionService: actionService,
            targetingService: targeting,
            level: 100,
            currentForm: MonkForm.OpoOpo,
            isAtRear: false,
            isAtFlank: false);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService, config: config);
        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.NotEmpty(scheduler.InspectGcdQueue());
    }

    [Fact]
    public void CollectCandidates_AoeRaptorWithoutFourPointFury_FallsBackToTwinSnakes()
    {
        var enemy = CreateMockEnemy();
        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        MockBuilders.SetupEnemyPackCount(targeting, 3);

        var config = KratosTestContext.CreateDefaultMonkConfiguration();
        config.Monk.EnableAoERotation = true;

        var actionService = MockBuilders.CreateMockActionService(canExecuteGcd: true);
        actionService.Setup(x => x.IsActionLearned(MNKActions.FourPointFury.ActionId)).Returns(false);
        actionService.Setup(x => x.IsActionLearned(MNKActions.TwinSnakes.ActionId)).Returns(true);

        var context = KratosTestContext.Create(
            config: config,
            actionService: actionService,
            targetingService: targeting,
            level: 47,
            currentForm: MonkForm.Raptor,
            isAtFlank: true);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService, config: config);
        _module.CollectCandidates(context, scheduler, isMoving: false);

        var gcd = scheduler.InspectGcdQueue();
        Assert.Contains(gcd, c => c.Behavior.Action.ActionId == MNKActions.TwinSnakes.ActionId);
        Assert.DoesNotContain(gcd, c => c.Behavior.Action.ActionId == MNKActions.FourPointFury.ActionId);
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
