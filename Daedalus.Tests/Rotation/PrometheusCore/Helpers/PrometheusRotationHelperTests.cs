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
    public void ShouldUseFullMetalFieldNow_FiresEvenWhenActionReadyReadsFalse()
    {
        // Regression (2026-07-01 MCH validation): FMF sits on the GLOBAL recast group, so
        // IsActionReady/GetCurrentCharges reads not-ready whenever the GCD is rolling — every submit
        // moment at full uptime. The old IsActionReady(FMF) gate was therefore a permanent lock and
        // FMF never fired in-game. Availability is the STATUS (RSR parity) — no cooldown check.
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionReady(MCHActions.FullMetalField.ActionId)).Returns(false);
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
    public void ShouldUseFullMetalFieldNow_DefersWhileReassembled()
    {
        // FMF is guaranteed crit/DH — spending a Reassemble charge on it is a waste
        // (RSR: !HasReassembled && HasFullMetalMachinist). Let a tool consume Reassemble first.
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionReady(MCHActions.AirAnchor.ActionId)).Returns(false);
        actionService.Setup(x => x.IsActionReady(MCHActions.Drill.ActionId)).Returns(false);
        actionService.Setup(x => x.IsActionReady(MCHActions.ChainSaw.ActionId)).Returns(false);
        actionService.Setup(x => x.GetCooldownRemaining(MCHActions.Wildfire.ActionId)).Returns(0f);

        var context = PrometheusTestContext.Create(
            actionService: actionService,
            hasFullMetalMachinist: true,
            hasReassemble: true,
            drillCharges: 0,
            battery: 50);

        Assert.False(PrometheusRotationHelper.ShouldUseFullMetalFieldNow(context));
    }

    [Fact]
    public void ShouldHoldHyperchargeForFullMetalField_HoldsEvenWhenActionReadyReadsFalse()
    {
        // Same broken IsActionReady(FMF) read gated the Hypercharge hold — it NEVER engaged, so HC
        // fired ahead of FMF and buried the proc under Overheat GCDs until it expired unspent.
        // With the proc up (and not overheated), the hold must engage unconditionally.
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionReady(MCHActions.FullMetalField.ActionId)).Returns(false);

        var context = PrometheusTestContext.Create(
            actionService: actionService,
            hasFullMetalMachinist: true);

        Assert.True(PrometheusRotationHelper.ShouldHoldHyperchargeForFullMetalField(context));
    }

    [Fact]
    public void ShouldHoldHyperchargeForFullMetalField_ReleasesOnceProcSpent()
    {
        var context = PrometheusTestContext.Create(hasFullMetalMachinist: false);
        Assert.False(PrometheusRotationHelper.ShouldHoldHyperchargeForFullMetalField(context));
    }

    [Fact]
    public void ShouldHoldQueenForDyingPack_HoldsWhenBeefiestEnemyNearlyDead()
    {
        // Regression (2026-07-01): Queen deployed onto a trash mob at 3.8% HP, 0.7s before combat
        // ended — pure Battery waste. Hold when even the highest-HP enemy in range is nearly dead.
        var context = PrometheusTestContext.Create(
            targetingService: TargetingWithBeefiest(currentHp: 38_000, maxHp: 1_000_000)); // 3.8%
        Assert.True(PrometheusRotationHelper.ShouldHoldQueenForDyingPack(context));
    }

    [Fact]
    public void ShouldHoldQueenForDyingPack_AllowsWhenHealthyEnemyPresent()
    {
        var context = PrometheusTestContext.Create(
            targetingService: TargetingWithBeefiest(currentHp: 500_000, maxHp: 1_000_000)); // 50%
        Assert.False(PrometheusRotationHelper.ShouldHoldQueenForDyingPack(context));
    }

    [Fact]
    public void ShouldHoldQueenForDyingPack_DisabledAtZeroThreshold()
    {
        var config = new Configuration();
        config.Machinist.QueenHoldTargetHpPercent = 0;
        var context = PrometheusTestContext.Create(
            config: config,
            targetingService: TargetingWithBeefiest(currentHp: 1, maxHp: 1_000_000));
        Assert.False(PrometheusRotationHelper.ShouldHoldQueenForDyingPack(context));
    }

    [Fact]
    public void ShouldHoldQueenForDyingPack_AllowsWhenNoEnemyResolved()
    {
        var context = PrometheusTestContext.Create(
            targetingService: MockBuilders.CreateMockTargetingService());
        Assert.False(PrometheusRotationHelper.ShouldHoldQueenForDyingPack(context));
    }

    private static Mock<Daedalus.Services.Targeting.ITargetingService> TargetingWithBeefiest(uint currentHp, uint maxHp)
    {
        var enemy = new Mock<Dalamud.Game.ClientState.Objects.Types.IBattleNpc>();
        enemy.Setup(x => x.CurrentHp).Returns(currentHp);
        enemy.Setup(x => x.MaxHp).Returns(maxHp);

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemy(
                Daedalus.Services.Targeting.EnemyTargetingStrategy.HighestHp,
                It.IsAny<float>(),
                It.IsAny<Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter>()))
            .Returns(enemy.Object);
        return targeting;
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
