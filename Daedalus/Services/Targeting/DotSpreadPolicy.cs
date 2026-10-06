using System.Collections.Generic;

namespace Daedalus.Services.Targeting;

/// <summary>One enemy as the DoT-spread rule sees it.</summary>
/// <param name="Id">Game object id.</param>
/// <param name="FightingOurSide">Its target is someone on our side (a pulled mob, not a bystander or a stranger's).</param>
/// <param name="DotRemaining">Seconds left on the DoT on it; 0 when it has none.</param>
/// <param name="TimeToKill">Estimated seconds to death; float.MaxValue when unknown.</param>
public readonly record struct DotSpreadCandidate(ulong Id, bool FightingOurSide, float DotRemaining, float TimeToKill);

/// <summary>
/// Where a DoT goes when the targeting strategy's own pick cannot take it (dying, or already dotted):
/// another enemy in the fight that needs it and will live long enough — the one expected to live longest.
/// RSR parity; stopping at the strategy's pick left whole trash packs undotted under Tank Assist.
/// </summary>
public static class DotSpreadPolicy
{
    /// <param name="candidates">Valid enemies in range, the strategy's pick already removed.</param>
    /// <param name="refreshThreshold">A DoT with less than this left needs reapplying.</param>
    /// <param name="ttkCutoff">Skip enemies dying sooner than this; null when the time-to-kill check is off.</param>
    public static ulong? Choose(IEnumerable<DotSpreadCandidate> candidates, float refreshThreshold, float? ttkCutoff)
    {
        ulong? best = null;
        var bestTtk = float.MinValue;
        foreach (var c in candidates)
        {
            if (!c.FightingOurSide)
                continue;
            if (c.DotRemaining >= refreshThreshold)
                continue;
            if (ttkCutoff is { } cutoff && c.TimeToKill < cutoff)
                continue;
            if (best == null || c.TimeToKill > bestTtk)
            {
                best = c.Id;
                bestTtk = c.TimeToKill;
            }
        }

        return best;
    }
}
