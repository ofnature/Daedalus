using Moq;
using Daedalus.Data;
using Daedalus.Rotation.PrometheusCore.Helpers;
using Daedalus.Services.Action;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.PrometheusCore;
using Xunit;

namespace Daedalus.Tests.Rotation.PrometheusCore.Helpers;

public class PrometheusRotationHelperTests
{
    [Theory]
    [InlineData(2866u, 2.5f, true)]   // Split Shot
    [InlineData(7412u, 4.0f, true)]   // Heated Slug Shot
    [InlineData(2866u, 0.5f, false)]
    [InlineData(2866u, 6.0f, false)]
    [InlineData(2869u, 2.5f, false)]  // Clean Shot
    public void NeedsComboRescue_RespectsComboWindow(uint lastCombo, float comboRemaining, bool expected)
    {
        var actionService = new Mock<IActionService>();
        actionService.Setup(x => x.GcdDuration).Returns(2.5f);

        var context = PrometheusTestContext.Create(
            actionService: actionService,
            lastComboAction: lastCombo,
            comboTimeRemaining: comboRemaining);

        Assert.Equal(expected, PrometheusRotationHelper.NeedsComboRescue(context));
    }

    [Fact]
    public void NeedsComboRescue_FalseWhenOverheated()
    {
        var context = PrometheusTestContext.Create(
            isOverheated: true,
            lastComboAction: MCHActions.SplitShot.ActionId,
            comboTimeRemaining: 2.5f);

        Assert.False(PrometheusRotationHelper.NeedsComboRescue(context));
    }

    [Fact]
    public void ShouldUseFullMetalFieldNow_DeferredWhileToolsReady()
    {
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionReady(MCHActions.FullMetalField.ActionId)).Returns(true);
        actionService.Setup(x => x.IsActionReady(MCHActions.AirAnchor.ActionId)).Returns(true);
        actionService.Setup(x => x.GetCooldownRemaining(MCHActions.Wildfire.ActionId)).Returns(0f);

        var context = PrometheusTestContext.Create(
            actionService: actionService,
            hasFullMetalMachinist: true,
            battery: 50);

        Assert.False(PrometheusRotationHelper.ShouldUseFullMetalFieldNow(context));
    }

    [Fact]
    public void ShouldUseFullMetalFieldNow_TrueWhenNoToolsReady()
    {
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionReady(MCHActions.FullMetalField.ActionId)).Returns(true);
        actionService.Setup(x => x.IsActionReady(MCHActions.AirAnchor.ActionId)).Returns(false);
        actionService.Setup(x => x.IsActionReady(MCHActions.Drill.ActionId)).Returns(false);
        actionService.Setup(x => x.IsActionReady(MCHActions.ChainSaw.ActionId)).Returns(false);
        actionService.Setup(x => x.GetCooldownRemaining(MCHActions.Wildfire.ActionId)).Returns(0f);

        var context = PrometheusTestContext.Create(
            actionService: actionService,
            hasFullMetalMachinist: true,
            drillCharges: 0,
            battery: 50);

        Assert.True(PrometheusRotationHelper.ShouldUseFullMetalFieldNow(context));
    }

    [Fact]
    public void PreferGaussRoundOverRicochet_DefaultsToRicochetWhenEqual()
    {
        var actionService = new Mock<IActionService>();
        actionService.Setup(x => x.GetRecastTimeElapsed(MCHActions.GaussRound.ActionId)).Returns(5f);
        actionService.Setup(x => x.GetRecastTimeElapsed(MCHActions.Ricochet.ActionId)).Returns(5f);

        Assert.False(PrometheusRotationHelper.PreferGaussRoundOverRicochet(
            actionService.Object, MCHActions.GaussRound.ActionId, MCHActions.Ricochet.ActionId));
    }

    [Fact]
    public void ShouldHoldHyperchargeForTools_TrueWhenDrillReturnsSoon()
    {
        var actionService = new Mock<IActionService>();
        actionService.Setup(x => x.IsActionReady(MCHActions.Drill.ActionId)).Returns(false);
        actionService.Setup(x => x.GetCooldownRemaining(MCHActions.Drill.ActionId)).Returns(5f);
        actionService.Setup(x => x.IsActionReady(MCHActions.AirAnchor.ActionId)).Returns(false);
        actionService.Setup(x => x.GetCooldownRemaining(MCHActions.AirAnchor.ActionId)).Returns(30f);
        actionService.Setup(x => x.IsActionReady(MCHActions.ChainSaw.ActionId)).Returns(false);
        actionService.Setup(x => x.GetCooldownRemaining(MCHActions.ChainSaw.ActionId)).Returns(30f);

        var context = PrometheusTestContext.Create(
            actionService: actionService,
            drillCharges: 0,
            level: 100);

        Assert.True(PrometheusRotationHelper.ShouldHoldHyperchargeForTools(context, enemyCount: 1));
    }

    [Fact]
    public void ShouldHoldHyperchargeForTools_FalseInAoE()
    {
        var actionService = new Mock<IActionService>();
        actionService.Setup(x => x.GetCooldownRemaining(MCHActions.Drill.ActionId)).Returns(5f);

        var context = PrometheusTestContext.Create(actionService: actionService, drillCharges: 0);

        Assert.False(PrometheusRotationHelper.ShouldHoldHyperchargeForTools(context, enemyCount: 3));
    }

    [Fact]
    public void ShouldHoldHyperchargeForFullMetalField_TrueWhenProcReady()
    {
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionReady(MCHActions.FullMetalField.ActionId)).Returns(true);

        var context = PrometheusTestContext.Create(
            actionService: actionService,
            hasFullMetalMachinist: true);

        Assert.True(PrometheusRotationHelper.ShouldHoldHyperchargeForFullMetalField(context));
    }

    [Theory]
    [InlineData(16500u, 85, true)]   // Air Anchor
    [InlineData(2873u, 85, false)]   // Clean Shot
    [InlineData(2873u, 95, true)]    // Clean Shot
    public void ShouldOvercapSummonQueen_BatteryThresholds(uint nextGcd, int battery, bool expected)
    {
        var context = PrometheusTestContext.Create(battery: battery);
        Assert.Equal(expected, PrometheusRotationHelper.ShouldOvercapSummonQueen(context, nextGcd));
    }
}
