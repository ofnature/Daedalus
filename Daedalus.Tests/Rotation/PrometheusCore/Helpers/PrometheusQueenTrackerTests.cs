using Moq;
using Daedalus;
using Daedalus.Data;
using Daedalus.Rotation.PrometheusCore.Helpers;
using Daedalus.Services;
using Daedalus.Services.Content;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.PrometheusCore;
using Xunit;

namespace Daedalus.Tests.Rotation.PrometheusCore.Helpers;

public class PrometheusQueenTrackerTests
{
    [Fact]
    public void MatchesCurrentStep_StepZero_RequiresZeroToSixty()
    {
        var tracker = new PrometheusQueenTracker();
        Assert.True(tracker.MatchesCurrentStep(lastQueenBattery: 0, battery: 60));
        Assert.False(tracker.MatchesCurrentStep(lastQueenBattery: 0, battery: 50));
    }

    [Fact]
    public void OnFrame_AdvancesStepWhenLastQueenBatteryChanges()
    {
        var tracker = new PrometheusQueenTracker();
        tracker.OnFrame(60);
        Assert.Equal(1, tracker.CurrentStep);
        Assert.True(tracker.MatchesCurrentStep(lastQueenBattery: 60, battery: 90));
    }

    [Fact]
    public void Reset_ClearsStepProgress()
    {
        var tracker = new PrometheusQueenTracker();
        tracker.OnFrame(60);
        tracker.Reset();
        Assert.Equal(0, tracker.CurrentStep);
        Assert.True(tracker.MatchesCurrentStep(lastQueenBattery: 0, battery: 60));
    }
}

public class PrometheusQueenDutyModeTests
{
    [Theory]
    [InlineData(EffectiveDutyProfile.Dungeon, true, false)]
    [InlineData(EffectiveDutyProfile.Trial, true, true)]
    [InlineData(EffectiveDutyProfile.Raid, true, true)]
    [InlineData(EffectiveDutyProfile.HighEndRaid, true, true)]
    [InlineData(EffectiveDutyProfile.None, true, false)]
    [InlineData(DutyContentType.Trial, false, true)]
    [InlineData(DutyContentType.Raid, false, true)]
    [InlineData(DutyContentType.Dungeon, false, false)]
    public void UseRaidQueenStepPairs_RespectsDuty(object profileOrDuty, bool autoOn, bool expected)
    {
        var duty = new Mock<IDutyContentService>();
        if (autoOn)
        {
            duty.Setup(x => x.EffectiveProfile).Returns((EffectiveDutyProfile)profileOrDuty);
        }
        else
        {
            duty.Setup(x => x.CurrentDuty).Returns((DutyContentType)profileOrDuty);
        }

        var config = new Configuration { EnableAutoDutyConfig = autoOn };
        Assert.Equal(expected, PrometheusRotationHelper.UseRaidQueenStepPairs(duty.Object, config));
    }

    [Fact]
    public void ShouldSummonQueenOpener_TrueAfterExcavatorAtSixtyBattery()
    {
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.WasLastAction(MCHActions.Excavator.ActionId)).Returns(true);

        var combatEvents = new Mock<ICombatEventService>();
        combatEvents.Setup(x => x.GetCombatDurationSeconds()).Returns(5f);

        var context = PrometheusTestContext.Create(
            actionService: actionService,
            battery: 60,
            combatEventService: combatEvents);

        Assert.True(PrometheusRotationHelper.ShouldSummonQueenOpener(context));
    }

    [Fact]
    public void ShouldSummonQueenOpener_FalseAfterFifteenSeconds()
    {
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.WasLastAction(MCHActions.Excavator.ActionId)).Returns(true);

        var combatEvents = new Mock<ICombatEventService>();
        combatEvents.Setup(x => x.GetCombatDurationSeconds()).Returns(20f);

        var context = PrometheusTestContext.Create(
            actionService: actionService,
            battery: 60,
            combatEventService: combatEvents);

        Assert.False(PrometheusRotationHelper.ShouldSummonQueenOpener(context));
    }
}
