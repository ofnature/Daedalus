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
/// Pass 1 rotation correctness: Imperator weave, Divine Might filler, proc gates.
/// </summary>
public sealed class ThemisPass1RotationTests
{
    private readonly BuffModule _buffModule = new();
    private readonly DamageModule _damageModule = new();

    [Fact]
    public void CollectCandidates_DuringFoF_PushesMagicPhaseBeforeConfiteor()
    {
        var targeting = BuildMeleeTargeting(enemyCount: 1);
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.GetAdjustedActionId(PLDActions.Requiescat.ActionId))
            .Returns(PLDActions.Imperator.ActionId);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = CreateMockContext(
            targeting,
            actionService,
            hasFightOrFlight: true,
            hasRequiescat: false,
            confiteorStep: 1);

        _buffModule.CollectCandidates(context, scheduler, isMoving: false);
        _damageModule.CollectCandidates(context, scheduler, isMoving: false);

        var ogcd = scheduler.InspectOgcdQueue();
        var gcd = scheduler.InspectGcdQueue();
        Assert.Contains(ogcd, c => c.Behavior == ThemisAbilities.Requiescat && c.Priority == 1);
        Assert.DoesNotContain(gcd, c => c.Behavior == ThemisAbilities.Confiteor);
    }

    [Fact]
    public void CollectCandidates_DivineMightWithoutRequiescat_PushesHolySpirit()
    {
        var targeting = BuildMeleeTargeting(enemyCount: 1);
        var actionService = MockBuilders.CreateMockActionService();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = CreateMockContext(
            targeting,
            actionService,
            hasDivineMight: true,
            hasRequiescat: false);

        _damageModule.CollectCandidates(context, scheduler, isMoving: false);

        var gcd = scheduler.InspectGcdQueue();
        Assert.Contains(gcd, c => c.Behavior == ThemisAbilities.HolySpirit && c.Priority == 4);
    }

    [Fact]
    public void CollectCandidates_ComboFinisherWithLiveProc_DoesNotPushRoyalAuthority()
    {
        var targeting = BuildMeleeTargeting(enemyCount: 1);
        var actionService = MockBuilders.CreateMockActionService();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = CreateMockContext(
            targeting,
            actionService,
            comboStep: 3,
            lastComboAction: PLDActions.RiotBlade.ActionId,
            comboTimeRemaining: 30f,
            hasDivineMight: true);

        _damageModule.CollectCandidates(context, scheduler, isMoving: false);

        var gcd = scheduler.InspectGcdQueue();
        Assert.DoesNotContain(gcd, c => c.Behavior == ThemisAbilities.RoyalAuthority);
    }

    [Fact]
    public void CollectCandidates_AtonementProcActive_DoesNotPushTotalEclipse()
    {
        var targeting = BuildMeleeTargeting(enemyCount: 3);
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.GetAdjustedActionId(PLDActions.RoyalAuthority.ActionId))
            .Returns(PLDActions.Atonement.ActionId);
        actionService.Setup(x => x.GetAdjustedActionId(PLDActions.TotalEclipse.ActionId))
            .Returns(PLDActions.TotalEclipse.ActionId);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = CreateMockContext(
            targeting,
            actionService,
            atonementStep: 1,
            enemyCountForTargeting: 3);

        _damageModule.CollectCandidates(context, scheduler, isMoving: false);

        var gcd = scheduler.InspectGcdQueue();
        Assert.DoesNotContain(gcd, c => c.Behavior == ThemisAbilities.TotalEclipse);
        Assert.Contains(gcd, c => c.Behavior == ThemisAbilities.Atonement);
    }

    private static IThemisContext CreateMockContext(
        Mock<ITargetingService> targeting,
        Mock<IActionService> actionService,
        bool hasFightOrFlight = false,
        bool hasRequiescat = false,
        bool hasDivineMight = false,
        int confiteorStep = 0,
        int atonementStep = 0,
        int comboStep = 0,
        uint lastComboAction = 0,
        float comboTimeRemaining = 0f,
        int enemyCountForTargeting = 1)
    {
        var config = ThemisTestContext.CreateDefaultPaladinConfiguration();
        config.Tank.EnableAoEDamage = true;
        var player = MockBuilders.CreateMockPlayerCharacter(level: 100);
        var enemy = CreateMockEnemy(12345UL);
        var objectTable = MockBuilders.CreateMockObjectTable();
        objectTable.Setup(x => x.SearchById(player.Object.GameObjectId)).Returns(player.Object);
        objectTable.Setup(x => x.SearchById(enemy.Object.GameObjectId)).Returns(enemy.Object);

        targeting.Setup(x => x.CountEnemiesInRangeOfTarget(
                It.IsAny<float>(), It.IsAny<IBattleNpc>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemyCountForTargeting);

        var mock = new Mock<IThemisContext>();
        mock.Setup(x => x.Player).Returns(player.Object);
        mock.Setup(x => x.InCombat).Returns(true);
        mock.Setup(x => x.IsMoving).Returns(false);
        mock.Setup(x => x.CanExecuteGcd).Returns(true);
        mock.Setup(x => x.CanExecuteOgcd).Returns(true);
        mock.Setup(x => x.Configuration).Returns(config);
        mock.Setup(x => x.ActionService).Returns(actionService.Object);
        mock.Setup(x => x.TargetingService).Returns(targeting.Object);
        mock.Setup(x => x.ObjectTable).Returns(objectTable.Object);
        mock.Setup(x => x.PartyHelper).Returns((Daedalus.Rotation.ThemisCore.Helpers.ThemisPartyHelper?)null);
        mock.Setup(x => x.TrainingService).Returns((Daedalus.Services.Training.ITrainingService?)null);
        mock.Setup(x => x.TimeToKillService).Returns((Daedalus.Services.Combat.ITimeToKillService?)null);
        mock.Setup(x => x.ComboStep).Returns(comboStep);
        mock.Setup(x => x.LastComboAction).Returns(lastComboAction);
        mock.Setup(x => x.ComboTimeRemaining).Returns(comboTimeRemaining);
        mock.Setup(x => x.AtonementStep).Returns(atonementStep);
        mock.Setup(x => x.ConfiteorStep).Returns(confiteorStep);
        mock.Setup(x => x.HasFightOrFlight).Returns(hasFightOrFlight);
        mock.Setup(x => x.FightOrFlightRemaining).Returns(15f);
        mock.Setup(x => x.HasRequiescat).Returns(hasRequiescat);
        mock.Setup(x => x.RequiescatStacks).Returns(hasRequiescat ? 4 : 0);
        mock.Setup(x => x.HasDivineMight).Returns(hasDivineMight);
        mock.Setup(x => x.HasGoringBladeReady).Returns(false);
        mock.Setup(x => x.HasBladeOfHonor).Returns(false);
        mock.Setup(x => x.HasSwiftcast).Returns(false);
        mock.Setup(x => x.Debug).Returns(new ThemisDebugState());

        return mock.Object;
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
}
