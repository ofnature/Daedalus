using System;

namespace Daedalus.Rotation.ProteusCore.Helpers;

/// <summary>
/// Final Sting damage estimator (pure). Calibrated from ONE observed damage number (an unbuffed,
/// NON-CRIT hit of a known-potency spell — Sonic Boom 210p, or a real Final Sting 2000p test),
/// then scaled by potency ratio and the selected buff multipliers.
///
/// Deliberately estimates the NON-CRIT FLOOR: sting planning wants a guaranteed kill, and crits
/// only help. Final Sting is PHYSICAL (piercing) — Whistle's Harmonized (+80% physical) applies,
/// Bristle's Boost (+50% MAGIC) does NOT. This baseline/estimate is also the seed for the v3
/// fleet-sting math (stingersNeeded = ceil(bossHp / (estimate × safetyFactor))).
/// </summary>
public static class FinalStingCalculator
{
    /// <summary>Final Sting's potency (~2000, sheet-sourced via BLUActions).</summary>
    public const float FinalStingPotency = 2000f;

    // Multipliers (mage.blue / game descriptions; all multiplicative):
    public const float WaxingNocturneMult = 1.5f;   // Moon Flute: +50% damage dealt
    public const float HarmonizedMult = 1.8f;       // Whistle: next PHYSICAL spell +80%
    public const float OffGuardMult = 1.05f;        // target takes +5% (all damage)
    public const float BasicInstinctMult = 2.0f;    // solo duty: +100% damage dealt
    public const float MightyGuardMult = 0.6f;      // stance: -40% damage dealt (BI does NOT
                                                    // remove it — the +100% just outweighs it)

    /// <summary>
    /// Estimated non-crit Final Sting damage from a calibration observation.
    /// </summary>
    /// <param name="baselineDamage">Observed NON-CRIT damage of the calibration hit (unbuffed).</param>
    /// <param name="baselinePotency">The calibration spell's potency (Sonic Boom 210, Final Sting 2000).</param>
    public static float Estimate(
        float baselineDamage,
        float baselinePotency,
        bool waxingNocturne = false,
        bool harmonized = false,
        bool offGuard = false,
        bool basicInstinct = false,
        bool mightyGuard = false)
    {
        if (baselineDamage <= 0f || baselinePotency <= 0f)
            return 0f;

        var damage = baselineDamage * (FinalStingPotency / baselinePotency);
        if (waxingNocturne) damage *= WaxingNocturneMult;
        if (harmonized) damage *= HarmonizedMult;
        if (offGuard) damage *= OffGuardMult;
        if (basicInstinct) damage *= BasicInstinctMult;
        if (mightyGuard) damage *= MightyGuardMult;
        return damage;
    }

    /// <summary>
    /// How many simultaneous stingers a target needs: ceil(hp / (perSting × safety)). The safety
    /// factor under-credits each sting (default 0.75 in the v3 design) so variance can't leave
    /// the boss alive with the party dead.
    /// </summary>
    public static int StingersNeeded(long targetHp, float perStingDamage, float safetyFactor)
    {
        if (targetHp <= 0)
            return 0;
        var effective = perStingDamage * Math.Clamp(safetyFactor, 0.1f, 1f);
        if (effective <= 0f)
            return int.MaxValue;
        return (int)Math.Ceiling(targetHp / effective);
    }
}
