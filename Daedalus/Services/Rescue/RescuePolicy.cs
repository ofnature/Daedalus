namespace Daedalus.Services.Rescue;

/// <summary>
/// Everything the healer-side fire decision needs (docs/rescue-plan.md). The healer never
/// evaluates the TARGET's position against its own hints — that is the sender's call (its
/// hints are the only ones that exclude assigned soaks). The healer asserts only what it can
/// know locally: its own spot is genuinely safe to pull INTO, the action is available, the
/// target is pullable, and the request is fresh. Pure data so the policy is unit-testable.
/// </summary>
/// <param name="RequestAgeSeconds">Seconds since the freshest RescueNeeded for this target.</param>
/// <param name="ActivationRemainingSeconds">Sender's ms-to-activation minus the request age —
/// the pull must land before this reaches zero.</param>
/// <param name="SecondsSinceClaimByOther">Seconds since another healer's RescueClaim for this
/// target (MaxValue when none seen).</param>
/// <param name="ElectionSatisfied"><c>RescueElection.MayFire</c> for this healer's rank.</param>
/// <param name="TargetKnockbackImmune">Healer's LOCAL status read (Surecast / Arm's Length) —
/// the sender's flag is only a hint.</param>
/// <param name="SelfActivationInSeconds">Soonest activation per the HEALER's own BossMod —
/// "safety" that activates in a second is not safety.</param>
/// <param name="HeightDeltaYalms">|target Y − self Y| — the ledge/platform guard; BMR models
/// zones, not geometry.</param>
/// <param name="SecondsSinceTargetLastPulled">Per-target re-pull spacing (MaxValue when never).</param>
public readonly record struct RescueSituation(
    bool AutoRescueEnabled,
    bool SelfAlive,
    bool RescueLearned,
    bool RescueReady,
    float RequestAgeSeconds,
    float ActivationRemainingSeconds,
    float SecondsSinceClaimByOther,
    bool ElectionSatisfied,
    bool TargetAlive,
    bool TargetInLocalParty,
    float TargetDistanceYalms,
    bool TargetKnockbackImmune,
    bool SelfPositionSafe,
    float SelfActivationInSeconds,
    float HeightDeltaYalms,
    float SecondsSinceTargetLastPulled);

/// <summary>
/// Pure gating for the Rescue pull (rescue-plan Phase 0). Ordered so the returned reason
/// names the FIRST thing that would have to change — same idea as <c>PhoenixDownPolicy</c>
/// and the scheduler's GateFailReasons.
/// </summary>
public static class RescuePolicy
{
    /// <summary>1y inside Rescue's real 30y so server-side position drift can't void the cast.</summary>
    public const float MaxPullRangeYalms = 29f;

    /// <summary>Below this much time to activation the pull lands at/after the hit — the server
    /// snapshots the target's position when the effect applies, so a late Rescue rescues nobody
    /// and burns 120s of cooldown.</summary>
    public const float AbortSeconds = 0.4f;

    /// <summary>The healer's own spot must not activate within this — it is the destination.</summary>
    public const float DestSafetyActivationSeconds = 2.5f;

    /// <summary>Ledge/platform guard: refuse cross-height pulls.</summary>
    public const float MaxHeightDeltaYalms = 3f;

    /// <summary>A toon re-entering the bad twice in ten seconds has a problem Rescue can't fix.</summary>
    public const float PerTargetRepullCooldownSeconds = 10f;

    /// <summary>After another healer claims the pull, stand down this long.</summary>
    public const float ClaimHoldOffSeconds = 3f;

    /// <summary>A RescueNeeded older than this is expired — the toon escaped or died. Senders
    /// re-broadcast every <see cref="RescueBroadcastPolicy.RebroadcastIntervalSeconds"/>, so a
    /// live danger is never older than one dropped datagram.</summary>
    public const float RequestTtlSeconds = 0.75f;

    public static (bool Fire, string Reason) Decide(in RescueSituation s)
    {
        if (!s.AutoRescueEnabled)
            return (false, "auto rescue disabled");
        if (!s.SelfAlive)
            return (false, "self dead");
        if (!s.RescueLearned)
            return (false, "Rescue not learned");
        if (!s.RescueReady)
            return (false, "Rescue on cooldown");
        if (s.RequestAgeSeconds > RequestTtlSeconds)
            return (false, "request stale — toon escaped or died");
        if (s.SecondsSinceClaimByOther < ClaimHoldOffSeconds)
            return (false, "another healer claimed this pull");
        if (!s.ElectionSatisfied)
            return (false, "holding for a higher-ranked healer");
        if (!s.TargetAlive)
            return (false, "target died");
        if (!s.TargetInLocalParty)
            return (false, "target not in this party");
        if (s.TargetKnockbackImmune)
            return (false, "target knockback-immune — the pull would no-op");
        if (s.TargetDistanceYalms > MaxPullRangeYalms)
            return (false, $"target {s.TargetDistanceYalms:F0}y away — beyond {MaxPullRangeYalms:F0}y");
        if (s.HeightDeltaYalms > MaxHeightDeltaYalms)
            return (false, "height gap — ledge guard");
        if (!s.SelfPositionSafe)
            return (false, "own position unsafe — nowhere good to pull to");
        if (s.SelfActivationInSeconds <= DestSafetyActivationSeconds)
            return (false, $"own spot activates in {s.SelfActivationInSeconds:F1}s — not a destination");
        if (s.ActivationRemainingSeconds <= AbortSeconds)
            return (false, "too late — the pull would land after the hit");
        if (s.SecondsSinceTargetLastPulled < PerTargetRepullCooldownSeconds)
            return (false, "target was pulled seconds ago — not chain-yanking");

        return (true, "firing");
    }
}
