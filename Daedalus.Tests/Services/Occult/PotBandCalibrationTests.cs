using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Daedalus.Config;
using Daedalus.Services.Occult;
using Xunit;

namespace Daedalus.Tests.Services.Occult;

/// <summary>
/// Band calibration turns the shipped guesses into measurements. Only "immediately" was ever
/// confirmed; the rest were estimates, and the plain band's 30y estimate was wrong by half —
/// found by hand on 2026-08-15 after coffers kept landing outside every cone.
/// <para>
/// Hunts here never install their measurements into the static <c>BandRange</c> hook: xUnit runs
/// classes in parallel, so whichever test built a hunt last decided what every other class read.
/// What a hunt would install is checked through <c>BandRangeWith</c> instead.
/// </para>
/// </summary>
public sealed class PotBandCalibrationTests
{
    private readonly PhantomConfig _config = new();

    private PotTreasureHunt Hunt() => new(null, null, null, _config, null, null, installMeasuredBands: false);

    private void Record(ElixirProximity band, float distance)
        => _config.PotHuntCalibration.Add(new PotHuntCalibrationSample
        {
            Band = band.ToString(),
            ActualDistance = distance,
            AngularErrorRadians = 0f,
        });

    [Fact]
    public void NoSamples_LeavesTheShippedBandAlone()
    {
        var hunt = Hunt();
        var (_, shipped) = PotTreasureTriangulation.ShippedBandRange(ElixirProximity.Within);

        Assert.Equal(0, hunt.BandSampleCount(ElixirProximity.Within));
        Assert.Null(hunt.MaxObservedBandDistance(ElixirProximity.Within));
        Assert.Equal(shipped, hunt.SuggestedBandMax(ElixirProximity.Within));
        Assert.False(hunt.IsBandWidened(ElixirProximity.Within));
    }

    /// <summary>
    /// The core asymmetry. A find INSIDE the band proves nothing about where the edge is, so the
    /// ceiling must not come down — narrowing would exclude the next honest case.
    /// </summary>
    [Fact]
    public void AFindInsideTheBand_NeverNarrowsIt()
    {
        Record(ElixirProximity.Within, 12f);
        var hunt = Hunt();
        var (_, shipped) = PotTreasureTriangulation.ShippedBandRange(ElixirProximity.Within);

        Assert.Equal(1, hunt.BandSampleCount(ElixirProximity.Within));
        Assert.Equal(12f, hunt.MaxObservedBandDistance(ElixirProximity.Within));
        Assert.Equal(shipped, hunt.SuggestedBandMax(ElixirProximity.Within));
        Assert.False(hunt.IsBandWidened(ElixirProximity.Within));
    }

    /// <summary>
    /// A find BEYOND the edge is a counterexample, not an estimate — one is proof. This is the
    /// case the arc's 100-sample threshold would have sat on while knowingly using a wrong edge.
    /// </summary>
    [Fact]
    public void OneFindBeyondTheEdge_WidensItImmediately()
    {
        var (_, shipped) = PotTreasureTriangulation.ShippedBandRange(ElixirProximity.Within);
        Record(ElixirProximity.Within, shipped + 20f);

        var hunt = Hunt();
        Assert.True(hunt.IsBandWidened(ElixirProximity.Within));
        Assert.Equal(shipped + 20f + PotTreasureHunt.BandMarginYalms,
            hunt.SuggestedBandMax(ElixirProximity.Within));
    }

    [Fact]
    public void TheFurthestFindWins_NotTheLatest()
    {
        var (_, shipped) = PotTreasureTriangulation.ShippedBandRange(ElixirProximity.Within);
        Record(ElixirProximity.Within, shipped + 30f);
        Record(ElixirProximity.Within, shipped + 5f);

        Assert.Equal(shipped + 30f, Hunt().MaxObservedBandDistance(ElixirProximity.Within));
    }

    /// <summary>Bands are measured independently — a far find must not widen the plain band.</summary>
    [Fact]
    public void BandsDoNotBleedIntoEachOther()
    {
        Record(ElixirProximity.Far, 500f);
        var hunt = Hunt();

        Assert.Equal(0, hunt.BandSampleCount(ElixirProximity.Within));
        Assert.False(hunt.IsBandWidened(ElixirProximity.Within));
    }

    /// <summary>
    /// The measurement has to reach the geometry, or it is a number in a config file. Installing
    /// it must move BandRange itself, which is what the cones, the surviving region and the
    /// estimate all read.
    /// </summary>
    [Fact]
    public void InstallingMeasurements_MovesTheGeometry()
    {
        var (_, shipped) = PotTreasureTriangulation.ShippedBandRange(ElixirProximity.Within);
        Record(ElixirProximity.Within, shipped + 25f);

        var hunt = Hunt();
        var (_, live) = PotTreasureTriangulation.BandRangeWith(hunt.MeasuredBands, ElixirProximity.Within);

        Assert.True(live > shipped, "BandRange must reflect the measurement");
        Assert.Equal(hunt.SuggestedBandMax(ElixirProximity.Within), live);

        // And the shipped view stays available, unmoved, for reporting the difference.
        Assert.Equal(shipped, PotTreasureTriangulation.ShippedBandRange(ElixirProximity.Within).Max);
    }

    /// <summary>An unbounded band cannot be widened — VeryFar already runs to the horizon.</summary>
    [Fact]
    public void AnUnboundedBand_IsNeverWidened()
    {
        Record(ElixirProximity.VeryFar, 900f);
        Assert.False(Hunt().IsBandWidened(ElixirProximity.VeryFar));
    }

    // ---------------- guard ----------------

    /// <summary>
    /// No test may write the band hook: not by assigning it, not through InstallMeasuredBands, and
    /// not by building a hunt with the public constructor, which installs. The cone and region
    /// tests read it through BandRange in parallel with whatever test wrote it.
    /// </summary>
    [Fact]
    public void NoTestInstallsTheStaticBandHook()
    {
        var root = FindTestsRoot();
        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => WritesTheHook(File.ReadAllText(f)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(offenders);
    }

    private static bool WritesTheHook(string source)
        => source.Contains(Assignment)
           || source.Contains(Install)
           || HuntConstruction.Matches(source).Any(m => !m.Value.Contains(OptOut));

    // Built at runtime so this file doesn't match its own search.
    private static readonly string Assignment = "PotTreasureTriangulation." + "MeasuredBandMax =";
    private static readonly string Install = "." + "InstallMeasuredBands(";
    private static readonly string OptOut = "installMeasuredBands" + ": false";

    // A hunt built in a test, up to the ';': the explicit constructor call, or a target-typed one
    // opening with three nulls (no chat, object table or client state is how a headless hunt starts).
    private static readonly Regex HuntConstruction = new(
        @"new\s*(?:" + "PotTreasureHunt" + @"\s*\(|\(\s*null\s*,\s*null\s*,\s*null\s*,)[^;]*;");

    private static string FindTestsRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Daedalus.Tests.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
