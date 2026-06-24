using System;

namespace Olympus.Services.Action;

/// <summary>
/// Minimum spacing floor for charge-based GCD submissions. A charge-based GCD cannot
/// legitimately fire twice within one global GCD window; this blocks duplicate UseAction
/// calls when higher-level dedupe latches flap during the queue window.
/// </summary>
internal static class ChargeGcdSubmitGuard
{
    /// <summary>
    /// Returns true when a charge-based GCD was submitted too recently to be a legal second use.
    /// </summary>
    public static bool ShouldBlock(DateTime lastSubmitUtc, float gcdDurationSeconds, DateTime nowUtc)
    {
        if (lastSubmitUtc == DateTime.MinValue)
            return false;

        var floor = Math.Max(gcdDurationSeconds, 2.0f);
        return (nowUtc - lastSubmitUtc).TotalSeconds < floor;
    }
}
