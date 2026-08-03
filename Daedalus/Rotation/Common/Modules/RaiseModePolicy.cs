using Daedalus.Config;

namespace Daedalus.Rotation.Common.Modules;

/// <summary>
/// Gives the Raise Priority setting its meaning. The dropdown shipped wired to nothing — no
/// rotation code read it — so all three modes silently behaved identically (found 2026-08-02).
/// <para>
/// The lever the modes control is the HARDCAST decision, because that is the expensive
/// commitment: 8 seconds planted with no healing output. The arithmetic is time-to-raise — a
/// Swiftcast raise lands when Swiftcast comes off cooldown, a hardcast lands in 8s — and the
/// modes pick how much healing continuity to trade for raise speed:
/// RaiseFirst waits only when Swiftcast is the strictly faster path (≤8s); Balanced gives it a
/// 10s window, accepting up to 2s of extra body-time to stay free to heal; HealFirst never
/// hardcasts in combat at all. Everyone hardcasts freely out of combat, where the cast is free.
/// </para>
/// </summary>
public static class RaiseModePolicy
{
    /// <summary>Hardcast raise cast time — the break-even point for waiting on Swiftcast.</summary>
    public const float HardcastSeconds = 8f;

    /// <summary>Balanced's extra grace over break-even, trading raise speed for healing uptime.</summary>
    public const float BalancedWaitSeconds = 10f;

    /// <summary>
    /// Maximum Swiftcast cooldown we are willing to wait out instead of hardcasting, in combat.
    /// Out of combat the caller must not wait at all — oGCDs never dispatch there, so waiting is
    /// the deadlock fixed earlier the same day.
    /// </summary>
    public static float SwiftcastWaitThresholdSeconds(RaiseExecutionMode mode) => mode switch
    {
        RaiseExecutionMode.RaiseFirst => HardcastSeconds,
        RaiseExecutionMode.HealFirst => float.PositiveInfinity,
        _ => BalancedWaitSeconds,
    };
}
