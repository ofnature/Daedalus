using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.ApolloCore.Modules.Healing;
using Daedalus.Services.Healing;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.ApolloCore.Modules.Healing;

/// <summary>
/// WHM half of the AoE-emergency regression suite (party reduced to 1 HP — see
/// AoEEmergencyRecoveryTests for the SGE origin story): the emergency must punch through the
/// count gate and the cross-healer AoE reservation, and outrank single-target triage.
/// </summary>
public class ApolloAoEEmergencyTests
{
    private static Mock<Daedalus.Rotation.ApolloCore.Helpers.IPartyHelper> PartyAtOneHp()
    {
        var members = new List<IBattleChara>
        {
            MockBuilders.CreateMockBattleChara(entityId: 2, currentHp: 1, maxHp: 50000).Object,
            MockBuilders.CreateMockBattleChara(entityId: 3, currentHp: 1, maxHp: 50000).Object,
            MockBuilders.CreateMockBattleChara(entityId: 4, currentHp: 1, maxHp: 50000).Object,
        };
        return MockBuilders.CreateMockPartyHelper(partyMembers: members);
    }

    private static Mock<IHealingSpellSelector> SelectorReturningMedica()
    {
        var selector = ApolloTestContext.CreateDefaultHealingSpellSelector();
        selector.Setup(x => x.SelectBestAoEHeal(
                It.IsAny<IPlayerCharacter>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<IBattleChara?>()))
            .Returns((WHMActions.Medica, 5000, (IBattleChara?)null));
        return selector;
    }

    [Fact]
    public void AoEHeal_Emergency_BypassesCountGateAndReservation_PushesEmergencyPriority()
    {
        var partyHelper = PartyAtOneHp(); // CountPartyMembersNeedingAoEHeal mock reports 0 — the count gate would starve
        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: true);

        var context = ApolloTestContext.Create(
            partyHelper: partyHelper,
            actionService: actionService,
            healingSpellSelector: SelectorReturningMedica(),
            level: 90,
            canExecuteGcd: true,
            canExecuteOgcd: true);

        // Another healer's reservation holds the frame — emergency must fire through it.
        Assert.True(context.HealingCoordination.TryReserveAoEHeal(null, 999u, 100, 0));

        var scheduler = SchedulerFactory.CreateForTest(actionService);
        new AoEHealingHandler().CollectCandidates(context, scheduler, isMoving: false);

        var candidate = Assert.Single(
            scheduler.InspectGcdQueue(),
            c => c.Behavior.Action.ActionId == WHMActions.Medica.ActionId);
        Assert.Equal(12, candidate.Priority); // above Assize (15) / Tetragrammaton (25), below Benediction (10)
    }

    [Fact]
    public void AoEHeal_NoEmergency_CountGateStillApplies()
    {
        // Healthy party: no emergency, and the count mock reports 0 injured → the gate holds.
        var members = new List<IBattleChara>
        {
            MockBuilders.CreateMockBattleChara(entityId: 2, currentHp: 50000, maxHp: 50000).Object,
            MockBuilders.CreateMockBattleChara(entityId: 3, currentHp: 50000, maxHp: 50000).Object,
        };
        var partyHelper = MockBuilders.CreateMockPartyHelper(partyMembers: members);
        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: true);

        var context = ApolloTestContext.Create(
            partyHelper: partyHelper,
            actionService: actionService,
            healingSpellSelector: SelectorReturningMedica(),
            level: 90,
            canExecuteGcd: true,
            canExecuteOgcd: true);

        var scheduler = SchedulerFactory.CreateForTest(actionService);
        new AoEHealingHandler().CollectCandidates(context, scheduler, isMoving: false);

        Assert.Empty(scheduler.InspectGcdQueue());
    }
}
