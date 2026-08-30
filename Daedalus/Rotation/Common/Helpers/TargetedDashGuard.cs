using System.Numerics;
using Daedalus.Services.Positional.Navigation;

namespace Daedalus.Rotation.Common.Helpers;

/// <summary>
/// Safety guard for dashes aimed AT a target — Phantom Kick, "leap towards your target".
/// <para>
/// Two separate questions, answered by two separate sources:
/// <list type="bullet">
/// <item><b>Is there floor where we land?</b> The navmesh knows. A target standing over a hole
/// (or on scenery the mesh does not cover) returns no floor point, and a floor point far below
/// our feet is a drop — either way the leap ends in a pit.</item>
/// <item><b>Is the leap into a telegraph?</b> BossMod knows, via the same
/// <c>Hints.IsDashSafe</c> segment query its own dash tweaks use.</item>
/// </list>
/// </para>
/// <para>
/// BMR treats this dash as a FIXED distance along the target direction rather than a move to the
/// target — <c>ClassShared.cs</c> special-cases Phantom Kick for exactly that reason ("regular
/// dash check doesn't work since this one is awkwardly fixed distance"). The hazard sweep follows
/// BMR and asks about the whole dash length; the floor check stays on the target, because that is
/// where the leap is aimed and terrain 15y beyond a nearby boss is ground we never touch.
/// </para>
/// <para>
/// Fail-open when a source is absent: no vnavmesh means no opinion about floor, and refusing the
/// Monk's damage button because a movement plugin is not installed is a worse outcome than the
/// hole it guards. Known-bad answers still block.
/// </para>
/// </summary>
public static class TargetedDashGuard
{
    /// <summary>Phantom Kick's dash length (BMR models it as a fixed 15y along the target direction).</summary>
    public const float PhantomKickDashYalms = 15f;

    /// <summary>Floor this far below the player at the landing point is a pit, not a step down.</summary>
    public const float MaxLandingDropYalms = 3.0f;

    /// <summary>Below this separation there is no travel worth checking.</summary>
    private const float MinTravelYalms = 0.1f;

    public static bool IsTargetedDashSafe(
        Vector3 playerPosition,
        Vector3 targetPosition,
        float dashYalms,
        IVNavService? vNav,
        IBossModSafetyService? bossModSafety)
    {
        var toTarget = targetPosition - playerPosition;
        toTarget.Y = 0f;
        var horizontal = toTarget.Length();
        if (horizontal < MinTravelYalms)
            return true;

        if (vNav is { IsAvailable: true, IsNavReady: true })
        {
            if (!vNav.TryGetFloorPoint(targetPosition, out var floor))
                return false;

            if (playerPosition.Y - floor.Y > MaxLandingDropYalms)
                return false;
        }

        if (bossModSafety is null)
            return true;

        var landing = playerPosition + toTarget / horizontal * dashYalms;
        if (!bossModSafety.IsSegmentSafe(playerPosition, landing))
            return false;

        return bossModSafety.QueryPositionSafety(targetPosition)
            is not (PositionSafety.Unsafe or PositionSafety.Imminent);
    }
}
