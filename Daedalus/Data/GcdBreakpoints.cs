using System;
using System.Collections.Generic;

namespace Daedalus.Data;

/// <summary>
/// GCD tier math on top of <see cref="StatConversions.GcdSeconds"/> — the tier staircase, the
/// window the current speed value sits in, and the point cost to reach the next tier. Pure;
/// feeds the breakpoint panel and the optimizer's tier-crossing detection.
/// </summary>
public static class GcdBreakpoints
{
    public readonly record struct Tier(float GcdSeconds, int SpeedFrom, int SpeedTo);

    /// <summary>Upper bound for tier scans — far beyond any reachable speed total.</summary>
    private const int MaxSpeed = 6000;

    /// <summary>
    /// The tier containing <paramref name="speed"/> plus neighbors: [previous, current, next…],
    /// with <paramref name="tiersAfter"/> tiers past the current one. SpeedTo of the last tier
    /// is clamped to <see cref="MaxSpeed"/>.
    /// </summary>
    public static IReadOnlyList<Tier> Window(int speed, int level, float baseRecastSeconds = 2.5f, int tiersAfter = 2)
    {
        var mods = StatConversions.ModsFor(level);
        var floor = mods.Sub;
        speed = Math.Max(speed, floor);

        // Walk tier boundaries from the substat floor upward.
        var tiers = new List<Tier>();
        var tierStart = floor;
        var tierGcd = StatConversions.GcdSeconds(tierStart, level, baseRecastSeconds);
        for (var s = floor + 1; s <= MaxSpeed; s++)
        {
            var gcd = StatConversions.GcdSeconds(s, level, baseRecastSeconds);
            if (gcd < tierGcd)
            {
                tiers.Add(new Tier(tierGcd, tierStart, s - 1));
                tierStart = s;
                tierGcd = gcd;
            }
        }

        tiers.Add(new Tier(tierGcd, tierStart, MaxSpeed));

        // Slice: previous, current, and N after.
        var currentIndex = tiers.FindIndex(t => speed >= t.SpeedFrom && speed <= t.SpeedTo);
        if (currentIndex < 0)
            currentIndex = tiers.Count - 1;

        var from = Math.Max(0, currentIndex - 1);
        var to = Math.Min(tiers.Count - 1, currentIndex + tiersAfter);
        return tiers.GetRange(from, to - from + 1);
    }

    /// <summary>Points of SkS/SpS needed from <paramref name="speed"/> to enter the next tier.</summary>
    public static int PointsToNextTier(int speed, int level, float baseRecastSeconds = 2.5f)
    {
        var current = StatConversions.GcdSeconds(speed, level, baseRecastSeconds);
        for (var s = speed + 1; s <= MaxSpeed; s++)
        {
            if (StatConversions.GcdSeconds(s, level, baseRecastSeconds) < current)
                return s - speed;
        }

        return int.MaxValue;
    }
}
