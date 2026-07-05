using System;
using System.Numerics;
using Daedalus.Services.Positional;
using Daedalus.Services.Positional.Navigation;

namespace Daedalus.Tests.Services.Positional;

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

    [Fact]
    public void CalculateMeleeApproach_MovesTowardTargetAtMeleeDistance()
    {
        var request = new MeleeApproachStandRequest(
            PlayerPosition: new Vector3(0f, 0f, 10f),
            PlayerHitboxRadius: 0.5f,
            TargetPosition: Vector3.Zero,
            TargetHitboxRadius: 2f,
            GcdRemainingSeconds: 2.5f);

        var result = PositionalStandCalculator.CalculateMeleeApproach(in request);

        Assert.True(result.Z < 10f, "Should move closer to target");
        Assert.Equal(0f, result.X, Epsilon);
    }

    [Fact]
    public void CalculateMaxMeleeBackoff_WhenHugging_BacksOutToSafeMaxMelee()
    {
        // Player standing almost on top of the target (z = 1) should be pushed out to the stand ring.
        var request = new MeleeApproachStandRequest(
            PlayerPosition: new Vector3(0f, 0f, 1f),
            PlayerHitboxRadius: 0.5f,
            TargetPosition: Vector3.Zero,
            TargetHitboxRadius: 2f);

        var result = PositionalStandCalculator.CalculateMaxMeleeBackoff(in request);

        Assert.Equal(0f, result.X, Epsilon);
        Assert.Equal(5.0f, result.Z, Epsilon); // backs straight out along +Z bearing to safe max melee
    }

    [Fact]
    public void CalculateMaxMeleeBackoff_KeepsCurrentBearing()
    {
        // Hugging from the +X side stays on the +X side, just farther out.
        var request = new MeleeApproachStandRequest(
            PlayerPosition: new Vector3(0.5f, 0f, 0f),
            PlayerHitboxRadius: 0.5f,
            TargetPosition: Vector3.Zero,
            TargetHitboxRadius: 2f);

        var result = PositionalStandCalculator.CalculateMaxMeleeBackoff(in request);

        Assert.Equal(5.0f, result.X, Epsilon);
        Assert.Equal(0f, result.Z, Epsilon);
    }

    // ── Boundary camping (BoundaryBiasRadians) ─────────────────────────────────────────────────────────

    private const float TenDegrees = MathF.PI / 18f;

    /// <summary>Bearing of a point around the target, degrees off its facing (+Z when rotation 0).</summary>
    private static float AngleOffFacingDeg(Vector3 point)
        => MathF.Atan2(point.X, point.Z) * 180f / MathF.PI;

    private static PositionalStandRequest BiasRequest(
        PositionalType required, Vector3 playerPosition, float biasRadians)
        => new(
            PlayerPosition: playerPosition,
            PlayerHitboxRadius: 0.5f,
            TargetPosition: Vector3.Zero,
            TargetHitboxRadius: 2f,
            TargetRotationRadians: 0f,
            RequiredPositional: required,
            GcdRemainingSeconds: float.NaN,
            FloorY: 0f,
            BoundaryBiasRadians: biasRadians);

    [Fact]
    public void Calculate_RearBias_RightSide_LandsAtBoundaryPlusBias()
    {
        // Player on the target's right (+X): boundary at +135°, rear-biased → 145° off facing.
        var request = BiasRequest(PositionalType.Rear, new Vector3(5f, 0f, 0f), TenDegrees);
        var result = PositionalStandCalculator.Calculate(in request);

        Assert.Equal(145f, AngleOffFacingDeg(result), 0.5f);
    }

    [Fact]
    public void Calculate_RearBias_LeftSide_LandsAtMirroredBoundary()
    {
        var request = BiasRequest(PositionalType.Rear, new Vector3(-5f, 0f, 0f), TenDegrees);
        var result = PositionalStandCalculator.Calculate(in request);

        Assert.Equal(-145f, AngleOffFacingDeg(result), 0.5f);
    }

    [Fact]
    public void Calculate_FlankBias_LandsAtBoundaryMinusBias()
    {
        // Flank-biased on the right side: 135° − 10° = 125° off facing (inside the 45–135° flank arc).
        var request = BiasRequest(PositionalType.Flank, new Vector3(5f, 0f, 0f), TenDegrees);
        var result = PositionalStandCalculator.Calculate(in request);

        Assert.Equal(125f, AngleOffFacingDeg(result), 0.5f);
    }

    [Fact]
    public void Calculate_FlankToRearSwap_TravelIsShortChord()
    {
        // The whole point of boundary camping: at 10° bias the flank↔rear swap is a ~1.9y chord
        // (2·r·sin(10°), r = 5.5), not a quarter-circle run.
        var flank = PositionalStandCalculator.Calculate(BiasRequest(PositionalType.Flank, new Vector3(5f, 0f, 0f), TenDegrees));
        var rear = PositionalStandCalculator.Calculate(BiasRequest(PositionalType.Rear, new Vector3(5f, 0f, 0f), TenDegrees));

        Assert.True(Vector3.Distance(flank, rear) < 2.5f,
            $"swap chord {Vector3.Distance(flank, rear):0.00}y should be < 2.5y");
    }

    [Fact]
    public void Calculate_ZeroBias_UsesArcCenter()
    {
        // Slider at 0 restores the old arc-center behavior: rear = straight behind (180°).
        var request = BiasRequest(PositionalType.Rear, new Vector3(5f, 0f, 0f), 0f);
        var result = PositionalStandCalculator.Calculate(in request);

        Assert.Equal(180f, MathF.Abs(AngleOffFacingDeg(result)), 0.5f);
    }
}
