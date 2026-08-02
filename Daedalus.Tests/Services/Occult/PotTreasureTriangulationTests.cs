using System;
using System.Collections.Generic;
using System.Numerics;
using Daedalus.Services.Occult;
using Xunit;

namespace Daedalus.Tests.Services.Occult;

/// <summary>
/// Narrowing the pot coffer down from successive elixir readings. Each reading is a cone from
/// wherever you stood; the answer is the overlap. Headings follow the game's convention —
/// 0 = SOUTH, +π/2 east, ±π north, −π/2 west — matching Atan2(dx, dz) as used elsewhere.
/// </summary>
public sealed class PotTreasureTriangulationTests
{
    private const float Half = PotTreasureTriangulation.DefaultHalfAngleRadians; // 22.5°

    private static ElixirBearing At(float x, float z, float heading)
        => new(new Vector3(x, 0, z), heading, Half);

    // ── Compass parsing ──

    [Theory]
    [InlineData("The treasure lies to the south.", 0f)]
    [InlineData("You sense it to the east.", MathF.PI / 2f)]
    [InlineData("Something to the north!", MathF.PI)]
    [InlineData("...to the west.", -MathF.PI / 2f)]
    public void TryParseHeading_ReadsCardinals(string text, float expected)
    {
        Assert.True(PotTreasureTriangulation.TryParseHeading(text, out var heading));
        Assert.Equal(expected, heading, precision: 4);
    }

    /// <summary>"southeast" must beat the "south" hiding inside it.</summary>
    [Theory]
    [InlineData("to the southeast", MathF.PI / 4f)]
    [InlineData("to the south-east", MathF.PI / 4f)]
    [InlineData("to the south east", MathF.PI / 4f)]
    [InlineData("to the northwest", -3f * MathF.PI / 4f)]
    public void TryParseHeading_PrefersTheLongerIntercardinal(string text, float expected)
    {
        Assert.True(PotTreasureTriangulation.TryParseHeading(text, out var heading));
        Assert.Equal(expected, heading, precision: 4);
    }

    [Theory]
    [InlineData("nothing here")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseHeading_RejectsAnythingWithoutADirection(string? text)
    {
        Assert.False(PotTreasureTriangulation.TryParseHeading(text, out _));
    }

    // ── The elixir's actual chat lines (captured in North Horn, 2026-08-01) ──

    [Theory]
    [InlineData("You sense something far, far to the north.", ElixirProximity.VeryFar, MathF.PI)]
    [InlineData("You sense something far, far to the northeast.", ElixirProximity.VeryFar, 3f * MathF.PI / 4f)]
    [InlineData("You sense something far, far to the east.", ElixirProximity.VeryFar, MathF.PI / 2f)]
    [InlineData("You sense something to the north.", ElixirProximity.Within, MathF.PI)]
    [InlineData("You sense something to the northeast.", ElixirProximity.Within, 3f * MathF.PI / 4f)]
    [InlineData("You sense something immediately to the southeast.", ElixirProximity.Immediate, MathF.PI / 4f)]
    [InlineData("You sense something immediately to the south.", ElixirProximity.Immediate, 0f)]
    [InlineData("You sense something immediately to the southwest.", ElixirProximity.Immediate, -MathF.PI / 4f)]
    public void TryReadElixir_ParsesTheRealMessages(string line, ElixirProximity band, float heading)
    {
        Assert.True(PotTreasureTriangulation.TryReadElixir(line, new Vector3(1, 2, 3), out var bearing));
        Assert.Equal(band, bearing.Proximity);
        Assert.Equal(heading, bearing.HeadingRadians, precision: 4);
        Assert.Equal(new Vector3(1, 2, 3), bearing.Origin);
    }

    [Fact]
    public void IsDiscovery_MatchesTheLineThatEndsTheHunt()
    {
        Assert.True(PotTreasureTriangulation.IsDiscovery("You discover a treasure coffer!"));
        Assert.False(PotTreasureTriangulation.IsDiscovery("You sense something to the north."));
    }

    /// <summary>The tracker gets the whole chat stream, so everything else must be ignored.</summary>
    [Theory]
    [InlineData("You obtain 10 Enlightenment silver obols.")]
    [InlineData("Seraph: deaths at ce!")]
    [InlineData("Thank you for the elixir! Take care, and good luck!")]
    [InlineData("You obtain a savage might materia XI.")]
    public void TryReadElixir_IgnoresEverythingElse(string line)
    {
        Assert.False(PotTreasureTriangulation.TryReadElixir(line, Vector3.Zero, out _));
    }

    // ── Distance bands ──

    [Theory]
    [InlineData("It lies far, far to the south.", ElixirProximity.VeryFar)]
    [InlineData("It lies far to the south.", ElixirProximity.Far)]
    [InlineData("It lies to the south.", ElixirProximity.Within)]
    [InlineData("It is immediately to the south!", ElixirProximity.Immediate)]
    [InlineData("nothing useful", ElixirProximity.Unknown)]
    public void ParseProximity_ReadsTheDistanceWording(string text, ElixirProximity expected)
    {
        Assert.Equal(expected, PotTreasureTriangulation.ParseProximity(text));
    }

    /// <summary>A reading with a distance band is a ring segment, not an open wedge.</summary>
    [Fact]
    public void IsInsideCone_RejectsCandidatesOutsideTheBand()
    {
        var immediate = new ElixirBearing(Vector3.Zero, 0f, Half, ElixirProximity.Immediate);

        Assert.True(PotTreasureTriangulation.IsInsideCone(new Vector3(0, 0, 6), immediate));
        Assert.False(PotTreasureTriangulation.IsInsideCone(new Vector3(0, 0, 40), immediate));
    }

    [Fact]
    public void IsInsideCone_FarExcludesBothTooCloseAndTooDistant()
    {
        var far = new ElixirBearing(Vector3.Zero, 0f, Half, ElixirProximity.Far);

        Assert.False(PotTreasureTriangulation.IsInsideCone(new Vector3(0, 0, 15), far));
        Assert.True(PotTreasureTriangulation.IsInsideCone(new Vector3(0, 0, 40), far));
        Assert.False(PotTreasureTriangulation.IsInsideCone(new Vector3(0, 0, 400), far));
    }

    [Fact]
    public void IsInsideCone_VeryFarKeepsOnlyDistantCandidates()
    {
        var veryFar = new ElixirBearing(Vector3.Zero, 0f, Half, ElixirProximity.VeryFar);

        Assert.False(PotTreasureTriangulation.IsInsideCone(new Vector3(0, 0, 40), veryFar));
        Assert.True(PotTreasureTriangulation.IsInsideCone(new Vector3(0, 0, 400), veryFar));
    }

    /// <summary>Even one banded reading bounds the answer — that is the value of the distance word.</summary>
    [Fact]
    public void EstimateCentre_ASingleBandedReadingAlreadyNarrows()
    {
        var bearings = new List<ElixirBearing>
        {
            new(Vector3.Zero, 0f, Half, ElixirProximity.Within),
        };

        var centre = PotTreasureTriangulation.EstimateCentre(bearings, Vector3.Zero, 200f);

        Assert.NotNull(centre);
        Assert.True(centre!.Value.Z > 0, "south of the reading");
        Assert.True(centre.Value.Length() <= PotTreasureTriangulation.TargetRangeYalms,
            "and inside targeting range rather than out on the horizon");
    }

    // ── Cone containment ──

    [Fact]
    public void IsInsideCone_AcceptsDueSouthOfTheReading()
    {
        var south = At(0, 0, 0f);

        Assert.True(PotTreasureTriangulation.IsInsideCone(new Vector3(0, 0, 100), south));
    }

    [Fact]
    public void IsInsideCone_RejectsTheOppositeDirection()
    {
        var south = At(0, 0, 0f);

        Assert.False(PotTreasureTriangulation.IsInsideCone(new Vector3(0, 0, -100), south));
    }

    /// <summary>Just inside 22.5° is in; just outside is out.</summary>
    [Fact]
    public void IsInsideCone_RespectsTheArcEdges()
    {
        var south = At(0, 0, 0f);
        var justInside = 100f * MathF.Tan(Half - 0.01f);
        var justOutside = 100f * MathF.Tan(Half + 0.01f);

        Assert.True(PotTreasureTriangulation.IsInsideCone(new Vector3(justInside, 0, 100), south));
        Assert.False(PotTreasureTriangulation.IsInsideCone(new Vector3(justOutside, 0, 100), south));
    }

    /// <summary>Standing on top of it: the bearing is meaningless, so don't discard the answer.</summary>
    [Fact]
    public void IsInsideCone_AcceptsACandidateUnderYourFeet()
    {
        Assert.True(PotTreasureTriangulation.IsInsideCone(new Vector3(0.2f, 0, -0.2f), At(0, 0, MathF.PI)));
    }

    /// <summary>The wrap-around case — a cone straddling ±π must not split in two.</summary>
    [Fact]
    public void IsInsideCone_HandlesTheNorthWrap()
    {
        var north = At(0, 0, MathF.PI);

        Assert.True(PotTreasureTriangulation.IsInsideCone(new Vector3(5, 0, -100), north));
        Assert.True(PotTreasureTriangulation.IsInsideCone(new Vector3(-5, 0, -100), north));
    }

    // ── Overlapping readings ──

    /// <summary>The whole point: two crossing cones keep only what satisfies both.</summary>
    [Fact]
    public void Feasible_KeepsOnlyWhatSatisfiesEveryReading()
    {
        var candidates = new List<Vector3>
        {
            new(0, 0, 100),    // due south of both readings
            new(-100, 0, 100), // south-west: fails the second reading
            new(0, 0, -100),   // north: fails both
        };

        var bearings = new List<ElixirBearing>
        {
            At(0, 0, 0f),                     // "south", read at the origin
            At(-80, 0, 100, MathF.PI / 2f),   // "east", read from 80y to the west
        };

        var feasible = PotTreasureTriangulation.Feasible(candidates, bearings);

        Assert.Single(feasible);
        Assert.Equal(new Vector3(0, 0, 100), feasible[0]);
    }

    private static ElixirBearing At(float x, float y, float z, float heading)
        => new(new Vector3(x, y, z), heading, Half);

    [Fact]
    public void Feasible_WithNoReadingsKeepsEverything()
    {
        var candidates = new List<Vector3> { new(1, 0, 1), new(-9, 0, 4) };

        Assert.Equal(2, PotTreasureTriangulation.Feasible(candidates, new List<ElixirBearing>()).Count);
    }

    // ── Centre estimate ──

    [Fact]
    public void EstimateCentre_LandsInTheOverlapOfTwoCones()
    {
        var bearings = new List<ElixirBearing>
        {
            At(0, 0, 0f),                        // treasure is south of here
            At(0, 0, 200, -MathF.PI / 2f),       // and west of a point further south
        };

        var centre = PotTreasureTriangulation.EstimateCentre(bearings, new Vector3(0, 0, 100), 150f);

        Assert.NotNull(centre);
        Assert.True(centre!.Value.Z > 0, "should be south of the first reading");
        Assert.True(centre.Value.X < 0, "should be west of the second reading");
    }

    /// <summary>Contradictory readings should say so rather than invent a point.</summary>
    [Fact]
    public void EstimateCentre_IsNullWhenReadingsCannotBothBeTrue()
    {
        var bearings = new List<ElixirBearing>
        {
            At(0, 0, 0f),        // south
            At(0, 0, MathF.PI),  // north, from the very same spot
        };

        Assert.Null(PotTreasureTriangulation.EstimateCentre(bearings, Vector3.Zero, 100f));
    }

    // ── Advice on where to read next ──

    [Fact]
    public void CrossingQuality_IsZeroForTwoReadingsTakenTogether()
    {
        Assert.Equal(0f, PotTreasureTriangulation.CrossingQuality(At(0, 0, 0f), At(2, 0, 0f, MathF.PI / 2f)));
    }

    [Fact]
    public void CrossingQuality_RewardsPerpendicularReadingsFromApart()
    {
        var quality = PotTreasureTriangulation.CrossingQuality(At(0, 0, 0f), At(0, 0, 120, MathF.PI / 2f));

        Assert.Equal(1f, quality, precision: 3);
    }

    [Fact]
    public void CrossingQuality_IsPoorWhenBothReadingsPointTheSameWay()
    {
        var quality = PotTreasureTriangulation.CrossingQuality(At(0, 0, 0f), At(0, 0, 120, 0f));

        Assert.True(quality < 0.2f, $"parallel cones barely narrow anything, got {quality}");
    }
}
