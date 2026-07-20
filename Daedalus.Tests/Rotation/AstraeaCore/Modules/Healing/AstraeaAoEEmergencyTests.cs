using Moq;
using Daedalus.Data;
using Daedalus.Rotation.AstraeaCore.Abilities;
using Daedalus.Rotation.AstraeaCore.Modules.Healing;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.AstraeaCore.Modules.Healing;

/// <summary>
/// AST half of the AoE-emergency regression suite (party reduced to 1 HP — see
/// AoEEmergencyRecoveryTests for the SGE origin story): Helios and Celestial Opposition must
/// punch through the cross-healer AoE reservation and outrank single-target triage.
/// </summary>
public class AstraeaAoEEmergencyTests
{
    [Fact]
    public void Helios_Emergency_BypassesReservation_PushesEmergencyPriority()
    {
        var config = AstraeaTestContext.CreateDefaultAstrologianConfiguration();
        config.Astrologian.EnableHelios = true;
        config.Astrologian.EnableAspectedHelios = true;

        var partyHelper = AstraeaTestContext.CreatePartyWithInjured(
            healthyCount: 1, injuredCount: 3, config: config, injuredHpPercent: 0.01f);
        var actionService = MockBuilders.CreateMockActionService(canExecuteGcd: true);

        var context = AstraeaTestContext.Create(
            config: config, partyHelper: partyHelper, actionService: actionService,
            level: 100, canExecuteGcd: true);

        Assert.True(context.HealingCoordination.TryReserveAoEHeal(null, 999u, 100, 0));

        var scheduler = SchedulerFactory.CreateForTest(actionService);
        new AoEHealingHandler().CollectCandidates(context, scheduler, isMoving: false);

        var candidate = Assert.Single(scheduler.InspectGcdQueue());
        Assert.Equal(8, candidate.Priority); // above Essential Dignity (10)
    }

    [Fact]
    public void Helios_NoEmergency_ReservationStillBlocks()
    {
        var config = AstraeaTestContext.CreateDefaultAstrologianConfiguration();
        config.Astrologian.EnableHelios = true;
        config.Astrologian.EnableAspectedHelios = true;

        // Injured but not critical (55% > the 40% emergency threshold) — normal dedup applies.
        var partyHelper = AstraeaTestContext.CreatePartyWithInjured(
            healthyCount: 1, injuredCount: 3, config: config, injuredHpPercent: 0.55f);
        var actionService = MockBuilders.CreateMockActionService(canExecuteGcd: true);

        var context = AstraeaTestContext.Create(
            config: config, partyHelper: partyHelper, actionService: actionService,
            level: 100, canExecuteGcd: true);

        Assert.True(context.HealingCoordination.TryReserveAoEHeal(null, 999u, 100, 0));

        var scheduler = SchedulerFactory.CreateForTest(actionService);
        new AoEHealingHandler().CollectCandidates(context, scheduler, isMoving: false);

        Assert.Empty(scheduler.InspectGcdQueue());
    }

    [Fact]
    public void CelestialOpposition_Emergency_BypassesReservation_PushesEmergencyPriority()
    {
        var config = AstraeaTestContext.CreateDefaultAstrologianConfiguration();
        config.Astrologian.EnableCelestialOpposition = true;

        var partyHelper = AstraeaTestContext.CreatePartyWithInjured(
            healthyCount: 1, injuredCount: 3, config: config, injuredHpPercent: 0.01f);
        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: true);
        actionService.Setup(x => x.IsActionReady(ASTActions.CelestialOpposition.ActionId)).Returns(true);

        var context = AstraeaTestContext.Create(
            config: config, partyHelper: partyHelper, actionService: actionService,
            level: 100, canExecuteOgcd: true);

        Assert.True(context.HealingCoordination.TryReserveAoEHeal(null, 999u, 100, 0));

        var scheduler = SchedulerFactory.CreateForTest(actionService);
        new CelestialOppositionHandler().CollectCandidates(context, scheduler, isMoving: false);

        var candidate = Assert.Single(
            scheduler.InspectOgcdQueue(),
            c => c.Behavior == AstraeaAbilities.CelestialOpposition);
        Assert.Equal(8, candidate.Priority);
    }
}
