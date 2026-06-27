using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
using Daedalus.Rotation.HermesCore.Context;
using Daedalus.Rotation.HermesCore.Helpers;
using Daedalus.Rotation.HermesCore.Modules;
using Daedalus.Services;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Daedalus.Tests.Rotation.HermesCore;
using Xunit;

namespace Daedalus.Tests.Rotation.HermesCore.Modules;

public class NinjutsuModuleTcjDesyncTests
{
    [Fact]
    public void CollectCandidates_AoEPull_StartsDotonNotSuiton()
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(99999UL);

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        MockBuilders.SetupEnemyPackCount(targeting, aoeRange: 4);

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionReady(NINActions.Ten.ActionId)).Returns(true);
        actionService.Setup(x => x.IsActionReady(NINActions.KunaisBane.ActionId)).Returns(true);

        var helper = new MudraHelper();
        var context = HermesTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            mudraHelper: helper,
            hasSuiton: false,
            canExecuteGcd: true,
            inCombat: true);

        new NinjutsuModule().CollectCandidates(context, SchedulerFactory.CreateForTest(), isMoving: false);

        Assert.True(helper.IsSequenceActive);
        Assert.Equal(NINActions.NinjutsuType.Doton, helper.TargetNinjutsu);
    }

    [Fact]
    public void CollectCandidates_NeedsSuiton_TenOnCd_DoesNotStartSequence()
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(99999UL);

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionReady(NINActions.KunaisBane.ActionId)).Returns(true);
        actionService.Setup(x => x.IsActionReady(NINActions.Ten.ActionId)).Returns(false);
        actionService.Setup(x => x.GetAdjustedActionId(NINActions.Ten.ActionId))
            .Returns(NINActions.Ten.ActionId);
        actionService.Setup(x => x.GetCurrentCharges(NINActions.Ten.ActionId)).Returns(0u);
        actionService.Setup(x => x.GetCooldownRemaining(NINActions.Ten.ActionId)).Returns(8f);
        actionService.Setup(x => x.GcdRemaining).Returns(0f);

        var helper = new MudraHelper();
        var context = HermesTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            mudraHelper: helper,
            hasSuiton: false,
            canExecuteGcd: true,
            inCombat: true);

        new NinjutsuModule().CollectCandidates(context, SchedulerFactory.CreateForTest(), isMoving: false);

        Assert.False(helper.IsSequenceActive);
        Assert.Contains("Queued Suiton", context.Debug.NinjutsuState);
    }

    [Fact]
    public void CollectCandidates_NeedsSuiton_StartsSequenceAndAttemptsFirstMudraSameFrame()
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(99999UL);

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionReady(NINActions.Ten.ActionId)).Returns(true);
        actionService.Setup(x => x.IsActionReady(NINActions.KunaisBane.ActionId)).Returns(true);

        var helper = new MudraHelper();
        var context = HermesTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            mudraHelper: helper,
            hasSuiton: false,
            canExecuteGcd: true,
            inCombat: true);

        new NinjutsuModule().CollectCandidates(context, SchedulerFactory.CreateForTest(), isMoving: false);

        Assert.True(helper.IsSequenceActive);
        Assert.Equal(NINActions.NinjutsuType.Suiton, helper.TargetNinjutsu);
        Assert.Contains("NoActive", context.Debug.NinjutsuState);
    }

    [Fact]
    public void CollectCandidates_MudraAcknowledgeStuck_AbortsAfterFortyFiveFrames()
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(99999UL);

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.GetAdjustedActionId(NINActions.Ninjutsu.ActionId))
            .Returns(NINActions.Ninjutsu.ActionId);
        actionService.Setup(x => x.GetAdjustedActionId(NINActions.Ten.ActionId))
            .Returns(NINActions.Ten.ActionId);
        actionService.Setup(x => x.GetCurrentCharges(NINActions.Ten.ActionId)).Returns(1u);
        actionService.Setup(x => x.WasLastAction(NINActions.Ten.ActionId)).Returns(true);
        actionService.Setup(x => x.GcdRemaining).Returns(0f);

        var helper = new MudraHelper();
        helper.StartSequence(NINActions.NinjutsuType.Raiton);

        var debug = new HermesDebugState();
        var context = HermesTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            mudraHelper: helper,
            hasGameMudraStatus: false,
            canExecuteGcd: true,
            inCombat: true,
            debugState: debug);

        var module = new NinjutsuModule();
        var scheduler = SchedulerFactory.CreateForTest();

        for (var i = 0; i < 44; i++)
        {
            module.CollectCandidates(context, scheduler, isMoving: false);
            Assert.True(helper.IsSequenceActive);
        }

        module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.False(helper.IsSequenceActive);
        Assert.Equal("Aborted: Stuck waiting for mudra step", debug.NinjutsuState);
        Assert.True(debug.NinjutsuAbortCooldownFrames > 0);
    }

    [Fact]
    public void CollectCandidates_AfterAbort_DoesNotImmediatelyRestartNinjutsu()
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(99999UL);

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionReady(NINActions.Ten.ActionId)).Returns(true);
        actionService.Setup(x => x.GetAdjustedActionId(NINActions.Ninjutsu.ActionId))
            .Returns(NINActions.Ninjutsu.ActionId);
        actionService.Setup(x => x.GetAdjustedActionId(NINActions.Ten.ActionId))
            .Returns(NINActions.Ten.ActionId);
        actionService.Setup(x => x.GetCurrentCharges(NINActions.Ten.ActionId)).Returns(1u);
        actionService.Setup(x => x.WasLastAction(NINActions.Ten.ActionId)).Returns(true);

        var helper = new MudraHelper();
        helper.StartSequence(NINActions.NinjutsuType.Raiton);

        var debug = new HermesDebugState();
        var context = HermesTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            mudraHelper: helper,
            canExecuteGcd: true,
            inCombat: true,
            debugState: debug);

        var module = new NinjutsuModule();
        var scheduler = SchedulerFactory.CreateForTest();

        for (var i = 0; i < 45; i++)
            module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.False(helper.IsSequenceActive);
        module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.False(helper.IsSequenceActive);
        Assert.Contains("Backoff after abort", debug.NinjutsuState);
    }

    [Fact]
    public void CollectCandidates_TenOnChargeCd_AbortsSequenceImmediately()
    {
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.GetAdjustedActionId(NINActions.Ninjutsu.ActionId))
            .Returns(NINActions.Ninjutsu.ActionId);
        actionService.Setup(x => x.GetAdjustedActionId(NINActions.Ten.ActionId))
            .Returns(NINActions.Ten.ActionId);
        actionService.Setup(x => x.GetCurrentCharges(NINActions.Ten.ActionId)).Returns(0u);
        actionService.Setup(x => x.GetCooldownRemaining(NINActions.Ten.ActionId)).Returns(20f);
        actionService.Setup(x => x.GcdRemaining).Returns(0f);

        var helper = new MudraHelper();
        helper.StartSequence(NINActions.NinjutsuType.Raiton);

        var debug = new HermesDebugState();
        var context = HermesTestContext.Create(
            actionService: actionService,
            mudraHelper: helper,
            hasGameMudraStatus: false,
            canExecuteGcd: true,
            inCombat: true,
            debugState: debug);

        new NinjutsuModule().CollectCandidates(context, SchedulerFactory.CreateForTest(), isMoving: false);

        Assert.False(helper.IsSequenceActive);
        Assert.Equal("Aborted: Ten on charge CD — combo filler", debug.NinjutsuState);
    }

    [Fact]
    public void CollectCandidates_MidSequenceSlotDesync_AbortsImmediately()
    {
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.GetAdjustedActionId(NINActions.Ninjutsu.ActionId))
            .Returns(NINActions.Ninjutsu.ActionId);
        actionService.Setup(x => x.GcdRemaining).Returns(0f);
        actionService.Setup(x => x.WasLastAction(NINActions.Ten.ActionId)).Returns(false);
        actionService.Setup(x => x.WasLastAction(NINActions.Chi.ActionId)).Returns(false);
        actionService.Setup(x => x.WasLastAction(NINActions.Jin.ActionId)).Returns(false);

        var helper = new MudraHelper();
        helper.StartSequence(NINActions.NinjutsuType.Suiton);
        helper.NotifyMudraPressed();

        var debug = new HermesDebugState();
        var context = HermesTestContext.Create(
            actionService: actionService,
            mudraHelper: helper,
            hasGameMudraStatus: false,
            canExecuteGcd: true,
            inCombat: true,
            debugState: debug);

        new NinjutsuModule().CollectCandidates(context, SchedulerFactory.CreateForTest(), isMoving: false);

        Assert.False(helper.IsSequenceActive);
        Assert.Equal("Aborted: Mudra slot desync", debug.NinjutsuState);
    }

    [Fact]
    public void CollectCandidates_MidSequencePendingNinjutsuFinish_DoesNotAbortForTenCd()
    {
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.GetAdjustedActionId(NINActions.Ninjutsu.ActionId))
            .Returns(NINActions.Suiton.ActionId);
        actionService.Setup(x => x.CanExecuteActionId(NINActions.Ninjutsu.ActionId)).Returns(false);
        actionService.Setup(x => x.GetCurrentCharges(NINActions.Ten.ActionId)).Returns(0u);
        actionService.Setup(x => x.GetCooldownRemaining(NINActions.Ten.ActionId)).Returns(20f);
        actionService.Setup(x => x.GcdRemaining).Returns(0f);

        var helper = new MudraHelper();
        helper.StartSequence(NINActions.NinjutsuType.Suiton);
        helper.NotifyMudraPressed();
        helper.NotifyMudraPressed();
        helper.NotifyMudraPressed();

        var debug = new HermesDebugState();
        var context = HermesTestContext.Create(
            actionService: actionService,
            mudraHelper: helper,
            hasGameMudraStatus: false,
            canExecuteGcd: true,
            inCombat: true,
            debugState: debug);

        new NinjutsuModule().CollectCandidates(context, SchedulerFactory.CreateForTest(), isMoving: false);

        Assert.True(helper.IsSequenceActive);
        Assert.DoesNotContain("Ten on charge CD", debug.NinjutsuState);
    }

    [Fact]
    public void CollectCandidates_MudraStuck_AbortsAfterFortyFiveUnpressableFrames()
    {
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.GetAdjustedActionId(NINActions.Ninjutsu.ActionId))
            .Returns(NINActions.FumaShuriken.ActionId);
        actionService.Setup(x => x.GetAdjustedActionId(NINActions.Ten.ActionId))
            .Returns(NINActions.Ten.ActionId);
        actionService.Setup(x => x.GetAdjustedActionId(NINActions.Chi.ActionId))
            .Returns(NINActions.Chi.ActionId);
        actionService.Setup(x => x.GetCurrentCharges(NINActions.Ten.ActionId)).Returns(1u);
        actionService.Setup(x => x.GetCurrentCharges(NINActions.Chi.ActionId)).Returns(1u);
        actionService.Setup(x => x.GetCooldownRemaining(NINActions.Ten.ActionId)).Returns(0f);
        actionService.Setup(x => x.GetCooldownRemaining(NINActions.Chi.ActionId)).Returns(0f);
        actionService.Setup(x => x.GcdRemaining).Returns(0f);
        actionService.Setup(x => x.WasLastAction(NINActions.Chi.ActionId)).Returns(true);

        var helper = new MudraHelper();
        helper.StartSequence(NINActions.NinjutsuType.Suiton);
        helper.NotifyMudraPressed();
        helper.NotifyMudraPressed();

        var debug = new HermesDebugState();
        var context = HermesTestContext.Create(
            actionService: actionService,
            mudraHelper: helper,
            hasGameMudraStatus: false,
            canExecuteGcd: true,
            inCombat: true,
            debugState: debug);

        var module = new NinjutsuModule();
        var scheduler = SchedulerFactory.CreateForTest();

        for (var i = 0; i < 44; i++)
        {
            module.CollectCandidates(context, scheduler, isMoving: false);
            Assert.True(helper.IsSequenceActive);
        }

        module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.False(helper.IsSequenceActive);
        Assert.Equal("Aborted: Stuck waiting for mudra step", debug.NinjutsuState);
    }

    [Fact]
    public void CollectCandidates_MudraStuck_DoesNotAbortWhileGcdRolling()
    {
        var actionService = MockBuilders.CreateMockActionService(canExecuteGcd: false);
        actionService.Setup(x => x.GetAdjustedActionId(NINActions.Ninjutsu.ActionId))
            .Returns(NINActions.Ninjutsu.ActionId);
        actionService.Setup(x => x.GetAdjustedActionId(NINActions.Ten.ActionId))
            .Returns(NINActions.Ten.ActionId);
        actionService.Setup(x => x.GetCurrentCharges(NINActions.Ten.ActionId)).Returns(0u);
        actionService.Setup(x => x.GetCooldownRemaining(NINActions.Ten.ActionId)).Returns(5f);

        var helper = new MudraHelper();
        helper.StartSequence(NINActions.NinjutsuType.Suiton);

        var context = HermesTestContext.Create(
            actionService: actionService,
            mudraHelper: helper,
            hasGameMudraStatus: false,
            canExecuteGcd: false,
            inCombat: true);

        var module = new NinjutsuModule();
        var scheduler = SchedulerFactory.CreateForTest();

        for (var i = 0; i < 20; i++)
            module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.True(helper.IsSequenceActive);
    }

    [Fact]
    public void CollectCandidates_RabbitSlot_AbortsSequence()
    {
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.GetAdjustedActionId(NINActions.Ninjutsu.ActionId))
            .Returns(NINActions.RabbitMedium.ActionId);

        var helper = new MudraHelper();
        helper.StartSequence(NINActions.NinjutsuType.Suiton);

        var debug = new HermesDebugState();
        var context = HermesTestContext.Create(
            actionService: actionService,
            mudraHelper: helper,
            hasGameMudraStatus: false,
            canExecuteGcd: true,
            inCombat: true,
            debugState: debug);

        new NinjutsuModule().CollectCandidates(context, SchedulerFactory.CreateForTest(), isMoving: false);

        Assert.False(helper.IsSequenceActive);
        Assert.Equal("Aborted: Rabbit Medium", debug.NinjutsuState);
    }

    [Fact]
    public void CollectCandidates_RabbitSlot_WithGcdRolling_AbortsImmediately()
    {
        var actionService = MockBuilders.CreateMockActionService(canExecuteGcd: false);
        actionService.Setup(x => x.GetAdjustedActionId(NINActions.Ninjutsu.ActionId))
            .Returns(NINActions.RabbitMedium.ActionId);

        var helper = new MudraHelper();
        helper.StartSequence(NINActions.NinjutsuType.Suiton);

        var debug = new HermesDebugState();
        var context = HermesTestContext.Create(
            actionService: actionService,
            mudraHelper: helper,
            hasGameMudraStatus: true,
            canExecuteGcd: false,
            inCombat: true,
            debugState: debug);

        new NinjutsuModule().CollectCandidates(context, SchedulerFactory.CreateForTest(), isMoving: false);

        Assert.False(helper.IsSequenceActive);
        Assert.Equal("Aborted: Rabbit Medium", debug.NinjutsuState);
    }
}
