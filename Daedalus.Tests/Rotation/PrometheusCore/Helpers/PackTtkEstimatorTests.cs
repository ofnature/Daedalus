using System;
using Daedalus.Rotation.PrometheusCore.Helpers;
using Xunit;

namespace Daedalus.Tests.Rotation.PrometheusCore.Helpers;

/// <summary>
/// Pack time-to-kill estimator for the Queen hold (2026-07-01). Trash packs don't get LOW, they
/// MELT — the HP% hold missed a Queen deployed 2.5s before pack death onto a mob at ~45% HP; the
/// rolling kill-rate estimate catches exactly that.
/// </summary>
public class PackTtkEstimatorTests
{
    private static readonly DateTime T0 = new(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Estimate_MeltingPack_ReportsShortTtk()
    {
        // The in-game case: ~200k HP pack losing ~50k/s → ~4s to live.
        var est = new PackTtkEstimator();
        est.Sample(400_000, T0);
        est.Sample(300_000, T0.AddSeconds(2));
        est.Sample(200_000, T0.AddSeconds(4));

        var ttk = est.EstimateTtkSeconds();
        Assert.NotNull(ttk);
        Assert.InRange(ttk!.Value, 3.5f, 4.5f);
    }

    [Fact]
    public void Estimate_NullWhileSpanTooShort()
    {
        var est = new PackTtkEstimator();
        est.Sample(400_000, T0);
        est.Sample(390_000, T0.AddSeconds(0.5));
        Assert.Null(est.EstimateTtkSeconds());
    }

    [Fact]
    public void Estimate_NullOnFreshPull_WhenHpJumpsUp()
    {
        // New pack entering the window: total HP rises → negative kill rate → no estimate → no hold.
        var est = new PackTtkEstimator();
        est.Sample(50_000, T0);
        est.Sample(30_000, T0.AddSeconds(2));
        est.Sample(1_500_000, T0.AddSeconds(4)); // next pack pulled
        Assert.Null(est.EstimateTtkSeconds());
    }

    [Fact]
    public void Estimate_OldSamplesFallOutOfWindow()
    {
        // A burst 10s ago must not skew the current rate — only the ~6s window counts.
        var est = new PackTtkEstimator();
        est.Sample(2_000_000, T0);                    // dropped (outside window)
        est.Sample(1_000_000, T0.AddSeconds(10));
        est.Sample(950_000, T0.AddSeconds(13));       // 50k over 3s → ~19s TTK

        var ttk = est.EstimateTtkSeconds();
        Assert.NotNull(ttk);
        Assert.InRange(ttk!.Value, 50f, 65f); // 950k / (50k/3s) ≈ 57s — slow kill, no hold
    }

    [Fact]
    public void Reset_ClearsSamples()
    {
        var est = new PackTtkEstimator();
        est.Sample(400_000, T0);
        est.Sample(200_000, T0.AddSeconds(3));
        est.Reset();
        Assert.Null(est.EstimateTtkSeconds());
    }
}
