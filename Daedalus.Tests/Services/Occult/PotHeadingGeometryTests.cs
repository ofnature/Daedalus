using System;
using System.Numerics;
using Daedalus.Services.Occult;
using Xunit;

namespace Daedalus.Tests.Services.Occult;

/// <summary>
/// The compass words must point where they say. Field screenshot 2026-08-14: four readings of
/// "northeast" and one of "north" drew cones pointing SOUTH, because the map renderer had its own
/// <c>(sin, -cos)</c> under a comment claiming "0 = north" while this file's convention is
/// 0 = SOUTH. The cones disagreed with the feasible region drawn beside them, which is computed
/// in world space and was correct throughout.
/// </summary>
public sealed class PotHeadingGeometryTests
{
    private const float Tol = 1e-4f;

    private static void AssertOffset(string compass, float expectedX, float expectedZ)
    {
        Assert.True(PotTreasureTriangulation.TryParseHeading(
            $"You sense something to the {compass}.", out var heading));

        var v = PotTreasureTriangulation.HeadingToWorldOffset(heading);
        Assert.True(MathF.Abs(v.X - expectedX) < Tol, $"{compass}: X was {v.X}, expected {expectedX}");
        Assert.True(MathF.Abs(v.Y - expectedZ) < Tol, $"{compass}: Z was {v.Y}, expected {expectedZ}");
    }

    // World axes: +X east, +Z south. Screen Y follows world Z, so north must be NEGATIVE.
    [Fact] public void North_IsNegativeZ() => AssertOffset("north", 0f, -1f);
    [Fact] public void South_IsPositiveZ() => AssertOffset("south", 0f, 1f);
    [Fact] public void East_IsPositiveX() => AssertOffset("east", 1f, 0f);
    [Fact] public void West_IsNegativeX() => AssertOffset("west", -1f, 0f);

    /// <summary>The exact reading from the field screenshot. It must go up and to the right.</summary>
    [Fact]
    public void Northeast_IsEastAndNorth()
    {
        var r = MathF.Sqrt(0.5f);
        AssertOffset("northeast", r, -r);
    }

    [Fact] public void Southeast_IsEastAndSouth() { var r = MathF.Sqrt(0.5f); AssertOffset("southeast", r, r); }
    [Fact] public void Southwest_IsWestAndSouth() { var r = MathF.Sqrt(0.5f); AssertOffset("southwest", -r, r); }
    [Fact] public void Northwest_IsWestAndNorth() { var r = MathF.Sqrt(0.5f); AssertOffset("northwest", -r, -r); }

    /// <summary>
    /// The offset must invert the Atan2(dx, dz) the rest of the file uses to test whether a point
    /// satisfies a bearing — that round trip is what keeps the drawing and the maths in step.
    /// </summary>
    [Fact]
    public void HeadingToWorldOffset_InvertsAtan2()
    {
        foreach (var heading in PotTreasureTriangulation.CompassHeadings.Values)
        {
            var v = PotTreasureTriangulation.HeadingToWorldOffset(heading);
            var back = MathF.Atan2(v.X, v.Y);
            Assert.True(
                MathF.Abs(PotTreasureTriangulation.NormalizeAngle(back - heading)) < Tol,
                $"heading {heading} round-tripped to {back}");
        }
    }

    /// <summary>
    /// A point placed along the heading must actually satisfy that bearing. This is the assertion
    /// that ties the arrow to the region: if the drawing convention ever flips again, the cone and
    /// the feasible region part company and this fails.
    /// </summary>
    [Fact]
    public void PointAlongHeading_SatisfiesItsOwnBearing()
    {
        var origin = new Vector3(100f, 0f, 200f);

        foreach (var (word, heading) in PotTreasureTriangulation.CompassHeadings)
        {
            var dir = PotTreasureTriangulation.HeadingToWorldOffset(heading);
            var target = new Vector3(origin.X + (dir.X * 40f), 0f, origin.Z + (dir.Y * 40f));

            var bearing = new ElixirBearing(
                origin, heading, PotTreasureTriangulation.DefaultHalfAngleRadians,
                ElixirProximity.Unknown);

            Assert.True(
                PotTreasureTriangulation.SatisfiesAll(target, new[] { bearing }),
                $"a point 40y {word} of the origin must satisfy the {word} reading");
        }
    }
}
