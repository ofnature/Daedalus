namespace Daedalus.Services.Farm;

public enum FarmRoamAction
{
    /// <summary>Do nothing this tick (in transit, or waiting for respawns at the spot).</summary>
    Stay,

    /// <summary>Path to the active spot.</summary>
    MoveToSpot,

    /// <summary>Nothing to kill here for long enough — cycle to the next spot.</summary>
    AdvanceSpot,
}

/// <summary>
/// Pure decision logic for the farm loop — kept free of Dalamud types so it is unit-testable.
/// </summary>
public static class FarmRoamPolicy
{
    /// <summary>Distance at which the player counts as "at" a spot.</summary>
    public const float ArriveToleranceYalms = 5f;

    /// <summary>Roam decision when there is nothing to fight.</summary>
    public static FarmRoamAction Decide(
        bool navBusy,
        float distanceToSpotYalms,
        double secondsIdleAtSpot,
        double respawnWaitSeconds,
        int spotCount)
    {
        if (distanceToSpotYalms > ArriveToleranceYalms)
            return navBusy ? FarmRoamAction.Stay : FarmRoamAction.MoveToSpot;

        // At the spot with nothing up: rotate spots so respawn downtime is spent scanning
        // the next cluster instead of idling (single-spot profiles just wait).
        if (spotCount > 1 && secondsIdleAtSpot >= respawnWaitSeconds)
            return FarmRoamAction.AdvanceSpot;

        return FarmRoamAction.Stay;
    }

    public static int NextSpotIndex(int current, int spotCount)
        => spotCount <= 0 ? 0 : (current + 1) % spotCount;

    public static bool IsComplete(uint itemCount, int targetCount)
        => targetCount > 0 && itemCount >= (uint)targetCount;

    /// <summary>Edge distance at which every job can land a ranged tag (all casts/shots reach 25y).</summary>
    public const float TagRangeYalms = 18f;

    /// <summary>Melee-reach edge distance for the walk-in fallback.</summary>
    public const float MeleeStopYalms = 2.5f;

    /// <summary>
    /// How long to stand at tag range waiting for the rotation to tag the mob before walking to
    /// melee instead (kits with no ranged attack — MNK — or ranged pulls disabled on tanks).
    /// </summary>
    public const double TagWindowSeconds = 4.0;

    /// <summary>
    /// Farm-only pull tactic (never shared with other movement systems): approach to ranged tag
    /// distance, stand still and tag, let the mob run to us while the rotation shoots it, and
    /// only walk to melee when the tag window expires without the mob engaging.
    /// </summary>
    public static FarmApproachAction DecideApproach(
        bool targetEngagedOnUs,
        float edgeDistanceYalms,
        double secondsAtTagRange)
    {
        // Tagged: the mob is coming to us — stand and kill it on the way in.
        if (targetEngagedOnUs)
            return FarmApproachAction.Hold;

        if (edgeDistanceYalms > TagRangeYalms)
            return FarmApproachAction.MoveToTagRange;

        if (edgeDistanceYalms <= MeleeStopYalms)
            return FarmApproachAction.Hold;

        // At tag range: give the rotation a window to land the ranged tag before walking in.
        return secondsAtTagRange >= TagWindowSeconds
            ? FarmApproachAction.MoveToMelee
            : FarmApproachAction.Hold;
    }
}

public enum FarmApproachAction
{
    /// <summary>Stand still (tagged mob incoming, standing in the tag window, or already in reach).</summary>
    Hold,

    /// <summary>Walk until the target is within ranged tag distance.</summary>
    MoveToTagRange,

    /// <summary>Tag window expired without engagement — walk to melee reach.</summary>
    MoveToMelee,
}
