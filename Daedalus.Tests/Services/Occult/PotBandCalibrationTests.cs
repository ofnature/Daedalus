using System;
using Daedalus.Config;
using Daedalus.Services.Occult;
using Xunit;

namespace Daedalus.Tests.Services.Occult;

/// <summary>
/// Band calibration turns the shipped guesses into measurements. Only "immediately" was ever
/// confirmed; the rest were estimates, and the plain band's 30y estimate was wrong by half —
/// found by hand on 2026-08-15 after coffers kept landing outside every cone.
/// <para>
/// Serialised with the other tests that read <c>BandRange</c>, because the measurement hook is
/// static and would otherwise leak across parallel classes.
/// </para>
/// </summary>
[Collection("PotBandStaticState")]
public sealed class PotBandCalibrationTests : IDisposable
{
    private readonly PhantomConfig _config = new();

    public void Dispose() => PotTreasureTriangulation.MeasuredBandMax = null;

    private PotTreasureHunt Hunt() => new(null, null, null, _config);

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

        var hunt = Hunt();          // the constructor installs
        var (_, live) = PotTreasureTriangulation.BandRange(ElixirProximity.Within);

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
}
