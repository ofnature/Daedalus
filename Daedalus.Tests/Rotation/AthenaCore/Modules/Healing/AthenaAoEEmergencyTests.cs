using Moq;
using Daedalus.Data;
using Daedalus.Rotation.AthenaCore.Modules.Healing;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.AthenaCore.Modules.Healing;

/// <summary>
/// SCH half of the AoE-emergency regression suite (party reduced to 1 HP — see
/// AoEEmergencyRecoveryTests for the SGE origin story): Succor and Indomitability must punch
/// through the cross-healer AoE reservation, the Aetherflow reserve, and single-target triage.
/// </summary>
public class AthenaAoEEmergencyTests
{
    [Fact]
    public void Succor_Emergency_BypassesReservation_PushesEmergencyPriority()
    {
        var config = AthenaTestContext.CreateDefaultScholarConfiguration();
        config.Scholar.EnableSuccor = true;

        var partyHelper = AthenaTestContext.CreatePartyWithInjured(
            healthyCount: 1, injuredCount: 3, config: config, injuredHpPercent: 0.01f);
        var actionService = MockBuilders.CreateMockActionService(canExecuteGcd: true);

        var context = AthenaTestContext.Create(
            config: config, partyHelper: partyHelper, actionService: actionService,
            level: 100, canExecuteGcd: true);

        Assert.True(context.HealingCoordination.TryReserveAoEHeal(null, 999u, 100, 0));

        var scheduler = SchedulerFactory.CreateForTest(actionService);
        new AoEHealHandler().CollectCandidates(context, scheduler, isMoving: false);

        var candidate = Assert.Single(
            scheduler.InspectGcdQueue(),
            c => c.Behavior.Action.ActionId == SCHActions.Succor.ActionId);
        Assert.Equal(6, candidate.Priority); // above single-target GCD heals (20)
    }

    [Fact]
    public void Indomitability_Emergency_SpendsThroughAetherflowReserve_AndReservation()
    {
        var config = AthenaTestContext.CreateDefaultScholarConfiguration();
        config.Scholar.EnableIndomitability = true;
        config.Scholar.AetherflowReserve = 1;

        var partyHelper = AthenaTestContext.CreatePartyWithInjured(
            healthyCount: 1, injuredCount: 3, config: config, injuredHpPercent: 0.01f);
        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: true);
        actionService.Setup(x => x.IsActionReady(SCHActions.Indomitability.ActionId)).Returns(true);

        // Exactly 1 stack = at the reserve floor: normally banked, in emergency it is spent.
        var aetherflow = AthenaTestContext.CreateMockAetherflowService(currentStacks: 1);

        var context = AthenaTestContext.Create(
            config: config, partyHelper: partyHelper, actionService: actionService,
            aetherflowService: aetherflow, level: 100, canExecuteOgcd: true);

        Assert.True(context.HealingCoordination.TryReserveAoEHeal(null, 999u, 100, 0));

        var scheduler = SchedulerFactory.CreateForTest(actionService);
        new IndomitabilityHandler().CollectCandidates(context, scheduler, isMoving: false);

        var candidate = Assert.Single(
            scheduler.InspectOgcdQueue(),
            c => c.Behavior.Action.ActionId == SCHActions.Indomitability.ActionId);
        Assert.Equal(8, candidate.Priority); // above Excogitation (15) / Lustrate (20)
    }

    [Fact]
    public void Indomitability_NoEmergency_AetherflowReserveStillBanksLastStack()
    {
        var config = AthenaTestContext.CreateDefaultScholarConfiguration();
        config.Scholar.EnableIndomitability = true;
        config.Scholar.AetherflowReserve = 1;

        // Injured but not critical — the reserve keeps its normal job.
        var partyHelper = AthenaTestContext.CreatePartyWithInjured(
            healthyCount: 1, injuredCount: 3, config: config, injuredHpPercent: 0.55f);
        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: true);
        actionService.Setup(x => x.IsActionReady(SCHActions.Indomitability.ActionId)).Returns(true);

        var aetherflow = AthenaTestContext.CreateMockAetherflowService(currentStacks: 1);

        var context = AthenaTestContext.Create(
            config: config, partyHelper: partyHelper, actionService: actionService,
            aetherflowService: aetherflow, level: 100, canExecuteOgcd: true);

        var scheduler = SchedulerFactory.CreateForTest(actionService);
        new IndomitabilityHandler().CollectCandidates(context, scheduler, isMoving: false);

        Assert.Empty(scheduler.InspectOgcdQueue());
    }
}
