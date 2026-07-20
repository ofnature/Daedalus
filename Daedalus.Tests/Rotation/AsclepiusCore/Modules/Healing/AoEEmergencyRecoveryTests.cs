using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
using Daedalus.Rotation.ApolloCore.Helpers;
using Daedalus.Rotation.AsclepiusCore.Helpers;
using Daedalus.Rotation.AsclepiusCore.Modules.Healing;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.AsclepiusCore.Modules.Healing;

/// <summary>
/// Regression suite (2026-07-19 field report — party reduced to 1 HP, two Sages): group heals
/// didn't flow. The cross-healer AoE reservation serialized recovery (sage A's per-frame
/// re-reservation continuously vetoed sage B's every AoE heal), and single-target triage
/// outranked AoE in the weave order. The AoE-emergency state (2+ members critically low, raw
/// HP) bypasses the min-target/avg-HP gates and the reservation, and pushes AoE heals at
/// priority 5 — above single-target triage (10).
/// </summary>
public class AoEEmergencyRecoveryTests
{
    private static Mock<IBattleChara> Member(uint entityId, uint currentHp, uint maxHp = 50000) =>
        MockBuilders.CreateMockBattleChara(entityId: entityId, currentHp: currentHp, maxHp: maxHp);

    private static Mock<IPartyHelper> PartyAt(params Mock<IBattleChara>[] members)
    {
        var list = new List<IBattleChara>();
        foreach (var m in members)
            list.Add(m.Object);

        var helper = MockBuilders.CreateMockPartyHelper(partyMembers: list);
        helper.Setup(p => p.CalculatePartyHealthMetrics(It.IsAny<IPlayerCharacter>()))
            .Returns((avgHpPercent: 0.60f, lowestHpPercent: 0.30f, injuredCount: 3));
        return helper;
    }

    // -------------------------------------------------------------------------
    // IsAoEEmergency
    // -------------------------------------------------------------------------

    [Fact]
    public void IsAoEEmergency_MultipleCritical_True()
    {
        var config = AsclepiusTestContext.CreateDefaultSageConfiguration();
        var helper = PartyAt(Member(2, 1), Member(3, 1), Member(4, 1));
        var player = MockBuilders.CreateMockPlayerCharacter();

        Assert.True(AsclepiusPartyMetrics.IsAoEEmergency(helper.Object, player.Object, config.Healing));
    }

    [Fact]
    public void IsAoEEmergency_SingleCritical_False()
    {
        var config = AsclepiusTestContext.CreateDefaultSageConfiguration();
        // One member at 1 HP, rest healthy — normal triage territory, not a group emergency.
        var helper = PartyAt(Member(2, 1), Member(3, 50000), Member(4, 50000));
        var player = MockBuilders.CreateMockPlayerCharacter();

        Assert.False(AsclepiusPartyMetrics.IsAoEEmergency(helper.Object, player.Object, config.Healing));
    }

    [Fact]
    public void IsAoEEmergency_DeadMembersDoNotCount()
    {
        var config = AsclepiusTestContext.CreateDefaultSageConfiguration();
        // A corpse is a raise problem, not an AoE heal problem.
        var helper = PartyAt(Member(2, 0), Member(3, 1), Member(4, 50000));
        var player = MockBuilders.CreateMockPlayerCharacter();

        Assert.False(AsclepiusPartyMetrics.IsAoEEmergency(helper.Object, player.Object, config.Healing));
    }

    [Fact]
    public void IsAoEEmergency_ToggleOff_False()
    {
        var config = AsclepiusTestContext.CreateDefaultSageConfiguration();
        config.Healing.ForceGroupHealsInEmergency = false;
        var helper = PartyAt(Member(2, 1), Member(3, 1), Member(4, 1));
        var player = MockBuilders.CreateMockPlayerCharacter();

        Assert.False(AsclepiusPartyMetrics.IsAoEEmergency(helper.Object, player.Object, config.Healing));
    }

    // -------------------------------------------------------------------------
    // Handler behavior under emergency
    // -------------------------------------------------------------------------

    [Fact]
    public void Ixochole_Emergency_BypassesReservation_AndOutranksSingleTargetTriage()
    {
        var handler = new IxocholeHandler();
        var partyHelper = PartyAt(Member(2, 1), Member(3, 1), Member(4, 1));
        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: true);

        var context = AsclepiusTestContext.Create(
            partyHelper: partyHelper,
            actionService: actionService,
            level: 100,
            inCombat: true,
            canExecuteOgcd: true,
            addersgallStacks: 3);

        // Another healer's AoE heal already holds the frame reservation — the exact state that
        // starved sage B in the field. Emergency must fire through it.
        Assert.True(context.HealingCoordination.TryReserveAoEHeal(null, 999u, 100, 0));

        var scheduler = SchedulerFactory.CreateForTest(actionService);
        handler.CollectCandidates(context, scheduler, isMoving: false);

        var candidate = Assert.Single(
            scheduler.InspectOgcdQueue(),
            c => c.Behavior.Action.ActionId == SGEActions.Ixochole.ActionId);
        Assert.Equal(AsclepiusPartyMetrics.AoEEmergencyPriority, candidate.Priority);
        Assert.Equal("AoE EMERGENCY", context.Debug.IxocholeState);
    }

    [Fact]
    public void Ixochole_NoEmergency_ReservationStillBlocks()
    {
        var handler = new IxocholeHandler();
        // Only one critical member — the dedup keeps its normal job on chip damage.
        var partyHelper = PartyAt(Member(2, 15000), Member(3, 30000), Member(4, 30000));
        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: true);

        var context = AsclepiusTestContext.Create(
            partyHelper: partyHelper,
            actionService: actionService,
            level: 100,
            inCombat: true,
            canExecuteOgcd: true,
            addersgallStacks: 3);

        Assert.True(context.HealingCoordination.TryReserveAoEHeal(null, 999u, 100, 0));

        var scheduler = SchedulerFactory.CreateForTest(actionService);
        handler.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(
            scheduler.InspectOgcdQueue(),
            c => c.Behavior.Action.ActionId == SGEActions.Ixochole.ActionId);
        Assert.Equal("Skipped (remote AOE reserved)", context.Debug.IxocholeState);
    }

    [Fact]
    public void Prognosis_Emergency_BypassesReservation_PushesEmergencyPriorityGcd()
    {
        var handler = new PrognosisHandler();
        var partyHelper = PartyAt(Member(2, 1), Member(3, 1), Member(4, 1));
        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: true);

        var context = AsclepiusTestContext.Create(
            partyHelper: partyHelper,
            actionService: actionService,
            level: 100,
            inCombat: true,
            canExecuteOgcd: true);

        Assert.True(context.HealingCoordination.TryReserveAoEHeal(null, 999u, 100, 0));

        var scheduler = SchedulerFactory.CreateForTest(actionService);
        handler.CollectCandidates(context, scheduler, isMoving: false);

        var candidate = Assert.Single(
            scheduler.InspectGcdQueue(),
            c => c.Behavior.Action.ActionId == SGEActions.Prognosis.ActionId);
        Assert.Equal(AsclepiusPartyMetrics.AoEEmergencyPriority + 1, candidate.Priority);
    }

    [Fact]
    public void PhysisII_Emergency_PushesAboveSingleTargetTriage()
    {
        var handler = new PhysisIIHandler();
        var partyHelper = PartyAt(Member(2, 1), Member(3, 1), Member(4, 1));
        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: true);

        var context = AsclepiusTestContext.Create(
            partyHelper: partyHelper,
            actionService: actionService,
            level: 100,
            inCombat: true,
            canExecuteOgcd: true);

        var scheduler = SchedulerFactory.CreateForTest(actionService);
        handler.CollectCandidates(context, scheduler, isMoving: false);

        var candidate = Assert.Single(
            scheduler.InspectOgcdQueue(),
            c => c.Behavior.Action.ActionId == SGEActions.PhysisII.ActionId);
        Assert.Equal(AsclepiusPartyMetrics.AoEEmergencyPriority + 1, candidate.Priority);
    }

    [Fact]
    public void Holos_Emergency_PushesEvenWithAddersgallAvailable()
    {
        var handler = new HolosHandler();
        var partyHelper = PartyAt(Member(2, 1), Member(3, 1), Member(4, 1));
        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: true);

        var context = AsclepiusTestContext.Create(
            partyHelper: partyHelper,
            actionService: actionService,
            level: 100,
            inCombat: true,
            canExecuteOgcd: true,
            addersgallStacks: 3); // Addersgall up — the old addersgall-dry emergency doesn't apply

        var scheduler = SchedulerFactory.CreateForTest(actionService);
        handler.CollectCandidates(context, scheduler, isMoving: false);

        var candidate = Assert.Single(
            scheduler.InspectOgcdQueue(),
            c => c.Behavior.Action.ActionId == SGEActions.Holos.ActionId);
        Assert.Equal(AsclepiusPartyMetrics.AoEEmergencyPriority + 2, candidate.Priority);
    }
}
