using Daedalus.Data;

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

    /// <summary>
    /// How close the farm approach walks to the target's hitbox edge before letting the
    /// rotation take over: melee reach for melee/tanks, comfortable cast range for everyone else.
    /// </summary>
    public static float ApproachStopYalms(uint jobId)
        => JobRegistry.IsMeleeDps(jobId) || JobRegistry.IsTank(jobId) ? 2.5f : 18f;
}
