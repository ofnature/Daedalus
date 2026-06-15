using System;
using System.Numerics;
using Olympus.Services.Positional.Navigation;

namespace Olympus.Services.Positional;

/// <summary>
/// Inputs for computing a clamped positional stand point (pure geometry, no IPC).
/// </summary>
public readonly record struct PositionalStandRequest(
    Vector3 PlayerPosition,
    float PlayerHitboxRadius,
    Vector3 TargetPosition,
    float TargetHitboxRadius,
    float TargetRotationRadians,
    PositionalType RequiredPositional,
    float GcdRemainingSeconds = float.NaN,
    float StandRadiusOffset = PositionalMovementConstants.DefaultStandRadiusOffset,
    float FloorY = float.NaN);

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

    private static Vector3 ComputeIdealStandPoint(in PositionalStandRequest request)
    {
        var direction = GetStandDirectionUnit(
            request.TargetRotationRadians,
            request.RequiredPositional,
            request.TargetPosition,
            request.PlayerPosition);

        var standDistance = request.TargetHitboxRadius + request.StandRadiusOffset;
        return request.TargetPosition + direction * standDistance;
    }

    private static Vector3 GetStandDirectionUnit(
        float targetRotation,
        PositionalType positional,
        Vector3 targetPosition,
        Vector3 playerPosition)
    {
        if (positional == PositionalType.Rear)
            return DirectionFromRotation(targetRotation + MathF.PI);

        // Flank: pick the side closer to the player's current bearing from the target.
        var flankRight = DirectionFromRotation(targetRotation + MathF.PI / 2f);
        var flankLeft = DirectionFromRotation(targetRotation - MathF.PI / 2f);

        var toPlayer = playerPosition - targetPosition;
        toPlayer.Y = 0f;
        if (toPlayer.LengthSquared() < 1e-6f)
            return flankRight;

        return Vector3.Dot(flankRight, toPlayer) >= Vector3.Dot(flankLeft, toPlayer)
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
