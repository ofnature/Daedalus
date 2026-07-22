using System;

namespace Daedalus.Data;

/// <summary>
/// Substat → derived-percentage conversions, ported from the community-verified formulas used by
/// Kouzukii/ffxiv-characterstatus-refined (the plugin that replaces the Character window with
/// these exact numbers). ONE source of truth for the meld optimizer: the aggregate panel's
/// derived column, the GCD breakpoint sidebar, and the sweep's DPS model all call this class so
/// they can never disagree.
///
/// All formulas are floor-based on 0.1% units: rate = floor(coeff × (stat − base) / div + offset).
/// Level modifiers (main-stat base, substat base, divisor) verified against in-game Character
/// windows — level-90 anchors from a live screenshot 2026-07-22 (Crit 1576 → 17.3%/152.3%,
/// Det 1352 → +7.0%, DH 1076 → 19.5%, SkS 803 → +2.7%), level-100 bases by definition
/// (420 substat → 5.0% crit chance, 2.50 GCD). // verified 2026-07-22
/// </summary>
public static class StatConversions
{
    public readonly record struct LevelMods(int Main, int Sub, int Div);

    /// <summary>Milestone level modifiers; lookups clamp down to the nearest milestone.</summary>
    private static readonly (int Level, LevelMods Mods)[] LevelTable =
    {
        (50, new LevelMods(202, 341, 341)),
        (60, new LevelMods(218, 354, 600)),
        (70, new LevelMods(292, 364, 900)),
        (80, new LevelMods(340, 380, 1300)),
        (90, new LevelMods(390, 400, 1900)),
        (100, new LevelMods(440, 420, 2780)),
    };

    public static LevelMods ModsFor(int level)
    {
        var best = LevelTable[0].Mods;
        foreach (var (milestone, mods) in LevelTable)
        {
            if (level >= milestone)
                best = mods;
            else
                break;
        }

        return best;
    }

    /// <summary>Critical hit chance in percent (base 5.0%).</summary>
    public static float CritChancePercent(int crit, int level)
    {
        var m = ModsFor(level);
        return MathF.Floor(200f * (crit - m.Sub) / m.Div + 50f) / 10f;
    }

    /// <summary>Critical hit damage multiplier in percent (base 140.0%).</summary>
    public static float CritDamagePercent(int crit, int level)
    {
        var m = ModsFor(level);
        return MathF.Floor(200f * (crit - m.Sub) / m.Div + 1400f) / 10f;
    }

    /// <summary>Direct hit rate in percent (base 0%).</summary>
    public static float DirectHitRatePercent(int dh, int level)
    {
        var m = ModsFor(level);
        return MathF.Floor(550f * (dh - m.Sub) / m.Div) / 10f;
    }

    /// <summary>Determination damage bonus in percent (base 0%). NOTE: uses the MAIN-stat base.</summary>
    public static float DeterminationBonusPercent(int det, int level)
    {
        var m = ModsFor(level);
        return MathF.Floor(140f * (det - m.Main) / m.Div) / 10f;
    }

    /// <summary>SkS/SpS damage-over-time / auto scalar bonus in percent (base 0%).</summary>
    public static float SpeedBonusPercent(int speed, int level)
    {
        var m = ModsFor(level);
        return MathF.Floor(130f * (speed - m.Sub) / m.Div) / 10f;
    }

    /// <summary>Tenacity outgoing-damage/mitigation bonus in percent (base 0%).</summary>
    public static float TenacityBonusPercent(int tenacity, int level)
    {
        var m = ModsFor(level);
        return MathF.Floor(112f * (tenacity - m.Sub) / m.Div) / 10f;
    }

    /// <summary>Piety MP regen per server tick (base 200).</summary>
    public static int PietyMpPerTick(int piety, int level)
    {
        var m = ModsFor(level);
        return 200 + (int)MathF.Floor(150f * (piety - m.Main) / m.Div);
    }

    /// <summary>
    /// GCD in seconds for a base 2.5s recast: the double-floor formula that produces the tier
    /// staircase (2.50 → 2.49 → …). Feeds the breakpoint sidebar AND the optimizer's
    /// tier-crossing detection.
    /// </summary>
    public static float GcdSeconds(int speed, int level, float baseRecastSeconds = 2.5f)
    {
        var m = ModsFor(level);
        var speedScalar = (int)MathF.Floor(130f * (speed - m.Sub) / m.Div);
        var recastMs = (int)(baseRecastSeconds * 1000f);
        var gcd10Ms = (int)Math.Floor((long)recastMs * (1000 - speedScalar) / 10000.0);
        return gcd10Ms / 100f;
    }
}
