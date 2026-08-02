using System;
using System.Collections.Generic;
using System.Numerics;

namespace Daedalus.Services.Occult;

/// <summary>
/// One reading from the Magical Elixir: taken at <paramref name="Origin"/>, the treasure lies
/// within <paramref name="HalfAngleRadians"/> either side of <paramref name="HeadingRadians"/>.
/// </summary>
/// <remarks>
/// Headings use the game's own convention, matching <c>Atan2(dx, dz)</c> as used elsewhere in
/// the codebase: 0 = SOUTH, +π/2 = east, ±π = north, −π/2 = west.
/// </remarks>
public readonly record struct ElixirBearing(
    Vector3 Origin,
    float HeadingRadians,
    float HalfAngleRadians,
    ElixirProximity Proximity = ElixirProximity.Unknown);

/// <summary>
/// How far the elixir says the treasure is. The wording carries a distance band as well as a
/// direction, so a single reading bounds a ring segment rather than an open wedge — which is
/// why one reading already narrows things and two crossing ones narrow them hard.
/// </summary>
public enum ElixirProximity
{
    /// <summary>No distance word recognised — treat as any distance.</summary>
    Unknown = 0,

    /// <summary>"immediately" — practically on top of it.</summary>
    Immediate = 1,

    /// <summary>Direction only, no distance word: inside targeting range.</summary>
    Within = 2,

    /// <summary>"far" — just outside targeting range.</summary>
    Far = 3,

    /// <summary>"far, far" — well beyond.</summary>
    VeryFar = 4,
}

/// <summary>
/// Narrows down the pot FATE's hidden coffer from successive elixir readings.
/// <para>
/// Each reading is a cone spreading out from wherever you were standing. One cone is a big
/// wedge; walk somewhere else, read again, and the answer is the OVERLAP. A few readings from
/// well-separated spots collapse the region to something small enough to sweep.
/// </para>
/// <para>
/// Pure geometry, no game state — the awkward parts (which compass word maps to which heading,
/// how wide the arc really is) are inputs, so they can be corrected from field data without
/// touching any of this.
/// </para>
/// </summary>
public static class PotTreasureTriangulation
{
    /// <summary>
    /// Default half-width of a reading's arc. An eight-point compass divides 360° into 45°
    /// sectors, so a reported direction is ±22.5°. Widen it if the game turns out to be vaguer
    /// than that — too wide only costs search area, while too narrow can exclude the real spot
    /// and make the overlap empty.
    /// </summary>
    public const float DefaultHalfAngleRadians = MathF.PI / 8f;

    /// <summary>
    /// Distance band edges, in yalms.
    /// <para>
    /// "Immediate" under 10y and "within targeting range" are field-reported. The outer edge of
    /// "far" is a GUESS — all we know is that "far, far" is further still — so it is deliberately
    /// generous: an over-wide band only costs search area, while one that is too tight can make
    /// two honest readings look contradictory and produce no answer at all.
    /// </para>
    /// </summary>
    public const float ImmediateRangeYalms = 10f;
    public const float TargetRangeYalms = 25f;
    public const float FarRangeYalms = 60f;

    /// <summary>Inclusive distance window a band allows, in yalms.</summary>
    public static (float Min, float Max) BandRange(ElixirProximity proximity) => proximity switch
    {
        ElixirProximity.Immediate => (0f, ImmediateRangeYalms),
        ElixirProximity.Within => (0f, TargetRangeYalms),
        ElixirProximity.Far => (TargetRangeYalms, FarRangeYalms),
        ElixirProximity.VeryFar => (FarRangeYalms, float.MaxValue),
        _ => (0f, float.MaxValue),
    };

    /// <summary>
    /// Reads the distance band out of the elixir's wording. Order matters: "far, far" has to be
    /// tested before "far", and a message with a direction but no distance word means the
    /// treasure is inside targeting range rather than unknown.
    /// </summary>
    public static ElixirProximity ParseProximity(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return ElixirProximity.Unknown;

        var cleaned = text.ToLowerInvariant();
        if (cleaned.Contains("immediat", StringComparison.Ordinal))
            return ElixirProximity.Immediate;

        var first = cleaned.IndexOf("far", StringComparison.Ordinal);
        if (first >= 0)
        {
            var second = cleaned.IndexOf("far", first + 3, StringComparison.Ordinal);
            return second >= 0 ? ElixirProximity.VeryFar : ElixirProximity.Far;
        }

        return TryParseHeading(text, out _) ? ElixirProximity.Within : ElixirProximity.Unknown;
    }

    /// <summary>Compass words to headings, in the game's convention (0 = south).</summary>
    public static readonly IReadOnlyDictionary<string, float> CompassHeadings =
        new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
        {
            ["south"] = 0f,
            ["southeast"] = MathF.PI / 4f,
            ["east"] = MathF.PI / 2f,
            ["northeast"] = 3f * MathF.PI / 4f,
            ["north"] = MathF.PI,
            ["northwest"] = -3f * MathF.PI / 4f,
            ["west"] = -MathF.PI / 2f,
            ["southwest"] = -MathF.PI / 4f,
        };

    /// <summary>Reads a compass word out of a sentence ("...to the south east." → south-east).</summary>
    public static bool TryParseHeading(string? text, out float headingRadians)
    {
        headingRadians = 0f;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var cleaned = text.Replace("-", string.Empty).Replace(" ", string.Empty);

        // Longest first, so "southeast" wins over "south".
        var best = string.Empty;
        foreach (var word in CompassHeadings.Keys)
        {
            if (cleaned.Contains(word, StringComparison.OrdinalIgnoreCase) && word.Length > best.Length)
                best = word;
        }

        if (best.Length == 0)
            return false;

        headingRadians = CompassHeadings[best];
        return true;
    }

    /// <summary>
    /// The elixir's own wording, field-captured 2026-08-01:
    /// <code>
    /// You sense something far, far to the north.
    /// You sense something far to the east.
    /// You sense something to the northeast.
    /// You sense something immediately to the southwest.
    /// You discover a treasure coffer!
    /// </code>
    /// </summary>
    public const string SenseMessage = "you sense something";
    public const string DiscoveryMessage = "you discover a treasure coffer";

    /// <summary>Is this chat line an elixir reading?</summary>
    public static bool IsElixirReading(string? text)
        => !string.IsNullOrWhiteSpace(text)
           && text.Contains(SenseMessage, StringComparison.OrdinalIgnoreCase)
           && TryParseHeading(text, out _);

    /// <summary>Is this the line that ends the hunt?</summary>
    public static bool IsDiscovery(string? text)
        => !string.IsNullOrWhiteSpace(text)
           && text.Contains(DiscoveryMessage, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Turns one elixir line plus where you were standing into a reading. Returns false for any
    /// other chat, so the hunt tracker can be fed the whole message stream without filtering.
    /// </summary>
    public static bool TryReadElixir(
        string? text, Vector3 origin, out ElixirBearing bearing, float? halfAngleRadians = null)
    {
        bearing = default;
        if (!IsElixirReading(text) || !TryParseHeading(text, out var heading))
            return false;

        bearing = new ElixirBearing(
            origin,
            heading,
            halfAngleRadians ?? DefaultHalfAngleRadians,
            ParseProximity(text));
        return true;
    }

    /// <summary>Signed difference between two headings, wrapped to [−π, π].</summary>
    public static float NormalizeAngle(float radians)
    {
        while (radians > MathF.PI) radians -= MathF.Tau;
        while (radians < -MathF.PI) radians += MathF.Tau;
        return radians;
    }

    /// <summary>Bearing from one point to another, in the game's convention.</summary>
    public static float HeadingTo(Vector3 from, Vector3 to)
        => MathF.Atan2(to.X - from.X, to.Z - from.Z);

    /// <summary>
    /// Is a candidate inside this reading's cone? Distance is ignored — the elixir gives a
    /// direction, not a range, so the cone runs to the edge of the map.
    /// <para>
    /// A candidate sitting almost exactly where the reading was taken counts as inside: at zero
    /// separation the bearing is meaningless and excluding it would drop the answer just as you
    /// arrive on top of it.
    /// </para>
    /// </summary>
    public static bool IsInsideCone(Vector3 candidate, ElixirBearing bearing)
    {
        var dx = candidate.X - bearing.Origin.X;
        var dz = candidate.Z - bearing.Origin.Z;
        var distance = MathF.Sqrt((dx * dx) + (dz * dz));

        var (min, max) = BandRange(bearing.Proximity);
        if (distance < min || distance > max)
            return false;

        // Practically on top of it: the bearing carries no information, and "immediately"
        // legitimately reports from arm's length.
        if (distance < 1f)
            return true;

        var delta = NormalizeAngle(MathF.Atan2(dx, dz) - bearing.HeadingRadians);
        return MathF.Abs(delta) <= bearing.HalfAngleRadians;
    }

    /// <summary>Candidates consistent with EVERY reading so far — the overlap.</summary>
    public static List<Vector3> Feasible(IEnumerable<Vector3>? candidates, IReadOnlyList<ElixirBearing>? bearings)
    {
        var result = new List<Vector3>();
        if (candidates is null)
            return result;

        foreach (var candidate in candidates)
        {
            if (SatisfiesAll(candidate, bearings))
                result.Add(candidate);
        }

        return result;
    }

    public static bool SatisfiesAll(Vector3 candidate, IReadOnlyList<ElixirBearing>? bearings)
    {
        if (bearings is null || bearings.Count == 0)
            return true;

        for (var i = 0; i < bearings.Count; i++)
        {
            if (!IsInsideCone(candidate, bearings[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Where to walk when there are no known candidate spots — samples a grid over the search
    /// area and averages whatever survives every cone. Null when the readings contradict each
    /// other, which is itself worth knowing: it means the arc is narrower than assumed, or a
    /// reading was taken after the treasure moved.
    /// </summary>
    public static Vector3? EstimateCentre(
        IReadOnlyList<ElixirBearing>? bearings, Vector3 searchCentre, float searchRadius, int samplesPerAxis = 48)
    {
        if (bearings is null || bearings.Count == 0 || searchRadius <= 0f || samplesPerAxis < 2)
            return null;

        var step = searchRadius * 2f / (samplesPerAxis - 1);
        var sum = Vector3.Zero;
        var hits = 0;

        for (var ix = 0; ix < samplesPerAxis; ix++)
        {
            for (var iz = 0; iz < samplesPerAxis; iz++)
            {
                var point = new Vector3(
                    searchCentre.X - searchRadius + (ix * step),
                    searchCentre.Y,
                    searchCentre.Z - searchRadius + (iz * step));

                if (Vector3.DistanceSquared(point, searchCentre) > searchRadius * searchRadius)
                    continue;
                if (!SatisfiesAll(point, bearings))
                    continue;

                sum += point;
                hits++;
            }
        }

        return hits == 0 ? null : sum / hits;
    }

    /// <summary>
    /// How much a further reading is worth from here. Two readings taken from nearly the same
    /// spot, or pointing nearly the same way, barely narrow anything — the overlap only tightens
    /// when the cones cross at an angle. Returns 0-1, and the UI uses it to say "walk further
    /// before reading again".
    /// </summary>
    public static float CrossingQuality(ElixirBearing a, ElixirBearing b)
    {
        var separation = Vector3.Distance(
            new Vector3(a.Origin.X, 0, a.Origin.Z),
            new Vector3(b.Origin.X, 0, b.Origin.Z));
        if (separation < 5f)
            return 0f;

        var spread = MathF.Abs(NormalizeAngle(a.HeadingRadians - b.HeadingRadians));
        return MathF.Min(1f, spread / (MathF.PI / 2f));
    }
}
