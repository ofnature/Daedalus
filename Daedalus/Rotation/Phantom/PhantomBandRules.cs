using Daedalus.Config;

namespace Daedalus.Rotation.Phantom;

/// <summary>
/// Pure decision predicates for the Phase 3 phantom bands (survival / mitigation /
/// interrupt / MP / party buffs). Kept free of game services so every rule is
/// unit-testable; <see cref="PhantomActionLayer"/> feeds them live values.
/// </summary>
public static class PhantomBandRules
{
    /// <summary>Self HP fraction below which the self-mits (Phantom Guard, Defend) fire.</summary>
    public const float SelfMitHpPct = 0.45f;

    /// <summary>Self HP fraction below which Pray fires (when configured as a heal).</summary>
    public const float PrayHpPct = 0.85f;

    public static bool ShouldUsePotion(PhantomConfig cfg, float selfHpPct, uint potionCount, bool inCombat)
        => inCombat && potionCount > 0 && selfHpPct < cfg.ChemistPotionHpPct;

    public static bool ShouldUseElixir(PhantomConfig cfg, float selfHpPct, uint elixirCount, bool inCombat)
        => inCombat && elixirCount > 0 && selfHpPct < cfg.ChemistElixirPartyHpPct;

    public static bool ShouldUseChakraForHp(PhantomConfig cfg, float selfHpPct, bool inCombat)
        => inCombat && selfHpPct < cfg.MonkChakraHpPct;

    public static bool ShouldUseChakraForMp(PhantomConfig cfg, uint currentMp, uint maxMp, bool inCombat)
        => inCombat && maxMp > 0 && currentMp < cfg.MonkChakraMpThreshold;

    public static bool ShouldUseEther(PhantomConfig cfg, uint currentMp, uint maxMp, uint potionCount, bool inCombat)
        => inCombat && maxMp > 0 && potionCount > 0 && currentMp < cfg.ChemistEtherMpThreshold;

    public static bool ShouldSelfMit(float selfHpPct, bool inCombat)
        => inCombat && selfHpPct < SelfMitHpPct;

    public static bool ShouldInterrupt(bool targetIsCasting, bool castInterruptible, float distanceYalms, float maxRangeYalms)
        => targetIsCasting && castInterruptible && distanceYalms <= maxRangeYalms;

    public static bool ShouldResuscitate(PhantomConfig cfg, float selfHpPct)
        => selfHpPct < cfg.FreelancerResuscitationHpPct;

    public static bool ShouldPray(PhantomConfig cfg, float selfHpPct)
        => cfg.KnightPrayAsHeal && selfHpPct < PrayHpPct;

    /// <summary>Thief Steal is an execute — fire on low-HP targets regardless of burst.</summary>
    public const float StealTargetHpPct = 0.25f;

    public static bool ShouldSteal(float targetHpPct) => targetHpPct < StealTargetHpPct;

    /// <summary>
    /// Damage-band burst hold. Only holds when burst data actually EXISTS
    /// (a burst window has been observed: <paramref name="secondsSinceLastBurstStart"/> ≥ 0) —
    /// solo field farming with no raid buffs must never starve the damage band.
    /// </summary>
    public static bool ShouldHoldDamage(bool saveForBurst, bool inBurstWindow, float secondsSinceLastBurstStart)
        => saveForBurst && !inBurstWindow && secondsSinceLastBurstStart >= 0f;

    /// <summary>Phantom Kick dashes to the target — cap the dash distance (config).</summary>
    public static bool ShouldPhantomKick(float distanceYalms, float maxRangeYalms)
        => distanceYalms <= maxRangeYalms;
}
