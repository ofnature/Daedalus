using Daedalus.Config;

namespace Daedalus.Services.Farm;

/// <summary>What the travel layer should do this tick (see <see cref="FarmMountPolicy.Decide"/>).</summary>
public enum FarmTravelAction
{
    /// <summary>No travel action — proceed with the normal ground/approach logic.</summary>
    Walk,

    /// <summary>Far leg ahead and unmounted: stop moving and cast the mount.</summary>
    CastMount,

    /// <summary>Mount cast issued — hold still until the mounted condition lands (or times out).</summary>
    AwaitMount,

    /// <summary>Mounted: ride (fly where legal) toward the destination.</summary>
    Ride,

    /// <summary>Mounted, airborne, at the destination mob — descend to the ground first
    /// (mid-air dismount = fall damage / stuck floating), then dismount.</summary>
    LandThenDismount,

    /// <summary>Mounted and close enough (or combat started) — get off the mount now.</summary>
    Dismount,
}

/// <summary>
/// Pure travel decisions for farm mode's mounted movement (docs/farm-mode.md §v4) — no Dalamud
/// types so every branch is unit-testable. The service owns the game reads (conditions, casts,
/// vNav) and feeds them in.
/// </summary>
public static class FarmMountPolicy
{
    /// <summary>
    /// One travel decision per poll tick.
    /// Combat always wins: mounted combat dismounts immediately, unmounted combat never mounts.
    /// Mob destinations dismount at <paramref name="dismountRangeYalms"/> (attacks fizzle mounted);
    /// spot destinations keep the mount — the next acquired mob triggers the dismount instead.
    /// </summary>
    /// <param name="mounted">ConditionFlag.Mounted.</param>
    /// <param name="inFlight">ConditionFlag.InFlight (airborne on a flying mount).</param>
    /// <param name="inCombat">ConditionFlag.InCombat — re-check at CAST time too, not just here.</param>
    /// <param name="mountCastPending">A mount cast was issued and the mounted condition hasn't landed yet.</param>
    /// <param name="mountSuppressed">Mount casts gave up recently (timeout/retry exhausted) — walk this leg.</param>
    /// <param name="distanceYalms">Distance to the destination (edge distance for mobs).</param>
    /// <param name="mountThresholdYalms">Legs longer than this mount up; shorter legs walk.</param>
    /// <param name="dismountRangeYalms">Mob-destination edge distance at which to get off.</param>
    /// <param name="destinationIsMob">True when traveling to an acquired mob, false for a patrol spot.</param>
    /// <param name="arriveToleranceYalms">Spot arrival tolerance — inside it there is no travel to do.</param>
    public static FarmTravelAction Decide(
        bool mounted,
        bool inFlight,
        bool inCombat,
        bool mountCastPending,
        bool mountSuppressed,
        float distanceYalms,
        float mountThresholdYalms,
        float dismountRangeYalms,
        bool destinationIsMob,
        float arriveToleranceYalms = FarmRoamPolicy.ArriveToleranceYalms)
    {
        if (inCombat)
            return mounted ? FarmTravelAction.Dismount : FarmTravelAction.Walk;

        if (mounted)
        {
            if (destinationIsMob && distanceYalms <= dismountRangeYalms)
                return inFlight ? FarmTravelAction.LandThenDismount : FarmTravelAction.Dismount;

            // Arrived at a patrol spot: nothing to ride toward — stay mounted and let the
            // normal scan/wait logic run (the next acquired mob handles the dismount).
            if (!destinationIsMob && distanceYalms <= arriveToleranceYalms)
                return FarmTravelAction.Walk;

            return FarmTravelAction.Ride;
        }

        if (mountCastPending)
            return FarmTravelAction.AwaitMount;

        if (!mountSuppressed && distanceYalms > mountThresholdYalms)
            return FarmTravelAction.CastMount;

        return FarmTravelAction.Walk;
    }

    /// <summary>
    /// Specific-mount selection with the unlock fallback: Specific mode only holds when a mount is
    /// actually picked AND unlocked on this toon — anything else rides the Roulette.
    /// </summary>
    public static bool UseSpecificMount(FarmMountMode mode, uint specificMountId, bool specificUnlocked)
        => mode == FarmMountMode.Specific && specificMountId != 0 && specificUnlocked;
}
