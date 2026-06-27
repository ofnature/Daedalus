using System;
using System.Numerics;
using Daedalus.Services.Positional.Navigation;

namespace Daedalus.Services.Positional;

/// <summary>
/// Inputs for computing a clamped positional stand point (pure geometry, no IPC).
/// </summary>
public readonly record struct MeleeApproachStandRequest(
    Vector3 PlayerPosition,
    float PlayerHitboxRadius,
    Vector3 TargetPosition,
    float TargetHitboxRadius,
    float GcdRemainingSeconds = float.NaN,
    float StandRadiusOffset = PositionalMovementConstants.DefaultStandRadiusOffset);

public readonly record struct PositionalStandRequest(
    Vector3 PlayerPosition,
    float PlayerHitboxRadius,
    Vector3 TargetPosition,
    float TargetHitboxRadius,
    float TargetRotationRadians,
    PositionalType RequiredPositional,
    float GcdRemainingSeconds = float.NaN,
    float StandRadiusOffset = PositionalMovementConstants.DefaultStandRadiusOffset,
    float FloorY = float.NaN,
    /// <summary>
    /// When > 0, targets the flank/rear boundary ± this margin instead of the arc center.
    /// Reduces travel distance for jobs with back-to-back flank/rear switches (e.g. MNK).
    /// </summary>
    float BoundaryBiasRadians = 0f);

/// <summary>
/// Computes ideal rear/flank stand coordinates and clamps horizontal travel to the remaining
/// GCD movement budget. Navmesh floor snap is a separate concern (<see cref="IVNavService.SnapToFloor"/>).
/// </summary>
public static class PositionalStandCalculator
{
    /// <summary>
    /// Computes a stand position: ideal point on the positional arc at melee distance,
    /// then clamps horizontal displacement to
    /// <c>min(distToIdeal, (GcdRemaining − buffer) × moveSpeed)</c>, or
    /// <see cref="PositionalMovementConstants.MaxMoveYalmsPerGcdWindow"/> when GCD time is unknown.
    /// </summary>
    public static Vector3 Calculate(in PositionalStandRequest request)
    {
        var ideal = ComputeIdealStandPoint(in request);
        var distToIdeal = HorizontalDistance(request.PlayerPosition, ideal);
        var maxMove = ComputeMaxHorizontalMoveYalms(distToIdeal, request.GcdRemainingSeconds);
        var clamped = ClampHorizontalDisplacement(request.PlayerPosition, ideal, maxMove);
        var floorY = float.IsNaN(request.FloorY) ? request.TargetPosition.Y : request.FloorY;
        return SnapToFloorPlane(clamped, floorY);
    }

    /// <summary>
    /// Stand point on the player→target line at melee distance (burst approach / gap close).
    /// Clamped to one GCD of movement for positional weaving.
    /// </summary>
    public static Vector3 CalculateMeleeApproach(in MeleeApproachStandRequest request)
    {
        var ideal = ComputeMeleeApproachPoint(in request);
        var distToIdeal = HorizontalDistance(request.PlayerPosition, ideal);
        var maxMove = ComputeMaxHorizontalMoveYalms(distToIdeal, request.GcdRemainingSeconds);
        var clamped = ClampHorizontalDisplacement(request.PlayerPosition, ideal, maxMove);
        return SnapToFloorPlane(clamped, request.TargetPosition.Y);
    }

    /// <summary>
    /// Full-distance melee stand for burst gap-close — vNav runs the entire path in one queue.
    /// </summary>
    public static Vector3 CalculateBurstMeleeApproach(in MeleeApproachStandRequest request)
    {
        var ideal = ComputeMeleeApproachPoint(in request);
        return SnapToFloorPlane(ideal, request.TargetPosition.Y);
    }

    /// <summary>
    /// Max-melee maintenance stand point: the point on the current target→player bearing at the safe
    /// max-melee distance. Unlike <see cref="ComputeMeleeApproachPoint"/> this always projects to the stand
    /// ring in either direction — backing the character out when hugging and pulling it in when drifted out
    /// of range — rather than holding position. Drives both the back-off and approach maintenance paths.
    /// </summary>
    public static Vector3 CalculateMaxMeleeBackoff(in MeleeApproachStandRequest request)
    {
        var toPlayer = request.PlayerPosition - request.TargetPosition;
        toPlayer.Y = 0f;

        var standDistance = MaxMeleeStandDistance(request.TargetHitboxRadius, request.PlayerHitboxRadius);
        var dist = toPlayer.Length();
        var dir = dist < 1e-6f ? Vector3.UnitZ : toPlayer / dist;

        var ideal = request.TargetPosition + dir * standDistance;
        return SnapToFloorPlane(ideal, request.TargetPosition.Y);
    }

    /// <summary>
    /// Horizontal distance from the player to the ideal stand point (before GCD budget clamp).
    /// </summary>
    public static float ComputeIdealHorizontalDistance(in PositionalStandRequest request)
    {
        var ideal = ComputeIdealStandPoint(in request);
        return HorizontalDistance(request.PlayerPosition, ideal);
    }

    /// <summary>
    /// Horizontal move cap from GCD budget or <see cref="PositionalMovementConstants.MaxMoveYalmsPerGcdWindow"/> fallback.
    /// </summary>
    public static float ComputeMaxHorizontalMoveYalms(float distToIdeal, float gcdRemainingSeconds)
    {
        float budget;
        if (float.IsNaN(gcdRemainingSeconds))
        {
            budget = PositionalMovementConstants.MaxMoveYalmsPerGcdWindow;
        }
        else
        {
            var window = gcdRemainingSeconds - PositionalMovementConstants.GcdClipBufferSeconds;
            budget = MathF.Max(0f, window) * PositionalMovementConstants.MoveSpeedYalmsPerSecond;
        }

        return MathF.Min(distToIdeal, budget);
    }

    /// <summary>Aligns Y to a reference floor height without navmesh queries.</summary>
    public static Vector3 SnapToFloorPlane(Vector3 position, float floorY)
        => new(position.X, floorY, position.Z);

    /// <summary>
    /// Estimated seconds to traverse a horizontal distance at conservative move speed.
    /// </summary>
    public static float EstimateMoveDurationSeconds(float horizontalDistanceYalms)
    {
        if (horizontalDistanceYalms <= 0f)
            return 0f;

        return horizontalDistanceYalms / PositionalMovementConstants.MoveSpeedYalmsPerSecond;
    }

    /// <summary>
    /// Center-to-center distance for a melee stand point at safe max melee: target hitbox + player hitbox
    /// + melee reach − safety buffer. Standing here keeps every melee GCD in range while sitting at the
    /// outer edge (minimal in/out movement for mechanics) instead of hugging the target.
    /// </summary>
    public static float MaxMeleeStandDistance(float targetHitboxRadius, float playerHitboxRadius)
        => targetHitboxRadius + playerHitboxRadius
           + PositionalMovementConstants.MeleeActionRangeYalms
           - PositionalMovementConstants.MaxMeleeSafetyBufferYalms;

    private static Vector3 ComputeMeleeApproachPoint(in MeleeApproachStandRequest request)
    {
        var toPlayer = request.PlayerPosition - request.TargetPosition;
        toPlayer.Y = 0f;

        var dist = toPlayer.Length();
        var standDistance = MaxMeleeStandDistance(request.TargetHitboxRadius, request.PlayerHitboxRadius);
        if (dist <= standDistance + 0.25f)
            return request.PlayerPosition;

        if (dist < 1e-6f)
            return request.TargetPosition + Vector3.UnitZ * standDistance;

        var dir = toPlayer / dist;
        return request.TargetPosition + dir * standDistance;
    }

    private static Vector3 ComputeIdealStandPoint(in PositionalStandRequest request)
    {
        var direction = GetStandDirectionUnit(
            request.TargetRotationRadians,
            request.RequiredPositional,
            request.TargetPosition,
            request.PlayerPosition,
            request.BoundaryBiasRadians);

        var standDistance = request.TargetHitboxRadius + request.StandRadiusOffset;
        return request.TargetPosition + direction * standDistance;
    }

    /// <summary>
    /// Returns the unit direction from the target toward the ideal stand point.
    /// When <paramref name="boundaryBiasRadians"/> &gt; 0, targets the flank/rear boundary ± that margin
    /// (on whichever side of the target the player is currently on) rather than the arc center. This keeps
    /// both the flank-stand and rear-stand within a small angular window, reducing travel distance for jobs
    /// with back-to-back flank/rear switches (e.g. MNK: Opo-opo Rear → Raptor Flank → Coeurl Rear/Flank).
    /// </summary>
    private static Vector3 GetStandDirectionUnit(
        float targetRotation,
        PositionalType positional,
        Vector3 targetPosition,
        Vector3 playerPosition,
        float boundaryBiasRadians = 0f)
    {
        if (boundaryBiasRadians > 0f)
        {
            // Determine which side of the target the player is on.
            var toPlayer = playerPosition - targetPosition;
            toPlayer.Y = 0f;
            var playerOnRight = toPlayer.LengthSquared() >= 1e-6f
                && Vector3.Dot(DirectionFromRotation(targetRotation + MathF.PI / 2f), toPlayer) >= 0f;
            var sideSign = playerOnRight ? 1f : -1f;

            // The flank/rear boundary on the player's side is at ±3π/4 from the target's facing.
            // Rear-biased: nudge boundaryBiasRadians into the rear arc from that boundary.
            // Flank-biased: nudge boundaryBiasRadians into the flank arc from that boundary.
            var boundaryBase = targetRotation + sideSign * (3f * MathF.PI / 4f);
            return positional == PositionalType.Rear
                ? DirectionFromRotation(boundaryBase + sideSign * boundaryBiasRadians)
                : DirectionFromRotation(boundaryBase - sideSign * boundaryBiasRadians);
        }

        if (positional == PositionalType.Rear)
            return DirectionFromRotation(targetRotation + MathF.PI);

        // Flank: pick the side closer to the player's current bearing from the target.
        var flankRight = DirectionFromRotation(targetRotation + MathF.PI / 2f);
        var flankLeft = DirectionFromRotation(targetRotation - MathF.PI / 2f);

        var toPlayerVec = playerPosition - targetPosition;
        toPlayerVec.Y = 0f;
        if (toPlayerVec.LengthSquared() < 1e-6f)
            return flankRight;

        return Vector3.Dot(flankRight, toPlayerVec) >= Vector3.Dot(flankLeft, toPlayerVec)
            ? flankRight
            : flankLeft;
    }

    private static Vector3 DirectionFromRotation(float rotationRadians)
        => new(MathF.Sin(rotationRadians), 0f, MathF.Cos(rotationRadians));

    private static Vector3 ClampHorizontalDisplacement(Vector3 from, Vector3 to, float maxDistance)
    {
        var delta = to - from;
        delta.Y = 0f;

        var length = delta.Length();
        if (length <= maxDistance || length < 1e-6f)
            return to;

        var scaled = delta / length * maxDistance;
        return new Vector3(from.X + scaled.X, to.Y, from.Z + scaled.Z);
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }
}
