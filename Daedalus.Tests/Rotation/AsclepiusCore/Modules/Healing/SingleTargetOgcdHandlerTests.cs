using Moq;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.AsclepiusCore.Modules.Healing;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.AsclepiusCore.Modules.Healing;

public class SingleTargetOgcdHandlerTests
{
    private readonly SingleTargetOgcdHandler _handler = new();

    [Fact]
    public void CollectCandidates_TankEmergencyWithReservedAddersgall_PushesDruochole()
    {
        var config = AsclepiusTestContext.CreateDefaultSageConfiguration();
        config.Sage.EnableDruochole = true;
        config.Sage.AddersgallReserve = 1;
        config.Sage.DruocholeThreshold = 0.55f;
        config.Healing.OgcdEmergencyThreshold = 0.50f;

        var tank = MockBuilders.CreateMockBattleChara(entityId: 1u, currentHp: 55000, maxHp: 153000);
        tank.Setup(x => x.GameObjectId).Returns(0xDEAD0001ul);

        var partyHelper = new Mock<Daedalus.Rotation.ApolloCore.Helpers.IPartyHelper>();
        partyHelper.Setup(p => p.FindTankInParty(It.IsAny<Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter>()))
            .Returns(tank.Object);
        partyHelper.Setup(p => p.FindLowestHpPartyMember(It.IsAny<Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter>()))
            .Returns(tank.Object);

        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: true);

        var context = AsclepiusTestContext.Create(
            config: config,
            partyHelper: partyHelper,
            actionService: actionService,
            level: 100,
            inCombat: true,
            canExecuteOgcd: true,
            addersgallStacks: 1);

        var scheduler = SchedulerFactory.CreateForTest(actionService);

        _handler.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(
            scheduler.InspectOgcdQueue(),
            c => c.Behavior.Action.ActionId == SGEActions.Druochole.ActionId);
    }

    [Fact]
    public void CollectCandidates_TankEmergency_PushesTaurochole()
    {
        var config = AsclepiusTestContext.CreateDefaultSageConfiguration();
        config.Sage.EnableTaurochole = true;
        config.Sage.TaurocholeThreshold = 0.55f;
        config.Healing.OgcdEmergencyThreshold = 0.50f;

        var tank = MockBuilders.CreateMockBattleChara(entityId: 1u, currentHp: 55000, maxHp: 153000);
        tank.Setup(x => x.GameObjectId).Returns(0xDEAD0001ul);

        var partyHelper = new Mock<Daedalus.Rotation.ApolloCore.Helpers.IPartyHelper>();
        partyHelper.Setup(p => p.FindTankInParty(It.IsAny<Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter>()))
            .Returns(tank.Object);

        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: true);
        actionService.Setup(x => x.IsActionReady(SGEActions.Taurochole.ActionId)).Returns(true);

        var context = AsclepiusTestContext.Create(
            config: config,
            partyHelper: partyHelper,
            actionService: actionService,
            level: 100,
            inCombat: true,
            canExecuteOgcd: true,
            addersgallStacks: 1);

        var scheduler = SchedulerFactory.CreateForTest(actionService);

        _handler.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(
            scheduler.InspectOgcdQueue(),
            c => c.Behavior.Action.ActionId == SGEActions.Taurochole.ActionId);
    }

    [Fact]
    public void CollectCandidates_AddersgallHardCapWithFullReserve_PushesCapDumpDruochole()
    {
        var config = AsclepiusTestContext.CreateDefaultSageConfiguration();
        config.Sage.EnableDruochole = true;
        config.Sage.PreventAddersgallCap = true;
        config.Sage.AddersgallReserve = 3;
        config.Sage.DruocholeThreshold = 0.55f;

        var tank = MockBuilders.CreateMockBattleChara(entityId: 1u, currentHp: 153000, maxHp: 153000);
        tank.Setup(x => x.GameObjectId).Returns(0xDEAD0001ul);

        var partyHelper = new Mock<Daedalus.Rotation.ApolloCore.Helpers.IPartyHelper>();
        partyHelper.Setup(p => p.FindTankInParty(It.IsAny<Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter>()))
            .Returns(tank.Object);
        partyHelper.Setup(p => p.FindLowestHpPartyMember(It.IsAny<Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter>()))
            .Returns(tank.Object);

        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: true);
        var addersgallService = AsclepiusTestContext.CreateMockAddersgallService(currentStacks: 3, timerRemaining: 0f);

        var context = AsclepiusTestContext.Create(
            config: config,
            partyHelper: partyHelper,
            actionService: actionService,
            addersgallService: addersgallService,
            level: 100,
            inCombat: true,
            canExecuteOgcd: true,
            addersgallStacks: 3);

        var scheduler = SchedulerFactory.CreateForTest(actionService);

        _handler.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(
            scheduler.InspectOgcdQueue(),
            c => c.Behavior.Action.ActionId == SGEActions.Druochole.ActionId);
    }

    /// <summary>
    /// Druochole's 7% MP refund is the point of Addersgall, and nothing used to spend a stack
    /// for it. Field 2026-08-10: raising repeatedly in the Occult Crescent bottoms a Sage out
    /// near 200 MP while stacks sit unused, because a raise costs ~2,400 MP and Lucid only
    /// returns 3,850 a minute.
    /// </summary>
    [Fact]
    public void CollectCandidates_LowMpWithSurplusAddersgall_HarvestsDruocholeOnHealthyParty()
    {
        var scheduler = RunMpHarvest(
            mp: 200, stacks: 3, reserve: 1, harvestEnabled: true, out _);

        Assert.Contains(
            scheduler.InspectOgcdQueue(),
            c => c.Behavior.Action.ActionId == SGEActions.Druochole.ActionId);
    }

    /// <summary>The reserve is for the tank's emergency — never trade it for mana.</summary>
    [Fact]
    public void CollectCandidates_LowMpButOnlyReservedStacks_DoesNotHarvest()
    {
        var scheduler = RunMpHarvest(
            mp: 200, stacks: 3, reserve: 3, harvestEnabled: true, out var context);

        Assert.DoesNotContain(
            scheduler.InspectOgcdQueue(),
            c => c.Behavior.Action.ActionId == SGEActions.Druochole.ActionId);
        Assert.Contains("Reserved", context.Debug.DruocholeState);
    }

    [Fact]
    public void CollectCandidates_HealthyMpAndHealthyParty_DoesNotHarvest()
    {
        var scheduler = RunMpHarvest(
            mp: 10000, stacks: 3, reserve: 1, harvestEnabled: true, out _);

        Assert.DoesNotContain(
            scheduler.InspectOgcdQueue(),
            c => c.Behavior.Action.ActionId == SGEActions.Druochole.ActionId);
    }

    [Fact]
    public void CollectCandidates_HarvestDisabled_LeavesTheMpOnTheFloor()
    {
        var scheduler = RunMpHarvest(
            mp: 200, stacks: 3, reserve: 1, harvestEnabled: false, out _);

        Assert.DoesNotContain(
            scheduler.InspectOgcdQueue(),
            c => c.Behavior.Action.ActionId == SGEActions.Druochole.ActionId);
    }

    /// <summary>
    /// Shared setup: a party at FULL HP (so only the MP path can push Druochole) with cap
    /// prevention off (so only the MP path, not the cap dump, can bypass the HP gate).
    /// </summary>
    private Daedalus.Rotation.Common.Scheduling.RotationScheduler RunMpHarvest(
        uint mp, int stacks, int reserve, bool harvestEnabled,
        out Daedalus.Rotation.AsclepiusCore.Context.IAsclepiusContext context)
    {
        var config = AsclepiusTestContext.CreateDefaultSageConfiguration();
        config.Sage.EnableDruochole = true;
        config.Sage.PreventAddersgallCap = false; // isolate the MP path from the cap dump
        config.Sage.HarvestAddersgallForMp = harvestEnabled;
        config.Sage.AddersgallMpThreshold = 0.60f;
        config.Sage.AddersgallReserve = reserve;
        config.Sage.DruocholeThreshold = 0.55f;

        var ally = MockBuilders.CreateMockBattleChara(entityId: 1u, currentHp: 153000, maxHp: 153000);
        ally.Setup(x => x.GameObjectId).Returns(0xDEAD0001ul);

        var partyHelper = new Mock<Daedalus.Rotation.ApolloCore.Helpers.IPartyHelper>();
        partyHelper.Setup(p => p.FindTankInParty(It.IsAny<Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter>()))
            .Returns(ally.Object);
        partyHelper.Setup(p => p.FindLowestHpPartyMember(It.IsAny<Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter>()))
            .Returns(ally.Object);

        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: true);
        var addersgallService = AsclepiusTestContext.CreateMockAddersgallService(
            currentStacks: stacks, timerRemaining: 15f);

        context = AsclepiusTestContext.Create(
            config: config,
            partyHelper: partyHelper,
            actionService: actionService,
            addersgallService: addersgallService,
            level: 100,
            currentMp: mp,
            inCombat: true,
            canExecuteOgcd: true,
            addersgallStacks: stacks);

        var scheduler = SchedulerFactory.CreateForTest(actionService);
        _handler.CollectCandidates(context, scheduler, isMoving: false);
        return scheduler;
    }
}
