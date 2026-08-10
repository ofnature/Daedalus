namespace Daedalus.Services.Rescue;

/// <summary>
/// Everything the sender-side broadcast decision needs, sampled each frame from the toon's OWN
/// BossMod hints (docs/rescue-plan.md). Own-hints is the tower/soak correctness rule: a zone
/// this toon is assigned to soak is not forbidden in its own hints, so a deliberate soaker
/// never broadcasts. Pure data so the policy is unit-testable.
/// </summary>
/// <param name="UnsafeStreakFrames">Consecutive frames (including this one) with the toon's
/// position unsafe — debounces single-frame hint flickers.</param>
/// <param name="ActivationInSeconds">Soonest forbidden-zone activation per the toon's own
/// BossMod (<c>ForbiddenZoneActivationInSeconds</c>; MaxValue when none).</param>
/// <param name="DashActive">The toon is mid-dash/forced movement — the dash resolves the
/// position before a pull could.</param>
public readonly record struct RescueBroadcastSituation(
    bool BroadcastEnabled,
    bool SelfAlive,
    bool InCombat,
    bool BossModAvailable,
    bool PositionSafe,
    int UnsafeStreakFrames,
    float ActivationInSeconds,
    bool DashActive);

/// <summary>
/// Pure gating for the "I won't make it" broadcast (rescue-plan Phase 0). BMR steering
/// normally clears zones with seconds to spare — a toon still standing in the bad inside the
/// panic window IS the won't-make-it signal; no pathfinding introspection needed. Ordered so
/// the returned reason names the FIRST thing that would have to change, like
/// <c>PhoenixDownPolicy</c>.
/// </summary>
public static class RescueBroadcastPolicy
{
    /// <summary>Still unsafe with this long (or less) to activation = broadcast.</summary>
    public const float PanicSeconds = 2.0f;

    /// <summary>Consecutive unsafe frames required before panicking — hint-flicker debounce.</summary>
    public const int MinUnsafeSamples = 3;

    /// <summary>Re-broadcast cadence while the condition holds (receivers key on freshness).</summary>
    public const float RebroadcastIntervalSeconds = 0.25f;

    public static (bool Broadcast, string Reason) Decide(in RescueBroadcastSituation s)
    {
        if (!s.BroadcastEnabled)
            return (false, "broadcast disabled for this toon");
        if (!s.SelfAlive)
            return (false, "self dead — raise territory, not rescue");
        if (!s.InCombat)
            return (false, "not in combat");
        if (!s.BossModAvailable)
            return (false, "BossMod unavailable — no timing source");
        if (s.PositionSafe)
            return (false, "position safe");
        if (s.UnsafeStreakFrames < MinUnsafeSamples)
            return (false, $"debouncing ({s.UnsafeStreakFrames}/{MinUnsafeSamples} unsafe frames)");
        if (s.ActivationInSeconds > PanicSeconds)
            return (false, $"{s.ActivationInSeconds:F1}s left — still time to walk out");
        if (s.DashActive)
            return (false, "dashing — the dash resolves the position first");

        return (true, "won't make it — broadcasting");
    }
}
