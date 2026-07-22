using System;
using System.Numerics;
using Daedalus.Services.Positional.Navigation;

namespace Daedalus.Rotation.Common.Helpers;

/// <summary>
/// Safety guard for fixed-distance FORWARD dashes (PCT Smudge: 15y in the facing direction).
/// Field report 2026-07-20: the naive "weave Smudge whenever moving" fired on BMR's micro-adjust
/// steps — dashing 15y past the destination (BMR then walked the toon back, churning movement and
/// interrupting casts) and, worse, straight off arena ledges into the abyss. A dash is only safe
/// when ALL of:
/// <list type="bullet">
/// <item>a real vNav travel leg is running — BMR input-injection steps and manual strafing never
/// run vnavmesh paths, so they can never trigger a dash;</item>
/// <item>the toon's actual movement direction aligns with its facing (vNav turns the character
/// down the path on travel legs; in combat, auto-face points AT the boss while strafing — a
/// forward dash there would launch the caster at/past the target);</item>
/// <item>the navmesh has floor at the dash midpoint AND landing, within a small height delta of
/// the current position (off-mesh or a big drop = ledge/abyss — fail closed, including when
/// vnavmesh is not available);</item>
/// <item>BMR flags neither sample point as hazardous.</item>
/// </list>
/// Static sampling state (last-position velocity estimate) follows the single-local-player
/// pattern used by the other rotation statics.
/// </summary>
public static class ForwardDashGuard
{
    /// <summary>PCT Smudge dash length (game tooltip: "Quickly dash 15 yalms forward").</summary>
    public const float SmudgeDashYalms = 15f;

    /// <summary>Max floor-height difference (yalms) between here and a dash sample point.</summary>
    public const float MaxFloorHeightDeltaYalms = 2.0f;

    /// <summary>Movement direction must align with facing at least this much (cos ~32°).</summary>
    public const float MinFacingAlignmentDot = 0.85f;

    /// <summary>A velocity sample older than this is stale (lag spike / first frame).</summary>
    public const float MaxSampleAgeSeconds = 0.5f;

    /// <summary>Test seam.</summary>
    internal static Func<DateTime> UtcNow = () => DateTime.UtcNow;

    private static Vector3 _lastSamplePos;
    private static DateTime _lastSampleUtc = DateTime.MinValue;

    internal static void ResetForTest()
    {
        _lastSamplePos = default;
        _lastSampleUtc = DateTime.MinValue;
        UtcNow = () => DateTime.UtcNow;
    }

    public static bool IsForwardDashSafe(
        Vector3 playerPosition,
        float facingRadians,
        float dashYalms,
        IVNavService? vNav,
        IBossModSafetyService? bossModSafety)
    {
        // Always take the velocity sample first so the frame-to-frame cadence survives early-outs.
        var moveDir = SampleMoveDirection(playerPosition);
        if (moveDir is not { } dir)
            return false;

        // Fail closed without navmesh knowledge — the entire point is never dashing blind.
        if (vNav is null || !vNav.IsAvailable || !vNav.IsNavReady)
            return false;

        // Travel legs only.
        if (!vNav.IsPathRunning)
            return false;

        var forward = new Vector3(MathF.Sin(facingRadians), 0f, MathF.Cos(facingRadians));
        if (Vector3.Dot(dir, forward) < MinFacingAlignmentDot)
            return false;

        // Sample the dash line at midpoint and landing — a gap midway swallows the toon too.
        Span<float> fractions = stackalloc float[] { 0.5f, 1f };
        foreach (var fraction in fractions)
        {
            var point = playerPosition + forward * (dashYalms * fraction);

            if (!vNav.TryGetFloorPoint(point, out var floor))
                return false;

            if (MathF.Abs(floor.Y - playerPosition.Y) > MaxFloorHeightDeltaYalms)
                return false;

            if (bossModSafety != null
                && bossModSafety.QueryPositionSafety(floor) is PositionSafety.Unsafe or PositionSafety.Imminent)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Horizontal unit movement direction from the last two calls' positions; null when this is
    /// the first sample, the previous one is stale, or the toon is not actually translating.
    /// </summary>
    private static Vector3? SampleMoveDirection(Vector3 playerPosition)
    {
        var now = UtcNow();
        var prevPos = _lastSamplePos;
        var prevUtc = _lastSampleUtc;
        _lastSamplePos = playerPosition;
        _lastSampleUtc = now;

        var ageSeconds = (now - prevUtc).TotalSeconds;
        if (ageSeconds <= 0 || ageSeconds > MaxSampleAgeSeconds)
            return null;

        var delta = playerPosition - prevPos;
        delta.Y = 0f;
        var distSq = delta.LengthSquared();
        if (distSq < 1e-6f)
            return null;

        return delta / MathF.Sqrt(distSq);
    }
}
