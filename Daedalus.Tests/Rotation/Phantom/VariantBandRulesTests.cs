using Daedalus.Config;
using Daedalus.Rotation.Phantom;
using Xunit;

namespace Daedalus.Tests.Rotation.Phantom;

/// <summary>
/// Tests for the variant duty-action band rules (docs/variant-actions-plan.md Phase 2),
/// especially the raise policy from the 2026-07-25 party-comp discussion.
/// </summary>
public class VariantBandRulesTests
{
    private static VariantConfig Cfg() => new();

    [Fact]
    public void Cure_FiresBelowConfiguredThreshold()
    {
        Assert.True(VariantBandRules.ShouldCure(Cfg(), 0.50f));   // default 0.60
        Assert.False(VariantBandRules.ShouldCure(Cfg(), 0.70f));
    }

    [Fact]
    public void SpiritDart_IsDotMaintenance_NotOnCooldownSpam()
    {
        // DoT missing (0s) or about to fall off → reapply; healthy DoT → hold.
        Assert.True(VariantBandRules.ShouldMaintainDart(Cfg(), 0f, float.MaxValue));
        Assert.True(VariantBandRules.ShouldMaintainDart(Cfg(), 2f, float.MaxValue));
        Assert.False(VariantBandRules.ShouldMaintainDart(Cfg(), 25f, float.MaxValue));

        var off = Cfg();
        off.UseSpiritDart = false;
        Assert.False(VariantBandRules.ShouldMaintainDart(off, 0f, float.MaxValue));
    }

    [Fact]
    public void SpiritDart_TtkGate_SkipsDyingTargets_FailsOpenWhenUnknown()
    {
        // Mob dying in 4s: the 30s DoT is a wasted weave.
        Assert.False(VariantBandRules.ShouldMaintainDart(Cfg(), 0f, targetTtkSeconds: 4f));
        // Healthy TTK → apply.
        Assert.True(VariantBandRules.ShouldMaintainDart(Cfg(), 0f, targetTtkSeconds: 30f));
        // Unknown TTK (MaxValue, e.g. fresh pull with no HP samples yet) → apply.
        Assert.True(VariantBandRules.ShouldMaintainDart(Cfg(), 0f, targetTtkSeconds: float.MaxValue));
    }

    [Fact]
    public void Rampart_PacedByItsBuff_UnlessSpamEnabled()
    {
        Assert.True(VariantBandRules.ShouldRampart(Cfg(), inCombat: true, buffActive: false));
        Assert.False(VariantBandRules.ShouldRampart(Cfg(), inCombat: true, buffActive: true));
        Assert.False(VariantBandRules.ShouldRampart(Cfg(), inCombat: false, buffActive: false));

        var spam = Cfg();
        spam.RampartSpamOnCooldown = true;
        Assert.True(VariantBandRules.ShouldRampart(spam, inCombat: true, buffActive: true));
    }

    // ── The raise policy (WAR/SAM/PCT + SGE comp): PCT raises the dead sage; while the
    //    sage lives, dead DPS are the sage's job — the PCT never burns 8s of casting. ──

    [Fact]
    public void Raise_DeadHealer_AlwaysRaised()
    {
        Assert.Equal(VariantRaiseDecision.RaiseHealer,
            VariantBandRules.DecideRaise(Cfg(), deadHealerPresent: true, deadOtherPresent: false, livingHealerPresent: false));
        // Even with another healer alive — a dead healer is always the priority pickup.
        Assert.Equal(VariantRaiseDecision.RaiseHealer,
            VariantBandRules.DecideRaise(Cfg(), deadHealerPresent: true, deadOtherPresent: true, livingHealerPresent: true));
    }

    [Fact]
    public void Raise_DeadDps_LeftToTheLivingHealer()
    {
        Assert.Equal(VariantRaiseDecision.None,
            VariantBandRules.DecideRaise(Cfg(), deadHealerPresent: false, deadOtherPresent: true, livingHealerPresent: true));
    }

    [Fact]
    public void Raise_DeadDps_NoHealerAlive_VariantRaiseFallsBackIn()
    {
        Assert.Equal(VariantRaiseDecision.RaiseOther,
            VariantBandRules.DecideRaise(Cfg(), deadHealerPresent: false, deadOtherPresent: true, livingHealerPresent: false));
    }

    [Fact]
    public void Raise_DisabledInConfig_NeverFires()
    {
        var cfg = Cfg();
        cfg.UseRaise = false;

        Assert.Equal(VariantRaiseDecision.None,
            VariantBandRules.DecideRaise(cfg, deadHealerPresent: true, deadOtherPresent: true, livingHealerPresent: false));
    }
}
