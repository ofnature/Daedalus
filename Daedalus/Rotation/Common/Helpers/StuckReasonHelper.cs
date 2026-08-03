using System.Collections.Generic;
using System;
using System.Linq;

namespace Daedalus.Rotation.Common.Helpers;

/// <summary>
/// Formats the scheduler's per-candidate gate-fail reasons into a short, class-accurate "why nothing
/// fired" string for the Why Stuck panel. The scheduler already records exactly why each queued GCD was
/// rejected (Cooldown, ProcBuff, ComboStep, ActionStatus, Toggle, NotLearned, DispatchRejected, ...) —
/// this just surfaces it instead of the generic global-pause reason.
/// </summary>
public static class StuckReasonHelper
{
    private const int MaxShown = 4;

    /// <summary>
    /// Returns a "Stuck — ..." summary when the GCD was ready but no candidate dispatched and at least
    /// one candidate was rejected. Returns null when something fired, or when the queue was empty (the
    /// module's own debug state already explains why nothing was pushed: no target, out of combat, etc.).
    /// </summary>
    public static string? Describe(bool dispatched, IReadOnlyList<string> gateFailReasons)
    {
        if (dispatched || gateFailReasons.Count == 0)
            return null;

        // "Already submitted" is our own dup guard confirming the accepted cast is in flight —
        // pure noise, filtered. Game status 582 is NOT filtered, deliberately: it looked like
        // the same noise until a field log (2026-08-02) showed two ~12s GCD stalls whose ONLY
        // symptom was sustained 582 spam — hiding it would have hidden the stall entirely.
        var real = gateFailReasons.Where(r => !IsBenign(r)).ToList();
        if (real.Count == 0)
            return null;

        var joined = string.Join("; ", real.Take(MaxShown));
        return real.Count > MaxShown
            ? $"Stuck — {joined}; +{real.Count - MaxShown} more"
            : $"Stuck — {joined}";
    }

    /// <summary>Only our own dup guard is noise; a game-status reject is always worth showing.</summary>
    public static bool IsBenign(string reason)
        => reason.Contains("already submitted", StringComparison.OrdinalIgnoreCase);
}
