using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using Moq;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.ApolloCore.Helpers;
using Daedalus.Rotation.AsclepiusCore.Modules;
using Daedalus.Services.Sage;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.AsclepiusCore.Modules;

/// <summary>
/// Smart Kardia placement: no-tank fallback ordering and the ShouldSwapKardia wiring
/// (swap to an injured ally, hold on cooldown/disabled, return home on recovery).
///
/// NOT unit-testable here (mock limitation, same as the AST card-targeting note):
/// - the DPS-role filter in the fallback — ClassJob is unmockable, every mock ally reads job 0
///   (counted as DPS), so healer exclusion can't be asserted;
/// - parser-ranked "highest DPS" — Name.TextValue is unmockable, so parse rows can't be matched
///   to allies. The deterministic first-ally ordering (all DPS at 0) and the self fallback ARE
///   covered, which is the same code path.
/// </summary>
public class KardiaModuleSmartSwapTests
{
    private readonly KardiaModule _module = new();

    private static Mock<Daedalus.Services.Action.IActionService> CreateDispatchReadyActionService()
    {
        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: true);
        actionService.Setup(x => x.IsActionReady(SGEActions.Kardia.ActionId)).Returns(true);
        actionService.Setup(x => x.ExecuteOgcd(It.IsAny<ActionDefinition>(), It.IsAny<ulong>()))
            .Returns(true);
        return actionService;
    }

    private static Mock<IKardiaManager> CreatePermissiveKardiaManager(
        bool hasKardia,
        ulong kardiaTargetId,
        bool canSwap)
    {
        var mock = new Mock<IKardiaManager>();
        mock.Setup(x => x.HasKardia).Returns(hasKardia);
        mock.Setup(x => x.CurrentKardiaTarget).Returns(kardiaTargetId);
        mock.Setup(x => x.CanSwapKardia).Returns(canSwap);
        mock.Setup(x => x.IsPostZoneWarmupActive).Returns(false);
        return mock;
    }

    private static Mock<IPartyHelper> CreateParty(IBattleChara? tank, params IBattleChara[] allMembers)
    {
        var partyHelper = new Mock<IPartyHelper>();
        partyHelper.Setup(p => p.FindTankInParty(It.IsAny<IPlayerCharacter>())).Returns(tank);
        partyHelper.Setup(p => p.GetAllPartyMembers(It.IsAny<IPlayerCharacter>(), It.IsAny<bool>()))
            .Returns(allMembers);
        return partyHelper;
    }

    [Fact]
    public void NoTank_PlacesOnFirstDpsAlly()
    {
        var allyA = MockBuilders.CreateMockBattleChara(entityId: 10u, currentHp: 50000, maxHp: 50000);
        var allyB = MockBuilders.CreateMockBattleChara(entityId: 20u, currentHp: 50000, maxHp: 50000);
        var partyHelper = CreateParty(tank: null, allyA.Object, allyB.Object);
        var actionService = CreateDispatchReadyActionService();
        var kardiaManager = CreatePermissiveKardiaManager(hasKardia: false, kardiaTargetId: 0, canSwap: true);

        var context = AsclepiusTestContext.Create(
            partyHelper: partyHelper,
            actionService: actionService,
            kardiaManager: kardiaManager,
            level: 100,
            inCombat: true,
            canExecuteOgcd: true);

        _module.CollectCandidates(context, SchedulerFactory.CreateForTest(actionService), isMoving: false);

        actionService.Verify(
            x => x.ExecuteOgcd(
                It.Is<ActionDefinition>(a => a.ActionId == SGEActions.Kardia.ActionId),
                allyA.Object.GameObjectId),
            Times.Once);
    }

    [Fact]
    public void NoTank_NoAllies_PlacesOnSelf()
    {
        var partyHelper = CreateParty(tank: null);
        var actionService = CreateDispatchReadyActionService();
        var kardiaManager = CreatePermissiveKardiaManager(hasKardia: false, kardiaTargetId: 0, canSwap: true);

        var context = AsclepiusTestContext.Create(
            partyHelper: partyHelper,
            actionService: actionService,
            kardiaManager: kardiaManager,
            level: 100,
            inCombat: true,
            canExecuteOgcd: true);

        _module.CollectCandidates(context, SchedulerFactory.CreateForTest(actionService), isMoving: false);

        actionService.Verify(
            x => x.ExecuteOgcd(
                It.Is<ActionDefinition>(a => a.ActionId == SGEActions.Kardia.ActionId),
                context.Player.GameObjectId),
            Times.Once);
    }

    [Fact]
    public void BearerHealthy_AllyInjured_SwapsToInjuredAlly()
    {
        var tank = MockBuilders.CreateMockBattleChara(entityId: 1u, currentHp: 100000, maxHp: 100000);
        var injured = MockBuilders.CreateMockBattleChara(entityId: 20u, currentHp: 20000, maxHp: 50000); // 40%
        var partyHelper = CreateParty(tank.Object, tank.Object, injured.Object);
        var actionService = CreateDispatchReadyActionService();

        var kardiaManager = CreatePermissiveKardiaManager(
            hasKardia: true, kardiaTargetId: tank.Object.GameObjectId, canSwap: true);
        kardiaManager
            .Setup(x => x.ShouldSwapKardia(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>(), false))
            .Returns(true);
        kardiaManager
            .Setup(x => x.IsKardionOnTarget(
                It.IsAny<IPlayerCharacter>(), It.IsAny<IBattleChara>(),
                It.IsAny<IObjectTable>(), It.IsAny<IPartyList>(), It.IsAny<IBattleChara?>()))
            .Returns(false);

        var context = AsclepiusTestContext.Create(
            partyHelper: partyHelper,
            actionService: actionService,
            kardiaManager: kardiaManager,
            level: 100,
            inCombat: true,
            canExecuteOgcd: true);

        _module.CollectCandidates(context, SchedulerFactory.CreateForTest(actionService), isMoving: false);

        actionService.Verify(
            x => x.ExecuteOgcd(
                It.Is<ActionDefinition>(a => a.ActionId == SGEActions.Kardia.ActionId),
                injured.Object.GameObjectId),
            Times.Once);
        kardiaManager.Verify(x => x.RecordSwap(injured.Object.GameObjectId, injured.Object.EntityId), Times.Once);
        // The NEW bearer must be latched so the next frame suppresses recasts on it.
        kardiaManager.Verify(
            x => x.ConfirmTankKardion(It.Is<IBattleChara>(c => c.EntityId == injured.Object.EntityId)),
            Times.Once);
    }

    [Fact]
    public void SwapDisabled_HoldsOnCurrentBearer()
    {
        var tank = MockBuilders.CreateMockBattleChara(entityId: 1u, currentHp: 100000, maxHp: 100000);
        var injured = MockBuilders.CreateMockBattleChara(entityId: 20u, currentHp: 20000, maxHp: 50000);
        var partyHelper = CreateParty(tank.Object, tank.Object, injured.Object);
        var actionService = CreateDispatchReadyActionService();

        var config = AsclepiusTestContext.CreateDefaultSageConfiguration();
        config.Sage.KardiaSwapEnabled = false;

        var kardiaManager = CreatePermissiveKardiaManager(
            hasKardia: true, kardiaTargetId: tank.Object.GameObjectId, canSwap: true);
        // Even a would-swap rule must never be consulted when the setting is off.
        kardiaManager
            .Setup(x => x.ShouldSwapKardia(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<bool>()))
            .Returns(true);
        kardiaManager.Setup(x => x.IsTankKardionLatched(tank.Object.EntityId)).Returns(true);

        var context = AsclepiusTestContext.Create(
            config: config,
            partyHelper: partyHelper,
            actionService: actionService,
            kardiaManager: kardiaManager,
            level: 100,
            inCombat: true,
            canExecuteOgcd: true);

        _module.CollectCandidates(context, SchedulerFactory.CreateForTest(actionService), isMoving: false);

        actionService.Verify(
            x => x.ExecuteOgcd(It.Is<ActionDefinition>(a => a.ActionId == SGEActions.Kardia.ActionId), It.IsAny<ulong>()),
            Times.Never);
    }

    [Fact]
    public void SwapOnCooldown_HoldsOnCurrentBearer()
    {
        var tank = MockBuilders.CreateMockBattleChara(entityId: 1u, currentHp: 100000, maxHp: 100000);
        var injured = MockBuilders.CreateMockBattleChara(entityId: 20u, currentHp: 20000, maxHp: 50000);
        var partyHelper = CreateParty(tank.Object, tank.Object, injured.Object);
        var actionService = CreateDispatchReadyActionService();

        var kardiaManager = CreatePermissiveKardiaManager(
            hasKardia: true, kardiaTargetId: tank.Object.GameObjectId, canSwap: false);
        kardiaManager
            .Setup(x => x.ShouldSwapKardia(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<bool>()))
            .Returns(true);
        kardiaManager.Setup(x => x.IsTankKardionLatched(tank.Object.EntityId)).Returns(true);

        var context = AsclepiusTestContext.Create(
            partyHelper: partyHelper,
            actionService: actionService,
            kardiaManager: kardiaManager,
            level: 100,
            inCombat: true,
            canExecuteOgcd: true);

        _module.CollectCandidates(context, SchedulerFactory.CreateForTest(actionService), isMoving: false);

        actionService.Verify(
            x => x.ExecuteOgcd(It.Is<ActionDefinition>(a => a.ActionId == SGEActions.Kardia.ActionId), It.IsAny<ulong>()),
            Times.Never);
    }

    [Fact]
    public void OffTankBearerRecovered_SwapsBackToTank()
    {
        var tank = MockBuilders.CreateMockBattleChara(entityId: 1u, currentHp: 100000, maxHp: 100000);
        var recovered = MockBuilders.CreateMockBattleChara(entityId: 20u, currentHp: 45000, maxHp: 50000); // 90%
        var partyHelper = CreateParty(tank.Object, tank.Object, recovered.Object);
        var actionService = CreateDispatchReadyActionService();

        var kardiaManager = CreatePermissiveKardiaManager(
            hasKardia: true, kardiaTargetId: recovered.Object.GameObjectId, canSwap: true);
        kardiaManager
            .Setup(x => x.ShouldSwapKardia(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>(), false))
            .Returns(false); // nobody injured
        kardiaManager
            .Setup(x => x.ShouldSwapKardia(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>(), true))
            .Returns(true);  // bearer recovered → return home
        kardiaManager
            .Setup(x => x.IsKardionOnTarget(
                It.IsAny<IPlayerCharacter>(), It.IsAny<IBattleChara>(),
                It.IsAny<IObjectTable>(), It.IsAny<IPartyList>(), It.IsAny<IBattleChara?>()))
            .Returns(false);

        var context = AsclepiusTestContext.Create(
            partyHelper: partyHelper,
            actionService: actionService,
            kardiaManager: kardiaManager,
            level: 100,
            inCombat: true,
            canExecuteOgcd: true);

        _module.CollectCandidates(context, SchedulerFactory.CreateForTest(actionService), isMoving: false);

        actionService.Verify(
            x => x.ExecuteOgcd(
                It.Is<ActionDefinition>(a => a.ActionId == SGEActions.Kardia.ActionId),
                tank.Object.GameObjectId),
            Times.Once);
        kardiaManager.Verify(
            x => x.ConfirmTankKardion(It.Is<IBattleChara>(c => c.EntityId == tank.Object.EntityId)),
            Times.Once);
    }

    [Fact]
    public void HealthyBearer_NoSwapRule_NoDispatch()
    {
        var tank = MockBuilders.CreateMockBattleChara(entityId: 1u, currentHp: 100000, maxHp: 100000);
        var partyHelper = CreateParty(tank.Object, tank.Object);
        var actionService = CreateDispatchReadyActionService();

        var kardiaManager = CreatePermissiveKardiaManager(
            hasKardia: true, kardiaTargetId: tank.Object.GameObjectId, canSwap: true);
        kardiaManager
            .Setup(x => x.ShouldSwapKardia(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<bool>()))
            .Returns(false);
        kardiaManager.Setup(x => x.IsTankKardionLatched(tank.Object.EntityId)).Returns(true);

        var context = AsclepiusTestContext.Create(
            partyHelper: partyHelper,
            actionService: actionService,
            kardiaManager: kardiaManager,
            level: 100,
            inCombat: true,
            canExecuteOgcd: true);

        _module.CollectCandidates(context, SchedulerFactory.CreateForTest(actionService), isMoving: false);

        actionService.Verify(
            x => x.ExecuteOgcd(It.Is<ActionDefinition>(a => a.ActionId == SGEActions.Kardia.ActionId), It.IsAny<ulong>()),
            Times.Never);
    }
}
