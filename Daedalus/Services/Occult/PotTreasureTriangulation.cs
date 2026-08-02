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
/// One reading measured against where the coffer actually was.
/// <para>
/// <paramref name="AngularErrorRadians"/> is the whole arc question answered: the reported
/// direction versus the true bearing to the find. The arc must be at least as wide as the worst
/// error, so a handful of hunts replaces the 22.5° guess with a measurement.
/// </para>
/// </summary>
public readonly record struct ElixirCalibrationSample(
    ElixirProximity Band, float ActualDistance, float AngularErrorRadians);

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
    /// Half-width of a reading's arc: ±22.5°.
    /// <para>
    /// This is a CEILING, not a guess. Eight compass words partition 360° into 45° sectors, so a
    /// reported direction can mean at most ±22.5° — any wider and "south" would overlap
    /// "southeast" and the word could not tell them apart.
    /// </para>
    /// <para>
    /// So a measured worst-case error should come out at or below this. Well below (say ≤15°)
    /// means the arc can be TIGHTENED for sharper overlaps. ABOVE it means something is wrong
    /// rather than merely narrow — likely candidates: the game uses a sixteen-point compass
    /// (which would be ±11.25°, narrower still), the heading convention is off, or the reading's
    /// origin was recorded after the player had already moved.
    /// </para>
    /// </summary>
    public const float DefaultHalfAngleRadians = MathF.PI / 8f;

    /// <summary>Sector width of a sixteen-point compass, if the game turns out to use one.</summary>
    public const float SixteenPointHalfAngleRadians = MathF.PI / 16f;

    /// <summary>
    /// Distance band windows, in yalms.
    /// <para>
    /// ONLY "immediately" is confirmed (under 10y, field-reported). The rest are GUESSES built
    /// from "just outside targeting range" and "further still", so the windows deliberately
    /// OVERLAP. That is the safe direction to be wrong in: an over-wide band only costs search
    /// area, whereas bands that are too tight make two perfectly honest readings contradict each
    /// other and collapse the answer to nothing.
    /// </para>
    /// <para>
    /// These are calibratable rather than permanent guesses — the hunt ends with "You discover a
    /// treasure coffer!", so the distance from each reading's origin to the coffer is ground
    /// truth for the band that reading used. Enough hunts and the real edges fall out of the data.
    /// </para>
    /// </summary>
    public const float ImmediateRangeYalms = 10f;   // CONFIRMED
    public const float TargetRangeYalms = 30f;      // guess — "within targeting range" plus slack
    public const float FarInnerYalms = 20f;         // guess — overlaps Within on purpose
    public const float FarOuterYalms = 80f;         // guess
    public const float VeryFarInnerYalms = 50f;     // guess — overlaps Far on purpose

    /// <summary>Inclusive distance window a band allows, in yalms.</summary>
    public static (float Min, float Max) BandRange(ElixirProximity proximity) => proximity switch
    {
        ElixirProximity.Immediate => (0f, ImmediateRangeYalms),
        ElixirProximity.Within => (0f, TargetRangeYalms),
        ElixirProximity.Far => (FarInnerYalms, FarOuterYalms),
        ElixirProximity.VeryFar => (VeryFarInnerYalms, float.MaxValue),
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

    /// <summary>
    /// Ground truth for the guessed bands. The coffer does not exist as an object until you are
    /// within interact range, so it can never be spotted early — but the moment it appears, the
    /// distance from each earlier reading to that spot tells you what the band word actually
    /// meant. Collect these across hunts and the real band edges fall out.
    /// </summary>
    public static List<ElixirCalibrationSample> Calibrate(
        IReadOnlyList<ElixirBearing>? bearings, Vector3 discoveredAt)
    {
        var samples = new List<ElixirCalibrationSample>();
        if (bearings is null)
            return samples;

        foreach (var bearing in bearings)
        {
            var dx = discoveredAt.X - bearing.Origin.X;
            var dz = discoveredAt.Z - bearing.Origin.Z;
            var distance = MathF.Sqrt((dx * dx) + (dz * dz));

            // How far off the reported compass direction the treasure actually was. The arc has
            // to be at least this wide, so the largest error ever seen IS the half-angle —
            // no need to guess at 22.5 degrees once a few hunts have been measured.
            var error = distance < 1f
                ? 0f
                : MathF.Abs(NormalizeAngle(MathF.Atan2(dx, dz) - bearing.HeadingRadians));

            samples.Add(new ElixirCalibrationSample(bearing.Proximity, distance, error));
        }

        return samples;
    }

    /// <summary>
    /// Did every reading actually contain the coffer? A false here means an assumption is wrong —
    /// the arc is narrower than <see cref="DefaultHalfAngleRadians"/>, or a band edge is off — and
    /// is worth surfacing rather than silently tolerating.
    /// </summary>
    public static bool AllReadingsAgreeWith(IReadOnlyList<ElixirBearing>? bearings, Vector3 discoveredAt)
        => SatisfiesAll(discoveredAt, bearings);

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
