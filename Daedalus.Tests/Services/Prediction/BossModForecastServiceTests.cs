using Daedalus.Services.Prediction;
using Daedalus.Timeline.Models;
using Xunit;

namespace Daedalus.Tests.Services.Prediction;

/// <summary>
/// Tests for <see cref="BossModForecastService.BuildForecast"/> — the pure builder that turns
/// raw BMR Timeline IPC countdowns into overlay forecast rows.
/// </summary>
public sealed class BossModForecastServiceTests
{
    private const float Window = 60f;

    [Fact]
    public void BuildForecast_NoMechanics_ReturnsEmpty()
    {
        var result = BossModForecastService.BuildForecast(float.MaxValue, float.MaxValue, Window);

        Assert.Empty(result);
    }

    [Fact]
    public void BuildForecast_RaidwideOnly_ReturnsSingleRaidwideRow()
    {
        var result = BossModForecastService.BuildForecast(12.5f, float.MaxValue, Window);

        var m = Assert.Single(result);
        Assert.Equal(TimelineEntryType.Raidwide, m.Type);
        Assert.Equal(12.5f, m.SecondsUntil);
        Assert.Equal(string.Empty, m.Name);
    }

    [Fact]
    public void BuildForecast_TankbusterOnly_ReturnsSingleTankBusterRow()
    {
        var result = BossModForecastService.BuildForecast(float.MaxValue, 8f, Window);

        var m = Assert.Single(result);
        Assert.Equal(TimelineEntryType.TankBuster, m.Type);
        Assert.Equal(8f, m.SecondsUntil);
    }

    [Fact]
    public void BuildForecast_BothPresent_SortedSoonestFirst()
    {
        var result = BossModForecastService.BuildForecast(30f, 5f, Window);

        Assert.Equal(2, result.Count);
        Assert.Equal(TimelineEntryType.TankBuster, result[0].Type);
        Assert.Equal(5f, result[0].SecondsUntil);
        Assert.Equal(TimelineEntryType.Raidwide, result[1].Type);
        Assert.Equal(30f, result[1].SecondsUntil);
    }

    [Fact]
    public void BuildForecast_BeyondWindow_IsDropped()
    {
        var result = BossModForecastService.BuildForecast(61f, 45f, Window);

        var m = Assert.Single(result);
        Assert.Equal(TimelineEntryType.TankBuster, m.Type);
    }

    [Fact]
    public void BuildForecast_NegativeCountdown_IsDropped()
    {
        // BMR computes (next - DateTime.Now); a mechanic resolving this instant can read negative.
        var result = BossModForecastService.BuildForecast(-0.5f, -3f, Window);

        Assert.Empty(result);
    }

    [Fact]
    public void BuildForecast_ZeroCountdown_IsIncluded()
    {
        var result = BossModForecastService.BuildForecast(0f, float.MaxValue, Window);

        var m = Assert.Single(result);
        Assert.Equal(0f, m.SecondsUntil);
        Assert.True(m.IsImminent);
    }

    [Fact]
    public void BuildForecast_ImminentAndSoonThresholds_MatchOverlaySeverity()
    {
        var result = BossModForecastService.BuildForecast(2.9f, 7.9f, Window);

        Assert.True(result[0].IsImminent);
        Assert.False(result[1].IsImminent);
        Assert.True(result[1].IsSoon);
    }
}
