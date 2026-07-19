using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using Moq;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.AsclepiusCore.Modules;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.AsclepiusCore.Modules;

/// <summary>
/// Regression suite (2026-07-19 field report — WAR/SGE/SGE/PCT multibox party): a sage that
/// could not resolve the tank yet (staggered zone-in) immediately fell back to the highest-DPS
/// ally and parked Kardion on the PCT. Pre-pull, an unresolved tank-job entry in the party
/// list must mean "wait for the tank", not "fall back to a DPS". In combat the fallback stays —
/// any bearer beats none.
///
/// The party-list job read goes through <see cref="KardiaModule.PartyMemberJobIdReader"/>
/// because Lumina RowRefs cannot be mocked.
/// </summary>
public class KardiaModuleTankLoadingTests
{
    private static Mock<Daedalus.Services.Action.IActionService> CreateDispatchReadyActionService()
    {
        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: true);
        actionService.Setup(x => x.IsActionReady(SGEActions.Kardia.ActionId)).Returns(true);
        actionService.Setup(x => x.ExecuteOgcd(It.IsAny<ActionDefinition>(), It.IsAny<ulong>()))
            .Returns(true);
        return actionService;
    }

    private static Mock<IPartyMember> PartyMember(uint entityId, IGameObject? gameObject = null)
    {
        var mock = new Mock<IPartyMember>();
        mock.Setup(x => x.EntityId).Returns(entityId);
        mock.Setup(x => x.GameObject).Returns(gameObject);
        return mock;
    }

    private static Mock<IPartyList> PartyList(params IPartyMember[] members)
    {
        var list = new List<IPartyMember>(members);
        var mock = new Mock<IPartyList>();
        mock.Setup(x => x.Length).Returns(list.Count);
        mock.Setup(x => x.GetEnumerator()).Returns(() => list.GetEnumerator());
        return mock;
    }

    /// <summary>
    /// Marks the party entry with the given entity id as the tank job; everyone else reads PCT.
    /// </summary>
    private static KardiaModule ModuleWithTankEntry(uint tankEntityId) => new()
    {
        PartyMemberJobIdReader = m =>
            m.EntityId == tankEntityId ? JobRegistry.Warrior : JobRegistry.Pictomancer,
    };

    [Fact]
    public void PrePull_TankJobInPartyListButUnresolved_WaitsInsteadOfDpsFallback()
    {
        var module = ModuleWithTankEntry(FFXIVConstants.InvalidTargetId);
        var actionService = CreateDispatchReadyActionService();

        // A DPS ally IS available — only the guard, not a lack of candidates, may hold the cast.
        var dps = MockBuilders.CreateMockBattleChara(entityId: 3, currentHp: 50000, maxHp: 50000);
        var partyHelper = MockBuilders.CreateMockPartyHelper(
            partyMembers: new List<IBattleChara> { dps.Object });

        var partyList = PartyList(
            PartyMember(1).Object, // the sage itself
            PartyMember(3).Object, // the PCT
            PartyMember(FFXIVConstants.InvalidTargetId).Object); // WAR still loading

        var context = AsclepiusTestContext.Create(
            partyHelper: partyHelper,
            actionService: actionService,
            level: 100,
            inCombat: false,
            canExecuteOgcd: true,
            partyList: partyList);

        module.CollectCandidates(context, SchedulerFactory.CreateForTest(actionService), isMoving: false);

        actionService.Verify(
            x => x.ExecuteOgcd(
                It.Is<ActionDefinition>(a => a.ActionId == SGEActions.Kardia.ActionId),
                It.IsAny<ulong>()),
            Times.Never);
        Assert.Equal("Waiting (tank loading)", context.Debug.KardiaState);
    }

    [Fact]
    public void InCombat_TankJobUnresolved_StillFallsBackToDps()
    {
        var module = ModuleWithTankEntry(FFXIVConstants.InvalidTargetId);
        var actionService = CreateDispatchReadyActionService();

        var dps = MockBuilders.CreateMockBattleChara(entityId: 3, currentHp: 50000, maxHp: 50000);
        var partyHelper = MockBuilders.CreateMockPartyHelper(
            partyMembers: new List<IBattleChara> { dps.Object });

        var partyList = PartyList(
            PartyMember(1).Object,
            PartyMember(3).Object,
            PartyMember(FFXIVConstants.InvalidTargetId).Object);

        var context = AsclepiusTestContext.Create(
            partyHelper: partyHelper,
            actionService: actionService,
            level: 100,
            inCombat: true,
            canExecuteOgcd: true,
            partyList: partyList);

        module.CollectCandidates(context, SchedulerFactory.CreateForTest(actionService), isMoving: false);

        actionService.Verify(
            x => x.ExecuteOgcd(
                It.Is<ActionDefinition>(a => a.ActionId == SGEActions.Kardia.ActionId),
                dps.Object.GameObjectId),
            Times.Once);
    }

    [Fact]
    public void PrePull_TankEntryResolvable_GuardDoesNotFire_FallbackProceeds()
    {
        // The tank entry resolves to a live object (the "tankless" outcome is then the party
        // helper's verdict, e.g. a dead tank) — the loading guard must not deadlock placement.
        var tankObject = MockBuilders.CreateMockBattleChara(entityId: 4, currentHp: 0, maxHp: 50000, isDead: true);
        var module = ModuleWithTankEntry(4);
        var actionService = CreateDispatchReadyActionService();

        var dps = MockBuilders.CreateMockBattleChara(entityId: 3, currentHp: 50000, maxHp: 50000);
        var partyHelper = MockBuilders.CreateMockPartyHelper(
            partyMembers: new List<IBattleChara> { dps.Object });

        var partyList = PartyList(
            PartyMember(1).Object,
            PartyMember(3).Object,
            PartyMember(4, tankObject.Object).Object);

        var context = AsclepiusTestContext.Create(
            partyHelper: partyHelper,
            actionService: actionService,
            level: 100,
            inCombat: false,
            canExecuteOgcd: true,
            partyList: partyList);

        module.CollectCandidates(context, SchedulerFactory.CreateForTest(actionService), isMoving: false);

        actionService.Verify(
            x => x.ExecuteOgcd(
                It.Is<ActionDefinition>(a => a.ActionId == SGEActions.Kardia.ActionId),
                dps.Object.GameObjectId),
            Times.Once);
    }

    [Fact]
    public void PrePull_NoTankJobInPartyList_FallbackProceeds()
    {
        // Genuinely tankless party (e.g. 4x DPS/healer) — behavior unchanged.
        var module = ModuleWithTankEntry(tankEntityId: 999); // no entry matches → all read PCT
        var actionService = CreateDispatchReadyActionService();

        var dps = MockBuilders.CreateMockBattleChara(entityId: 3, currentHp: 50000, maxHp: 50000);
        var partyHelper = MockBuilders.CreateMockPartyHelper(
            partyMembers: new List<IBattleChara> { dps.Object });

        var partyList = PartyList(
            PartyMember(1).Object,
            PartyMember(3).Object);

        var context = AsclepiusTestContext.Create(
            partyHelper: partyHelper,
            actionService: actionService,
            level: 100,
            inCombat: false,
            canExecuteOgcd: true,
            partyList: partyList);

        module.CollectCandidates(context, SchedulerFactory.CreateForTest(actionService), isMoving: false);

        actionService.Verify(
            x => x.ExecuteOgcd(
                It.Is<ActionDefinition>(a => a.ActionId == SGEActions.Kardia.ActionId),
                dps.Object.GameObjectId),
            Times.Once);
    }
}
