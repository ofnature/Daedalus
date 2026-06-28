using Moq;
using Daedalus.Config;
using Daedalus.Data;
using Daedalus.Rotation.AstraeaCore.Abilities;
using Daedalus.Rotation.AstraeaCore.Modules.Healing;
using Daedalus.Services.Action;
using Daedalus.Services.Healing;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.AstraeaCore.Modules.Healing;

/// <summary>
/// Essential Dignity per-charge thresholds (RSR parity) and the RestrictGcdHealsWithCoHealer GCD-heal gate.
/// </summary>
public class AstraeaCoHealerAndDignityTests
{
    // ── Essential Dignity charge-tier thresholds ───────────────────────────────────────────────

    [Fact]
    public void EssentialDignity_SpareCharge_FiresAtHigherHp()
    {
        // 2 charges available: a target at 68% (below the 0.70 spare threshold, above the 0.60 last
        // threshold) should get Essential Dignity because a spare charge is spent proactively.
        var config = AstraeaTestContext.CreateDefaultAstrologianConfiguration();
        config.Astrologian.EssentialDignitySpareChargeThreshold = 0.70f;
        config.Astrologian.EssentialDignityThreshold = 0.60f;

        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: true);
        actionService.Setup(x => x.IsActionReady(ASTActions.EssentialDignity.ActionId)).Returns(true);
        actionService.Setup(x => x.GetCurrentCharges(ASTActions.EssentialDignity.ActionId)).Returns(2u);

        // One member at 68% HP.
        var partyHelper = AstraeaTestContext.CreatePartyWithInjured(healthyCount: 3, injuredCount: 1, injuredHpPercent: 0.68f);

        var context = AstraeaTestContext.Create(
            config: config, partyHelper: partyHelper, actionService: actionService,
            level: 100, canExecuteOgcd: true);
        var scheduler = SchedulerFactory.CreateForTest(actionService);

        new EssentialDignityHandler().CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == AstraeaAbilities.EssentialDignity);
    }

    [Fact]
    public void EssentialDignity_LastCharge_HeldAtSpareHp()
    {
        // Only 1 charge left: a target at 68% is above the 0.60 last-charge threshold, so the final
        // charge is banked (NOT spent) — the spare threshold (0.70) must not apply on the last charge.
        var config = AstraeaTestContext.CreateDefaultAstrologianConfiguration();
        config.Astrologian.EssentialDignitySpareChargeThreshold = 0.70f;
        config.Astrologian.EssentialDignityThreshold = 0.60f;

        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: true);
        actionService.Setup(x => x.IsActionReady(ASTActions.EssentialDignity.ActionId)).Returns(true);
        actionService.Setup(x => x.GetCurrentCharges(ASTActions.EssentialDignity.ActionId)).Returns(1u);

        var partyHelper = AstraeaTestContext.CreatePartyWithInjured(healthyCount: 3, injuredCount: 1, injuredHpPercent: 0.68f);

        var context = AstraeaTestContext.Create(
            config: config, partyHelper: partyHelper, actionService: actionService,
            level: 100, canExecuteOgcd: true);
        var scheduler = SchedulerFactory.CreateForTest(actionService);

        new EssentialDignityHandler().CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == AstraeaAbilities.EssentialDignity);
    }

    [Fact]
    public void EssentialDignity_LastCharge_FiresWhenCritical()
    {
        // Last charge, target at 55% (below the 0.60 last-charge threshold) — the banked charge fires.
        var config = AstraeaTestContext.CreateDefaultAstrologianConfiguration();
        config.Astrologian.EssentialDignitySpareChargeThreshold = 0.70f;
        config.Astrologian.EssentialDignityThreshold = 0.60f;

        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: true);
        actionService.Setup(x => x.IsActionReady(ASTActions.EssentialDignity.ActionId)).Returns(true);
        actionService.Setup(x => x.GetCurrentCharges(ASTActions.EssentialDignity.ActionId)).Returns(1u);

        var partyHelper = AstraeaTestContext.CreatePartyWithInjured(healthyCount: 3, injuredCount: 1, injuredHpPercent: 0.55f);

        var context = AstraeaTestContext.Create(
            config: config, partyHelper: partyHelper, actionService: actionService,
            level: 100, canExecuteOgcd: true);
        var scheduler = SchedulerFactory.CreateForTest(actionService);

        new EssentialDignityHandler().CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == AstraeaAbilities.EssentialDignity);
    }

    // ── RestrictGcdHealsWithCoHealer ───────────────────────────────────────────────────────────

    [Fact]
    public void SingleTargetHeal_WithCoHealer_DefersNonCriticalGcdHeal()
    {
        // Co-healer present, target injured but above the GCD-emergency threshold: defer the GCD heal.
        var config = AstraeaTestContext.CreateDefaultAstrologianConfiguration();
        config.Astrologian.RestrictGcdHealsWithCoHealer = true;
        config.Astrologian.EnableBeneficII = true;
        config.Astrologian.BeneficIIThreshold = 0.60f;
        config.Healing.GcdEmergencyThreshold = 0.40f;
        config.Healing.EnableCoHealerAwareness = false; // isolate the hard gate

        var actionService = MockBuilders.CreateMockActionService(canExecuteGcd: true);
        actionService.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);

        var coHealer = new Mock<ICoHealerDetectionService>();
        coHealer.Setup(x => x.HasCoHealer).Returns(true);

        // Target at 55%: below the 60% Benefic II threshold, above the 40% emergency threshold.
        var partyHelper = AstraeaTestContext.CreatePartyWithInjured(healthyCount: 3, injuredCount: 1, injuredHpPercent: 0.55f);

        var context = AstraeaTestContext.Create(
            config: config, partyHelper: partyHelper, actionService: actionService,
            coHealerDetectionService: coHealer, level: 100, canExecuteGcd: true);
        var scheduler = SchedulerFactory.CreateForTest(actionService);

        new SingleTargetHandler().CollectCandidates(context, scheduler, isMoving: false);

        Assert.Empty(scheduler.InspectGcdQueue());
    }

    [Fact]
    public void SingleTargetHeal_WithCoHealer_StillHealsCriticalTarget()
    {
        // Co-healer present but target is critical (below GCD-emergency): the GCD heal still fires.
        var config = AstraeaTestContext.CreateDefaultAstrologianConfiguration();
        config.Astrologian.RestrictGcdHealsWithCoHealer = true;
        config.Astrologian.EnableBeneficII = true;
        config.Astrologian.BeneficIIThreshold = 0.60f;
        config.Healing.GcdEmergencyThreshold = 0.40f;
        config.Healing.EnableCoHealerAwareness = false;

        var actionService = MockBuilders.CreateMockActionService(canExecuteGcd: true);
        actionService.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);

        var coHealer = new Mock<ICoHealerDetectionService>();
        coHealer.Setup(x => x.HasCoHealer).Returns(true);

        // Target at 30%: below the 40% emergency threshold.
        var partyHelper = AstraeaTestContext.CreatePartyWithInjured(healthyCount: 3, injuredCount: 1, injuredHpPercent: 0.30f);

        var context = AstraeaTestContext.Create(
            config: config, partyHelper: partyHelper, actionService: actionService,
            coHealerDetectionService: coHealer, level: 100, canExecuteGcd: true);
        var scheduler = SchedulerFactory.CreateForTest(actionService);

        new SingleTargetHandler().CollectCandidates(context, scheduler, isMoving: false);

        Assert.NotEmpty(scheduler.InspectGcdQueue());
    }

    [Fact]
    public void SingleTargetHeal_MainRole_DoesNotDeferEvenWithCoHealer()
    {
        // Designated Main healer owns GCD heals — it must NOT defer a non-critical GCD heal even though a
        // co-healer is present (this is what prevents two healers from both deferring to each other).
        var config = AstraeaTestContext.CreateDefaultAstrologianConfiguration();
        config.Astrologian.RestrictGcdHealsWithCoHealer = true;
        config.Astrologian.EnableBeneficII = true;
        config.Astrologian.BeneficIIThreshold = 0.60f;
        config.Healing.GcdEmergencyThreshold = 0.40f;
        config.Healing.EnableCoHealerAwareness = false;
        config.Healing.HealerRole = HealerRoleAssignment.Main;

        var actionService = MockBuilders.CreateMockActionService(canExecuteGcd: true);
        actionService.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);

        var coHealer = new Mock<ICoHealerDetectionService>();
        coHealer.Setup(x => x.HasCoHealer).Returns(true);

        var partyHelper = AstraeaTestContext.CreatePartyWithInjured(healthyCount: 3, injuredCount: 1, injuredHpPercent: 0.55f);

        var context = AstraeaTestContext.Create(
            config: config, partyHelper: partyHelper, actionService: actionService,
            coHealerDetectionService: coHealer, level: 100, canExecuteGcd: true);
        var scheduler = SchedulerFactory.CreateForTest(actionService);

        new SingleTargetHandler().CollectCandidates(context, scheduler, isMoving: false);

        Assert.NotEmpty(scheduler.InspectGcdQueue());
    }

    [Fact]
    public void SingleTargetHeal_CoRole_DefersWithCoHealer()
    {
        // Designated Co healer defers non-critical GCD heals to the Main, even if the per-job awareness
        // master toggle is off — the role itself drives deferral via RestrictGcdHealsWithCoHealer.
        var config = AstraeaTestContext.CreateDefaultAstrologianConfiguration();
        config.Astrologian.RestrictGcdHealsWithCoHealer = true;
        config.Astrologian.EnableBeneficII = true;
        config.Astrologian.BeneficIIThreshold = 0.60f;
        config.Healing.GcdEmergencyThreshold = 0.40f;
        config.Healing.EnableCoHealerAwareness = false;
        config.Healing.HealerRole = HealerRoleAssignment.Co;

        var actionService = MockBuilders.CreateMockActionService(canExecuteGcd: true);
        actionService.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);

        var coHealer = new Mock<ICoHealerDetectionService>();
        coHealer.Setup(x => x.HasCoHealer).Returns(true);

        var partyHelper = AstraeaTestContext.CreatePartyWithInjured(healthyCount: 3, injuredCount: 1, injuredHpPercent: 0.55f);

        var context = AstraeaTestContext.Create(
            config: config, partyHelper: partyHelper, actionService: actionService,
            coHealerDetectionService: coHealer, level: 100, canExecuteGcd: true);
        var scheduler = SchedulerFactory.CreateForTest(actionService);

        new SingleTargetHandler().CollectCandidates(context, scheduler, isMoving: false);

        Assert.Empty(scheduler.InspectGcdQueue());
    }

    [Theory]
    [InlineData(HealerRoleAssignment.Main, true, true, false)]   // Main never defers
    [InlineData(HealerRoleAssignment.Co, true, true, true)]      // Co defers when co-healer present
    [InlineData(HealerRoleAssignment.Auto, true, true, true)]    // Auto defers when co-healer present
    [InlineData(HealerRoleAssignment.Co, false, true, false)]    // master toggle off → no defer
    [InlineData(HealerRoleAssignment.Co, true, false, false)]    // solo (no co-healer) → no defer
    public void ShouldDeferGcdHeals_ResolvesByRole(
        HealerRoleAssignment role, bool restrictEnabled, bool hasCoHealer, bool expected)
    {
        Assert.Equal(expected,
            Daedalus.Rotation.Common.Helpers.CoHealerAwarenessHelper.ShouldDeferGcdHeals(
                role, restrictEnabled, hasCoHealer));
    }

    [Fact]
    public void SingleTargetHeal_SoloHealer_NotRestricted()
    {
        // No co-healer: the restriction is inert, non-critical GCD heal fires as normal.
        var config = AstraeaTestContext.CreateDefaultAstrologianConfiguration();
        config.Astrologian.RestrictGcdHealsWithCoHealer = true;
        config.Astrologian.EnableBeneficII = true;
        config.Astrologian.BeneficIIThreshold = 0.60f;
        config.Healing.GcdEmergencyThreshold = 0.40f;
        config.Healing.EnableCoHealerAwareness = false;

        var actionService = MockBuilders.CreateMockActionService(canExecuteGcd: true);
        actionService.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);

        var coHealer = new Mock<ICoHealerDetectionService>();
        coHealer.Setup(x => x.HasCoHealer).Returns(false);

        var partyHelper = AstraeaTestContext.CreatePartyWithInjured(healthyCount: 3, injuredCount: 1, injuredHpPercent: 0.55f);

        var context = AstraeaTestContext.Create(
            config: config, partyHelper: partyHelper, actionService: actionService,
            coHealerDetectionService: coHealer, level: 100, canExecuteGcd: true);
        var scheduler = SchedulerFactory.CreateForTest(actionService);

        new SingleTargetHandler().CollectCandidates(context, scheduler, isMoving: false);

        Assert.NotEmpty(scheduler.InspectGcdQueue());
    }
}
