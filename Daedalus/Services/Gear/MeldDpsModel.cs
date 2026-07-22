using System;
using System.Collections.Generic;
using Daedalus.Data;
using Daedalus.Models.Gear;

namespace Daedalus.Services.Gear;

/// <summary>
/// The substat DPS multiplier model shared by the GCD "worth it?" verdict (phase 4) and the
/// meld sweep ranking (phase 5) — one model so the panel and the optimizer can never disagree.
/// Standard community multiplicative form on top of <see cref="StatConversions"/>:
///
///   mult = critMult × dhMult × detMult × gcdUptimeMult
///   critMult = 1 + chance × (critDamage − 1)
///   dhMult   = 1 + dhRate × 0.25
///   detMult  = 1 + detBonus
///   gcdUptimeMult = baseGcd / currentGcd   (more casts per unit time; tier-stepped)
///
/// Deliberately ignores DoT/auto speed scalars and job-specific weights beyond tiering — good
/// enough to RANK meld distributions (relative deltas), not a sim. // verified 2026-07-22
/// </summary>
public static class MeldDpsModel
{
    /// <param name="speedValued">
    /// Whether GCD-tier throughput counts (speed-priority jobs like BLM). For everyone else,
    /// speed is deliberately valued at ZERO: the naive uptime multiplier makes speed dominate
    /// every other substat (+0.4%/tier), which is exactly the trap The Balance guidance exists
    /// to prevent — speed breaks 2-min burst alignment and resource loops, so "hold at base
    /// tier" is the rule and comfort tiers are a player choice, not an optimizer output.
    /// </param>
    public static double Multiplier(IReadOnlyDictionary<uint, int> totals, int level, uint speedStat, bool speedValued = false)
    {
        // Totals are GEAR-ONLY; the character's naked base sits under them (420/440 at cap).
        var crit = CharacterTotal(totals, GearStatIds.CriticalHit, level);
        var det = CharacterTotal(totals, GearStatIds.Determination, level);
        var dh = CharacterTotal(totals, GearStatIds.DirectHit, level);
        var speed = CharacterTotal(totals, speedStat, level);

        var critChance = StatConversions.CritChancePercent(crit, level) / 100.0;
        var critDamage = StatConversions.CritDamagePercent(crit, level) / 100.0;
        var dhRate = StatConversions.DirectHitRatePercent(dh, level) / 100.0;
        var detBonus = StatConversions.DeterminationBonusPercent(det, level) / 100.0;
        var gcd = StatConversions.GcdSeconds(speed, level);

        var critMult = 1.0 + critChance * (critDamage - 1.0);
        var dhMult = 1.0 + dhRate * 0.25;
        var detMult = 1.0 + detBonus;
        var gcdMult = speedValued ? 2.5 / Math.Max(0.1, gcd) : 1.0;

        return critMult * dhMult * detMult * gcdMult;
    }

    /// <summary>Relative DPS change (percent, e.g. 0.42 = +0.42%) between two stat totals.</summary>
    public static double DeltaPercent(
        IReadOnlyDictionary<uint, int> from,
        IReadOnlyDictionary<uint, int> to,
        int level,
        uint speedStat,
        bool speedValued = false)
        => (Multiplier(to, level, speedStat, speedValued) / Multiplier(from, level, speedStat, speedValued) - 1.0) * 100.0;

    /// <summary>Gear total + naked-character floor = the value the conversion formulas expect.</summary>
    public static int CharacterTotal(IReadOnlyDictionary<uint, int> totals, uint statId, int level)
        => StatConversions.SubstatFloor(statId, level) + (totals.TryGetValue(statId, out var value) ? value : 0);
}
