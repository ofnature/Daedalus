using System.Numerics;

namespace Daedalus.Rotation.Roleplay;

/// <summary>
/// One action the boss engine wants the character you are playing to use (<c>Minerva.Hints.RoleplayActions</c>): the
/// action, who it is for (0 for none), Minerva's priority, where to aim a ground-targeted one, and a facing in radians
/// (NaN for none). Minerva sends them best first.
/// </summary>
public readonly record struct RoleplayRequest(uint ActionId, ulong TargetId, float Priority, Vector3 TargetPos, float FacingRad)
{
    public bool GroundTargeted => TargetPos != default;
}

/// <summary>What the layer knows about a request's action this frame, from the game's Action sheet and action manager.</summary>
public readonly record struct RoleplayActionFacts(bool IsGcd, float CastTime, bool Ready);

/// <summary>
/// Playing someone else in a quest battle: Y'shtola in The Will of the Moon, Alphinaud, Hien, Nyelbert. The job's own
/// actions are gone from the bar, so the job rotation has nothing to press, and the boss engine's module knows the
/// fight -- heal Hien harder while Daidukul casts Tranquil Annihilation. Minerva decides; this presses.
/// <para>The Will of the Moon, 2026-09-29: Hien fell to 15% and the user healed him by hand ("that will fail the solo
/// duty"). "We will need full kits for all the solo fights not as the warrior of light."</para>
/// </summary>
public static class RoleplayActionPolicy
{
    /// <summary>
    /// The layer takes the frame over: switched on, in combat, and the engine named something. The job's modules then
    /// stand down for the frame, whether or not anything is ready to press.
    /// </summary>
    public static bool TakesOver(bool enabled, bool inCombat, int requestCount)
        => enabled && inCombat && requestCount > 0;

    /// <summary>The scheduler priority of the <paramref name="index"/>-th request: the engine's order is the kit's order
    /// of preference, and the scheduler dispatches ascending.</summary>
    public static int SchedulerPriority(int index) => index;

    /// <summary>
    /// Why this request is not queued this frame, or null to queue it. Range, a dead target and the global cooldown are
    /// the scheduler's own gates and are left to it.
    /// </summary>
    public static string? Reject(in RoleplayRequest request, in RoleplayActionFacts facts, bool isMoving)
    {
        if (request.ActionId == 0)
            return "no action";
        if (request.TargetId == 0 && !request.GroundTargeted)
            return "no target";
        if (facts.CastTime > 0f && isMoving)
            return "needs a hard cast (moving)";
        // oGCDs are not ready-checked by the scheduler; GCDs are, through the queue window
        if (!facts.IsGcd && !facts.Ready)
            return "on cooldown";
        // the scheduler places ground targets for oGCDs only
        if (request.GroundTargeted && facts.IsGcd)
            return "ground-targeted GCD (not yet supported)";
        return null;
    }
}
