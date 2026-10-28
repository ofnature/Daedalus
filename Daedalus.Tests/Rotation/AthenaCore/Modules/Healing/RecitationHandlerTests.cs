using Moq;
using Daedalus.Config;
using Daedalus.Data;
using Daedalus.Rotation.AthenaCore.Modules.Healing;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.AthenaCore.Modules.Healing;

/// <summary>
/// Recitation is only worth pressing when its planned follow-up can go out inside the 15s buff. RSR presses it for
/// Excogitation / Indomitability only while that action is off cooldown (SCH_Reborn ShouldUseRecitation, the branch
/// RSR 7f857722 fixed). Recitation (60s) and Excogitation (45s) drift apart, so without the check Recitation went out
/// with its follow-up still 30s away.
/// </summary>
public class RecitationHandlerTests
{
    [Fact]
    public void IndomitabilityPriority_IndomitabilityReady_PushesRecitation()
    {
        var scheduler = Collect(indomitabilityReady: true);

        Assert.Contains(scheduler.InspectOgcdQueue(),
            c => c.Behavior.Action.ActionId == SCHActions.Recitation.ActionId);
    }

    [Fact]
    public void IndomitabilityPriority_IndomitabilityOnCooldown_HoldsRecitation()
    {
        var scheduler = Collect(indomitabilityReady: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(),
            c => c.Behavior.Action.ActionId == SCHActions.Recitation.ActionId);
    }

    [Fact]
    public void IndomitabilityPriority_IndomitabilityDisabled_HoldsRecitation()
    {
        var scheduler = Collect(indomitabilityReady: true, enableIndomitability: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(),
            c => c.Behavior.Action.ActionId == SCHActions.Recitation.ActionId);
    }

    private static Daedalus.Rotation.Common.Scheduling.RotationScheduler Collect(
        bool indomitabilityReady, bool enableIndomitability = true)
    {
        var config = AthenaTestContext.CreateDefaultScholarConfiguration();
        config.Scholar.EnableRecitation = true;
        config.Scholar.EnableIndomitability = enableIndomitability;
        config.Scholar.RecitationPriority = RecitationPriority.Indomitability;

        // A raidwide just landed: three of four members at 30%.
        var partyHelper = AthenaTestContext.CreatePartyWithInjured(
            healthyCount: 1, injuredCount: 3, config: config, injuredHpPercent: 0.30f);
        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: true);
        actionService.Setup(x => x.IsActionReady(SCHActions.Recitation.ActionId)).Returns(true);
        actionService.Setup(x => x.IsActionReady(SCHActions.Indomitability.ActionId)).Returns(indomitabilityReady);

        var context = AthenaTestContext.Create(
            config: config, partyHelper: partyHelper, actionService: actionService,
            level: 100, canExecuteOgcd: true);

        var scheduler = SchedulerFactory.CreateForTest(actionService);
        new RecitationHandler().CollectCandidates(context, scheduler, isMoving: false);
        return scheduler;
    }
}
