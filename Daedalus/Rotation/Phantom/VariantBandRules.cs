using Daedalus.Config;

namespace Daedalus.Rotation.Phantom;

public enum VariantRaiseDecision
{
    None,
    RaiseHealer,
    RaiseOther,
}

/// <summary>
/// Pure decision predicates for the variant duty-action executor
/// (docs/variant-actions-plan.md Phase 2). Fed live values by VariantActionLayer.
/// </summary>
public static class VariantBandRules
{
    /// <summary>Reapply the Spirit Dart DoT when our Sustained Damage has this long left.</summary>
    public const float DartRefreshSeconds = 3f;

    /// <summary>Don't waste the 30s DoT on a target dying sooner than this.</summary>
    public const float DartMinTtkSeconds = 8f;

    public static bool ShouldCure(VariantConfig cfg, float selfHpPct)
        => selfHpPct < cfg.CureHpPct;

    /// <summary>
    /// DoT maintenance — never on-cooldown spam (2.5s recast, 30s DoT), and never on a
    /// target about to die (TTK gate; float.MaxValue = unknown ⇒ fire).
    /// </summary>
    public static bool ShouldMaintainDart(VariantConfig cfg, float dotRemainingSeconds, float targetTtkSeconds)
        => cfg.UseSpiritDart
           && dotRemainingSeconds < DartRefreshSeconds
           && targetTtkSeconds >= DartMinTtkSeconds;

    public static bool ShouldRampart(VariantConfig cfg, bool inCombat, bool buffActive)
        => cfg.UseRampart && inCombat && (cfg.RampartSpamOnCooldown || !buffActive);

    /// <summary>
    /// The raise policy (user comp 2026-07-25: WAR/SAM/PCT + SGE): a dead healer is
    /// always raised (healers cannot slot Variant Raise — a DPS/tank is their lifeline);
    /// dead non-healers are LEFT to a living healer's own raise (don't burn 8s of DPS);
    /// only when no healer lives does the variant raise pick up the rest.
    /// </summary>
    public static VariantRaiseDecision DecideRaise(
        VariantConfig cfg, bool deadHealerPresent, bool deadOtherPresent, bool livingHealerPresent)
    {
        if (!cfg.UseRaise)
            return VariantRaiseDecision.None;
        if (deadHealerPresent)
            return VariantRaiseDecision.RaiseHealer;
        if (deadOtherPresent && !livingHealerPresent)
            return VariantRaiseDecision.RaiseOther;
        return VariantRaiseDecision.None;
    }
}
