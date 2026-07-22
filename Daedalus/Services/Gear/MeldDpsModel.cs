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
    public static double Multiplier(IReadOnlyDictionary<uint, int> totals, int level, uint speedStat)
    {
        var crit = Get(totals, GearStatIds.CriticalHit, StatConversions.ModsFor(level).Sub);
        var det = Get(totals, GearStatIds.Determination, StatConversions.ModsFor(level).Main);
        var dh = Get(totals, GearStatIds.DirectHit, StatConversions.ModsFor(level).Sub);
        var speed = Get(totals, speedStat, StatConversions.ModsFor(level).Sub);

        var critChance = StatConversions.CritChancePercent(crit, level) / 100.0;
        var critDamage = StatConversions.CritDamagePercent(crit, level) / 100.0;
        var dhRate = StatConversions.DirectHitRatePercent(dh, level) / 100.0;
        var detBonus = StatConversions.DeterminationBonusPercent(det, level) / 100.0;
        var gcd = StatConversions.GcdSeconds(speed, level);

        var critMult = 1.0 + critChance * (critDamage - 1.0);
        var dhMult = 1.0 + dhRate * 0.25;
        var detMult = 1.0 + detBonus;
        var gcdMult = 2.5 / Math.Max(0.1, gcd);

        return critMult * dhMult * detMult * gcdMult;
    }

    /// <summary>Relative DPS change (fraction, e.g. 0.0042 = +0.42%) between two stat totals.</summary>
    public static double DeltaPercent(
        IReadOnlyDictionary<uint, int> from,
        IReadOnlyDictionary<uint, int> to,
        int level,
        uint speedStat)
        => (Multiplier(to, level, speedStat) / Multiplier(from, level, speedStat) - 1.0) * 100.0;

    private static int Get(IReadOnlyDictionary<uint, int> totals, uint statId, int floor)
        => totals.TryGetValue(statId, out var value) && value > floor ? value : floor;
}
