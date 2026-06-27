using Moq;
using Daedalus.Data;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.HephaestusCore.Abilities;
using Daedalus.Rotation.HephaestusCore.Context;
using Daedalus.Rotation.HephaestusCore.Modules;
using Daedalus.Services;
using Daedalus.Services.Action;
using Daedalus.Services.Training;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;

namespace Daedalus.Tests.Rotation.HephaestusCore.Modules;


public class BuffModuleCollectCandidatesTests
{
    [Fact]
    public void CollectCandidates_NotInCombat_PushesNothing()
    {
        var module = new BuffModule();
        var scheduler = SchedulerFactory.CreateForTest();
        var context = CreateContext(inCombat: false);

        module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Empty(scheduler.InspectOgcdQueue());
        Assert.Equal("Not in combat", context.Debug.BuffState);
    }

    [Fact]
    public void CollectCandidates_NoMercy_PushedAtPriority1_WhenReadyAndInBurstWindow()
    {
        // Burst window active (IsInBurstWindow=true) so ShouldHoldForBurst returns false
        var burstService = new Mock<IBurstWindowService>();
        burstService.Setup(x => x.IsInBurstWindow).Returns(true);
        burstService.Setup(x => x.IsBurstImminent(It.IsAny<float>())).Returns(true);

        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: false);
        actionService.Setup(x => x.IsActionReady(GNBActions.NoMercy.ActionId)).Returns(true);
        actionService.Setup(x => x.IsActionReady(GNBActions.Bloodfest.ActionId)).Returns(false);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = CreateContext(
            inCombat: true,
            hasNoMercy: false,
            cartridges: 2,
            actionService: actionService);

        new BuffModule(burstService.Object).CollectCandidates(context, scheduler, isMoving: false);

        var queue = scheduler.InspectOgcdQueue();
        Assert.Single(queue);
        Assert.Equal(GnbAbilities.NoMercy, queue[0].Behavior);
        Assert.Equal(1, queue[0].Priority);
    }

    [Fact]
    public void CollectCandidates_NoMercy_NotPushed_WhenShouldHoldForBurst()
    {
        // Imminent but not yet active: ShouldHoldForBurst returns true -> should not push.
        // cartridges=2 also disables Bloodfest (maxBenefit < 2), so only NoMercy runs.
        var burstService = new Mock<IBurstWindowService>();
        burstService.Setup(x => x.IsInBurstWindow).Returns(false);
        burstService.Setup(x => x.IsBurstImminent(It.IsAny<float>())).Returns(true);

        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: false);
        actionService.Setup(x => x.IsActionReady(GNBActions.NoMercy.ActionId)).Returns(true);
        // Bloodfest also blocked so debug state is stable after NoMercy sets it
        actionService.Setup(x => x.IsActionReady(GNBActions.Bloodfest.ActionId)).Returns(false);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        // cartridges=2: Bloodfest maxBenefit=1 < 2 so it sets a different debug string.
        // Use cartridges=0: Bloodfest maxBenefit=3 passes cap check, but NoMercy-hold gate
        // fires first; then Bloodfest IsActionReady returns false so it doesn't set any state.
        var context = CreateContext(
            inCombat: true,
            hasNoMercy: false,
            cartridges: 2,
            actionService: actionService);

        new BuffModule(burstService.Object).CollectCandidates(context, scheduler, isMoving: false);

        // Nothing pushed to the queue
        Assert.Empty(scheduler.InspectOgcdQueue());
    }

    [Fact]
    public void CollectCandidates_Bloodfest_PushedAtPriority2_WhenLowCartridgesAndNoMercyActive()
    {
        // HasNoMercy=true, cartridges=0 -> maxBenefit=3 >= 2, NM hold bypass skipped
        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: false);
        actionService.Setup(x => x.IsActionReady(GNBActions.NoMercy.ActionId)).Returns(false);
        actionService.Setup(x => x.IsActionReady(GNBActions.Bloodfest.ActionId)).Returns(true);
        actionService.Setup(x => x.GetCooldownRemaining(GNBActions.NoMercy.ActionId)).Returns(0f);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = CreateContext(
            inCombat: true,
            hasNoMercy: true,
            cartridges: 0,
            actionService: actionService);

        new BuffModule().CollectCandidates(context, scheduler, isMoving: false);

        var queue = scheduler.InspectOgcdQueue();
        Assert.Single(queue);
        Assert.Equal(GnbAbilities.Bloodfest, queue[0].Behavior);
        Assert.Equal(2, queue[0].Priority);
    }

    [Fact]
    public void CollectCandidates_Bloodfest_PushedAtFullCartridges_WhenNoMercyActive_7_4()
    {
        // 7.4 regression: Bloodfest can no longer overcap (temporary 6-cart cap) so it must fire
        // inside No Mercy even at 3/3 cartridges. The old maxBenefit < 2 gate wrongly blocked this.
        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: false);
        actionService.Setup(x => x.IsActionReady(GNBActions.NoMercy.ActionId)).Returns(false);
        actionService.Setup(x => x.IsActionReady(GNBActions.Bloodfest.ActionId)).Returns(true);
        actionService.Setup(x => x.GetCooldownRemaining(GNBActions.NoMercy.ActionId)).Returns(0f);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = CreateContext(
            inCombat: true,
            hasNoMercy: true,
            cartridges: 3,
            actionService: actionService);

        new BuffModule().CollectCandidates(context, scheduler, isMoving: false);

        var queue = scheduler.InspectOgcdQueue();
        Assert.Single(queue);
        Assert.Equal(GnbAbilities.Bloodfest, queue[0].Behavior);
        Assert.Equal(2, queue[0].Priority);
    }

    [Fact]
    public void CollectCandidates_Bloodfest_HeldWhenNoMercyFarOnCooldown_7_4()
    {
        // Bloodfest should wait for No Mercy (held) rather than dumping Ready to Reign outside the
        // burst window when No Mercy is still far away on cooldown.
        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: false);
        actionService.Setup(x => x.IsActionReady(GNBActions.NoMercy.ActionId)).Returns(false);
        actionService.Setup(x => x.IsActionReady(GNBActions.Bloodfest.ActionId)).Returns(true);
        actionService.Setup(x => x.GetCooldownRemaining(GNBActions.NoMercy.ActionId)).Returns(25f);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = CreateContext(
            inCombat: true,
            hasNoMercy: false,
            cartridges: 0,
            actionService: actionService);

        new BuffModule().CollectCandidates(context, scheduler, isMoving: false);

        Assert.Empty(scheduler.InspectOgcdQueue());
    }

    [Fact]
    public void CollectCandidates_Bloodfest_NotPushed_WhenNoEnemyTarget()
    {
        // Regression: Bloodfest is flagged CanTargetSelf=false / CanTargetHostile=true. It was being
        // dispatched at player.GameObjectId, which UseAction silently no-ops (perpetually "ready, never
        // casts"). With no enemy target it must not be pushed at all.
        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: false);
        actionService.Setup(x => x.IsActionReady(GNBActions.NoMercy.ActionId)).Returns(false);
        actionService.Setup(x => x.IsActionReady(GNBActions.Bloodfest.ActionId)).Returns(true);
        actionService.Setup(x => x.GetCooldownRemaining(GNBActions.NoMercy.ActionId)).Returns(0f);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = CreateContext(
            inCombat: true,
            hasNoMercy: true,
            cartridges: 0,
            actionService: actionService,
            hasTarget: false);

        new BuffModule().CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == GnbAbilities.Bloodfest);
    }

    [Fact]
    public void CollectCandidates_Bloodfest_DispatchedAtEnemyTarget_NotSelf()
    {
        // Bloodfest must be dispatched at the hostile target's id, not the player's, or the game refuses it.
        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: false);
        actionService.Setup(x => x.IsActionReady(GNBActions.NoMercy.ActionId)).Returns(false);
        actionService.Setup(x => x.IsActionReady(GNBActions.Bloodfest.ActionId)).Returns(true);
        actionService.Setup(x => x.GetCooldownRemaining(GNBActions.NoMercy.ActionId)).Returns(0f);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = CreateContext(
            inCombat: true,
            hasNoMercy: true,
            cartridges: 0,
            actionService: actionService);

        new BuffModule().CollectCandidates(context, scheduler, isMoving: false);

        var bloodfest = Assert.Single(scheduler.InspectOgcdQueue(), c => c.Behavior == GnbAbilities.Bloodfest);
        Assert.NotEqual(context.Player.GameObjectId, bloodfest.TargetId);
        Assert.Equal(context.CurrentTarget!.GameObjectId, bloodfest.TargetId);
    }

    [Fact]
    public void CollectCandidates_EnableNoMercyFalse_PushesNothing()
    {
        var config = HephaestusTestContext.CreateDefaultGunbreakerConfiguration();
        config.Tank.EnableNoMercy = false;
        config.Tank.EnableBloodfest = false;

        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: false);
        actionService.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = CreateContext(
            inCombat: true,
            hasNoMercy: false,
            cartridges: 2,
            config: config,
            actionService: actionService);

        new BuffModule().CollectCandidates(context, scheduler, isMoving: false);

        Assert.Empty(scheduler.InspectOgcdQueue());
    }

    [Fact]
    public void CollectCandidates_RoyalGuard_PushedWhenStanceMissing()
    {
        // Regression: GNB BuffModule previously never pushed tank stance at all. Royal Guard must be
        // pushed in combat when the stance isn't up and AutoTankStance is enabled.
        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: false);
        actionService.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = CreateContext(
            inCombat: true,
            hasRoyalGuard: false,
            actionService: actionService);

        new BuffModule().CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == GnbAbilities.RoyalGuard);
    }

    [Fact]
    public void CollectCandidates_RoyalGuard_NotPushedWhenStanceActive()
    {
        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: false);
        actionService.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = CreateContext(
            inCombat: true,
            hasRoyalGuard: true,
            actionService: actionService);

        new BuffModule().CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == GnbAbilities.RoyalGuard);
    }

    #region Helpers

    private static IHephaestusContext CreateContext(
        bool inCombat,
        bool hasNoMercy = false,
        float noMercyRemaining = 0f,
        int cartridges = 0,
        byte level = 100,
        Configuration? config = null,
        Mock<IActionService>? actionService = null,
        bool hasRoyalGuard = true,
        bool hasTarget = true)
    {
        config ??= HephaestusTestContext.CreateDefaultGunbreakerConfiguration();
        actionService ??= MockBuilders.CreateMockActionService(canExecuteOgcd: false);
        actionService.Setup(x => x.GetCooldownRemaining(GNBActions.NoMercy.ActionId)).Returns(20f);

        var player = MockBuilders.CreateMockPlayerCharacter(level: level);
        player.Setup(x => x.StatusList).Returns((Dalamud.Game.ClientState.Statuses.StatusList?)null!);

        var mock = new Mock<IHephaestusContext>();
        mock.Setup(x => x.Player).Returns(player.Object);
        mock.Setup(x => x.InCombat).Returns(inCombat);
        mock.Setup(x => x.CanExecuteGcd).Returns(true);
        mock.Setup(x => x.CanExecuteOgcd).Returns(false);
        mock.Setup(x => x.Configuration).Returns(config);
        mock.Setup(x => x.ActionService).Returns(actionService.Object);
        mock.Setup(x => x.TrainingService).Returns((ITrainingService?)null);
        mock.Setup(x => x.HasNoMercy).Returns(hasNoMercy);
        mock.Setup(x => x.NoMercyRemaining).Returns(noMercyRemaining);
        mock.Setup(x => x.Cartridges).Returns(cartridges);
        mock.Setup(x => x.HasRoyalGuard).Returns(hasRoyalGuard);
        // Bloodfest is CanTargetHostile (not self-targetable), so it requires a CurrentTarget to dispatch.
        mock.Setup(x => x.CurrentTarget).Returns(
            hasTarget ? MockBuilders.CreateMockBattleChara(entityId: 0x40000001).Object : null);
        mock.Setup(x => x.Debug).Returns(new HephaestusDebugState());

        return mock.Object;
    }

    #endregion
}
