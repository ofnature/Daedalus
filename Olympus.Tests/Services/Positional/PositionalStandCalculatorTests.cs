using System;
using System.Numerics;
using Olympus.Services.Positional;
using Olympus.Services.Positional.Navigation;

namespace Olympus.Tests.Services.Positional;

/// <summary>
/// Pure-math tests for <see cref="PositionalStandCalculator"/> — no vNav or BMR runtime.
/// </summary>
public class PositionalStandCalculatorTests
{
    private const float Epsilon = 0.01f;

    private static PositionalStandRequest RearFromFrontRequest(float gcdRemainingSeconds = float.NaN)
        => new(
            PlayerPosition: new Vector3(0f, 0f, 5f),
            PlayerHitboxRadius: 0.5f,
            TargetPosition: Vector3.Zero,
            TargetHitboxRadius: 2f,
            TargetRotationRadians: 0f,
            RequiredPositional: PositionalType.Rear,
            GcdRemainingSeconds: gcdRemainingSeconds,
            FloorY: 0f);

    [Fact]
    public void Calculate_WithFullGcdWindow_ReachesIdealRear()
    {
        // dist to ideal rear z=-5.5 is 10.5y; budget (2.5 - 0.1) * 5 = 12y → full ideal.
        var request = RearFromFrontRequest(gcdRemainingSeconds: 2.5f);
        var result = PositionalStandCalculator.Calculate(in request);

        Assert.Equal(0f, result.X, Epsilon);
        Assert.Equal(0f, result.Y, Epsilon);
        Assert.Equal(-5.5f, result.Z, Epsilon);
    }

    [Fact]
    public void Calculate_WithHalfGcdWindow_CapsAtFiveYalms()
    {
        // budget (1.1 - 0.1) * 5 = 5y from z=5 → z=0
        var request = RearFromFrontRequest(gcdRemainingSeconds: 1.1f);
        var result = PositionalStandCalculator.Calculate(in request);

        Assert.Equal(0f, result.X, Epsilon);
        Assert.Equal(0f, result.Y, Epsilon);
        Assert.Equal(0f, result.Z, Epsilon);
    }

    [Fact]
    public void Calculate_WithoutGcdRemaining_UsesMaxMoveYalmsPerGcdWindowFallback()
    {
        // dist 10.5y capped to MaxMoveYalmsPerGcdWindow (10y) → z=-5
        var request = RearFromFrontRequest();
        var result = PositionalStandCalculator.Calculate(in request);

        Assert.Equal(0f, result.X, Epsilon);
        Assert.Equal(0f, result.Y, Epsilon);
        Assert.Equal(-5f, result.Z, Epsilon);
        Assert.Equal(PositionalMovementConstants.MaxMoveYalmsPerGcdWindow, 10f);
    }

    [Fact]
    public void ComputeMaxHorizontalMoveYalms_NearFinisherWindow_IsTiny()
    {
        // (0.12 - 0.1) * 5 = 0.1y — clip guard in service skips before queueing.
        var cap = PositionalStandCalculator.ComputeMaxHorizontalMoveYalms(distToIdeal: 10.5f, gcdRemainingSeconds: 0.12f);

        Assert.Equal(0.1f, cap, Epsilon);
    }

    [Fact]
    public void Calculate_RearWhenAlreadyAtIdeal_MinimalDisplacement()
    {
        var request = new PositionalStandRequest(
            PlayerPosition: new Vector3(0f, 0f, -5.5f),
            PlayerHitboxRadius: 0.5f,
            TargetPosition: Vector3.Zero,
            TargetHitboxRadius: 2f,
            TargetRotationRadians: 0f,
            RequiredPositional: PositionalType.Rear,
            GcdRemainingSeconds: 2.5f,
            FloorY: 0f);

        var result = PositionalStandCalculator.Calculate(in request);

        Assert.Equal(0f, result.X, Epsilon);
        Assert.Equal(0f, result.Y, Epsilon);
        Assert.Equal(-5.5f, result.Z, Epsilon);
    }

    [Fact]
    public void Calculate_Flank_PicksSideCloserToPlayer()
    {
        var request = new PositionalStandRequest(
            PlayerPosition: new Vector3(5f, 0f, 0f),
            PlayerHitboxRadius: 0.5f,
            TargetPosition: Vector3.Zero,
            TargetHitboxRadius: 2f,
            TargetRotationRadians: 0f,
            RequiredPositional: PositionalType.Flank,
            GcdRemainingSeconds: 2.5f,
            FloorY: 0f);

        var result = PositionalStandCalculator.Calculate(in request);

        var standDistance = 2f + PositionalMovementConstants.DefaultStandRadiusOffset;
        Assert.True(result.X > 0f, "Expected positive X (right flank)");
        Assert.Equal(0f, result.Z, 0.5f);
        Assert.InRange(result.X, standDistance - 0.5f, standDistance + 0.5f);
    }

    [Fact]
    public void Calculate_UsesTargetFloorYWhenFloorYNaN()
    {
        var request = new PositionalStandRequest(
            PlayerPosition: new Vector3(0f, 1f, -5.5f),
            PlayerHitboxRadius: 0.5f,
            TargetPosition: new Vector3(0f, 2.5f, 0f),
            TargetHitboxRadius: 2f,
            TargetRotationRadians: 0f,
            RequiredPositional: PositionalType.Rear,
            GcdRemainingSeconds: 2.5f);

        var result = PositionalStandCalculator.Calculate(in request);

        Assert.Equal(2.5f, result.Y, Epsilon);
    }

    [Fact]
    public void Calculate_ClampsHorizontalAndDefaultsFloorYToTargetWhenNaN()
    {
        var request = new PositionalStandRequest(
            PlayerPosition: new Vector3(0f, 9f, 5f),
            PlayerHitboxRadius: 0.5f,
            TargetPosition: new Vector3(0f, 2.5f, 0f),
            TargetHitboxRadius: 2f,
            TargetRotationRadians: 0f,
            RequiredPositional: PositionalType.Rear,
            GcdRemainingSeconds: 1.1f);

        var result = PositionalStandCalculator.Calculate(in request);

        Assert.Equal(0f, result.Z, Epsilon); // 5y GCD budget toward rear
        Assert.Equal(2.5f, result.Y, Epsilon);
        Assert.Equal(0f, result.X, Epsilon);
    }

    [Fact]
    public void SnapToFloorPlane_SetsY()
    {
        var input = new Vector3(1f, 99f, 2f);
        var snapped = PositionalStandCalculator.SnapToFloorPlane(input, floorY: 0.25f);

        Assert.Equal(1f, snapped.X, Epsilon);
        Assert.Equal(0.25f, snapped.Y, Epsilon);
        Assert.Equal(2f, snapped.Z, Epsilon);
    }

    [Fact]
    public void EstimateMoveDurationSeconds_UsesConfiguredMoveSpeed()
    {
        var duration = PositionalStandCalculator.EstimateMoveDurationSeconds(5f);
        Assert.Equal(1f, duration, Epsilon);

        Assert.Equal(PositionalMovementConstants.MoveSpeedYalmsPerSecond, 5f);
    }

    [Fact]
    public void Calculate_WhenGcdBudgetZero_NoHorizontalMove()
    {
        var request = RearFromFrontRequest(gcdRemainingSeconds: 0.05f);
        var result = PositionalStandCalculator.Calculate(in request);

        Assert.Equal(5f, result.Z, Epsilon);
    }
}
