using Moq;
using Olympus.Data;
using Olympus.Models.Action;
using Olympus.Rotation.AsclepiusCore.Modules.Healing;
using Olympus.Tests.Mocks;
using Olympus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Olympus.Tests.Rotation.AsclepiusCore.Modules.Healing;

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

        var partyHelper = new Mock<Olympus.Rotation.ApolloCore.Helpers.IPartyHelper>();
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

        var partyHelper = new Mock<Olympus.Rotation.ApolloCore.Helpers.IPartyHelper>();
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

        var partyHelper = new Mock<Olympus.Rotation.ApolloCore.Helpers.IPartyHelper>();
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
}
