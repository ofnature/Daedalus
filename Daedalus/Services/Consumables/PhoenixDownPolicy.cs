namespace Daedalus.Services.Consumables;

/// <summary>
/// Everything the Phoenix Down decision needs, gathered by <see cref="PhoenixDownService"/>
/// from live game state. Pure data so the policy is unit-testable.
/// </summary>
public readonly record struct PhoenixDownSituation(
    bool Enabled,
    bool InCombat,
    bool SelfAlive,
    bool SelfCasting,
    bool SelfIsTank,
    bool SelfIsDesignatedOffTank,
    int LivingOthers,
    bool HealersPresent,
    bool AllHealersDead,
    bool TargetFound,
    float TargetDistanceYalms,
    uint ItemCount,
    double SecondsSinceOwnUse,
    double SecondsSinceOwnAttempt,
    double SecondsSinceForeignClaim,
    bool IsMoving);

/// <summary>
/// Pure gating for the Phoenix Down safety net (lan-ipc-plan Phase 3): when every healer in
/// the party is dead, a toon hardcasts item 4570 on the nearest dead healer. Ordered so the
/// returned reason names the FIRST thing that would have to change — the same idea as the
/// scheduler's GateFailReasons.
/// </summary>
public static class PhoenixDownPolicy
{
    /// <summary>Phoenix Down cast range (Action row 43336).</summary>
    public const float RangeYalms = 15f;

    /// <summary>Item recast (360s, medicine group). Tracked locally so we don't spam refusals.</summary>
    public const float RecastSeconds = 360f;

    /// <summary>After the game refuses a use (blocked duty, recast we can't see), wait this long.</summary>
    public const float RetryBackoffSeconds = 10f;

    /// <summary>
    /// After ANOTHER toon broadcasts it is casting one, hold off this long — the 8s cast plus
    /// slack. Stops the whole fleet burning an item each on the same corpse.
    /// </summary>
    public const float ClaimHoldOffSeconds = 12f;

    /// <summary>
    /// The cast is 8 seconds and planted — starting it during BMR micro-pauses just gets it
    /// interrupted, so require a slightly longer still window than the rotation's cast grace.
    /// </summary>
    public const float MovementGraceSeconds = 0.5f;

    public static (bool Fire, string Reason) Decide(in PhoenixDownSituation s)
    {
        if (!s.Enabled)
            return (false, "disabled in settings");
        if (!s.SelfAlive)
            return (false, "self dead");
        if (!s.InCombat)
            return (false, "not in combat — normal raises own this");
        if (!s.HealersPresent)
            return (false, "no healers in party");
        if (!s.AllHealersDead)
            return (false, "a healer lives");

        // User rule (2026-08-03): the tank never stops to hardcast this while anyone else is
        // alive — 8 planted seconds on the MT is how the wipe finishes. A designated off-tank
        // (LAN tank-swap role) is not anchoring the boss and may cast.
        if (s.SelfIsTank && !s.SelfIsDesignatedOffTank && s.LivingOthers > 0)
            return (false, "tank holds Phoenix Down unless last alive");

        if (s.ItemCount == 0)
            return (false, "no Phoenix Down in inventory");
        if (s.SecondsSinceOwnUse < RecastSeconds)
            return (false, $"item recast rolling ({RecastSeconds - s.SecondsSinceOwnUse:F0}s)");
        if (s.SecondsSinceOwnAttempt < RetryBackoffSeconds)
            return (false, "backing off after a refused use");
        if (s.SecondsSinceForeignClaim < ClaimHoldOffSeconds)
            return (false, "another toon is casting one");
        if (!s.TargetFound)
            return (false, "no raisable dead healer (raise already pending?)");
        if (s.TargetDistanceYalms > RangeYalms)
            return (false, $"dead healer {s.TargetDistanceYalms:F0}y away — out of {RangeYalms:F0}y range");
        if (s.SelfCasting)
            return (false, "already casting");
        if (s.IsMoving)
            return (false, "moving — the 8s hardcast needs stillness");

        return (true, "firing");
    }
}
