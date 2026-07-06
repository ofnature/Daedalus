using Moq;
using Daedalus.Data;
using Daedalus.Rotation.HermesCore.Abilities;
using Daedalus.Rotation.HermesCore.Helpers;
using Daedalus.Rotation.HermesCore.Modules;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.HermesCore.Modules;

/// <summary>
/// Lv60 field regression (Xelphatol 2026-07-05): two Rabbit Mediums in three pulls, both at pull
/// open. Root causes decoded from press timestamps: (1) a hand sign submitted 0.30s after the
/// previous one (clean sequences space 0.7-0.8s) raced the game's mudra combo-state write into a
/// duplicate/invalid sign; (2) Kassatsu weaved BETWEEN signs flips HasKassatsu mid-sequence, which
/// feeds the step resolver and derails the remaining signs. Fixes: a minimum inter-press pacing
/// gate driven by MudraHelper.SecondsSinceLastPress, and Kassatsu blocked while a sequence runs.
/// </summary>
public class HermesMudraPacingTests
{
    [Fact]
    public void MudraHelper_NoPressYet_ReportsMaxInterval()
    {
        var helper = new MudraHelper();

        Assert.Equal(double.MaxValue, helper.SecondsSinceLastPress);
    }

    [Fact]
    public void MudraHelper_AfterPress_ReportsSubSecondInterval()
    {
        var helper = new MudraHelper();

        helper.NotifyMudraPressed();

        // Immediately after a press the interval must be tiny — the executor's pacing gate
        // (MinSecondsBetweenMudraPresses) holds the next sign until the 0.5s recast elapses.
        Assert.True(helper.SecondsSinceLastPress < HermesNinjutsuMudraExecutor.MinSecondsBetweenMudraPresses);
    }

    [Fact]
    public void PacingConstant_MatchesMudraRecast()
    {
        // The game's mudra recast is 0.5s; pacing below it is what produced the 0.30s race.
        Assert.Equal(0.5, HermesNinjutsuMudraExecutor.MinSecondsBetweenMudraPresses);
    }

    [Fact]
    public void Kassatsu_BlockedWhileMudraSequenceActive()
    {
        var mudraHelper = new MudraHelper();
        mudraHelper.StartSequence(NINActions.NinjutsuType.Suiton);

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);
        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = HermesTestContext.Create(
            actionService: actionService,
            mudraHelper: mudraHelper,
            level: 60);

        new BuffModule().CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == HermesAbilities.Kassatsu);
    }

    [Fact]
    public void OrphanedLiveSigns_BlockComboGcds()
    {
        // Second field round (Sohm Al logs): the module sequence aborted but the GAME still held
        // registered signs — Death Blossom fired 1.0s after an orphaned Ten and Throwing Dagger
        // fired over a pending Suiton, both ending in Rabbit Medium. Mudra status + a pending
        // ninjutsu slot must hold combo GCDs even with no module-side sequence active.
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.GetAdjustedActionId(NINActions.Ninjutsu.ActionId))
            .Returns(NINActions.Raiton.ActionId);
        var context = HermesTestContext.Create(
            actionService: actionService,
            hasGameMudraStatus: true,
            isMudraSequenceActive: false,
            level: 60);

        Assert.True(HermesMudraGate.ShouldBlockComboGcds(context));
    }

    [Fact]
    public void IdleSlot_NoSequence_DoesNotBlockComboGcds()
    {
        // Guard the normal case: no signs anywhere — combo flows.
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.GetAdjustedActionId(NINActions.Ninjutsu.ActionId))
            .Returns(NINActions.Ninjutsu.ActionId);
        var context = HermesTestContext.Create(
            actionService: actionService,
            hasGameMudraStatus: false,
            isMudraSequenceActive: false,
            level: 60);

        Assert.False(HermesMudraGate.ShouldBlockComboGcds(context));
    }

    [Fact]
    public void PendingSlotWithoutMudraStatus_DoesNotBlockComboGcds()
    {
        // Bounded by the mudra status: once the signs expire the status drops, and a stale slot
        // read alone must not freeze the combo.
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.GetAdjustedActionId(NINActions.Ninjutsu.ActionId))
            .Returns(NINActions.Raiton.ActionId);
        var context = HermesTestContext.Create(
            actionService: actionService,
            hasGameMudraStatus: false,
            isMudraSequenceActive: false,
            level: 60);

        Assert.False(HermesMudraGate.ShouldBlockComboGcds(context));
    }

    [Fact]
    public void Kassatsu_StillFires_WhenNoSequenceActive()
    {
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);
        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = HermesTestContext.Create(
            actionService: actionService,
            level: 60);

        new BuffModule().CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == HermesAbilities.Kassatsu);
    }
}
