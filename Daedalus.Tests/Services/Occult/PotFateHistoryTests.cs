using System;
using Daedalus.Config;
using Daedalus.Data;
using Daedalus.Services.Occult;
using Dalamud.Plugin.Services;
using Moq;
using Xunit;

namespace Daedalus.Tests.Services.Occult;

/// <summary>
/// The countdown is derived from observation, so the sighting history has to survive a plugin
/// reload while you stay in the Horn — and must NOT survive leaving it, since the zone is
/// instanced. Field-reported 2026-07-31: two pot FATEs were missed because every Debug rebuild
/// silently reset the clock.
/// </summary>
public sealed class PotFateHistoryTests
{
    private const string NorthPot = "In a Pot of Bother";

    private static PotFateTracker Tracker(PhantomConfig config, ushort territory, out int saves)
    {
        var clientState = new Mock<IClientState>();
        clientState.Setup(x => x.TerritoryType).Returns(territory);

        var saveCount = 0;
        var tracker = new PotFateTracker(null, clientState.Object, null, null, config, () => saveCount++);
        saves = saveCount;
        return tracker;
    }

    private static PhantomConfig ConfigWithSighting(DateTime lastSeenUtc, double? cycle = null)
    {
        return new PhantomConfig
        {
            PotFateHistory =
            {
                [$"{PhantomJobData.NorthHornTerritoryId}:{NorthPot}"] = new PotFateSighting
                {
                    LastSeenUnixSeconds = new DateTimeOffset(lastSeenUtc, TimeSpan.Zero).ToUnixTimeSeconds(),
                    CycleSeconds = cycle,
                },
            },
        };
    }

    [Fact]
    public void StoredSighting_SurvivesConstruction()
    {
        var now = new DateTime(2026, 7, 31, 12, 0, 0, DateTimeKind.Utc);
        var config = ConfigWithSighting(now.AddMinutes(-10));

        var tracker = Tracker(config, PhantomJobData.NorthHornTerritoryId, out _);
        tracker.UtcNow = () => now;

        Assert.Equal(now.AddMinutes(-10), tracker.LastSeenUtc(NorthPot));
    }

    /// <summary>The regression: without persistence this is null after a reload and the HUD goes quiet.</summary>
    [Fact]
    public void StoredSighting_ProducesACountdownAfterReload()
    {
        var now = new DateTime(2026, 7, 31, 12, 0, 0, DateTimeKind.Utc);
        var config = ConfigWithSighting(now.AddMinutes(-10));

        var tracker = Tracker(config, PhantomJobData.NorthHornTerritoryId, out _);
        tracker.UtcNow = () => now;

        var seconds = tracker.SecondsUntilNextPot();

        Assert.NotNull(seconds);
        Assert.Equal(20 * 60, seconds!.Value, precision: 0);
    }

    [Fact]
    public void StoredCycle_IsRestoredAsMeasured()
    {
        var now = new DateTime(2026, 7, 31, 12, 0, 0, DateTimeKind.Utc);
        var config = ConfigWithSighting(now.AddMinutes(-5), cycle: 3300);

        var tracker = Tracker(config, PhantomJobData.NorthHornTerritoryId, out _);
        tracker.UtcNow = () => now;

        Assert.True(tracker.CycleIsMeasured(NorthPot));
    }

    [Fact]
    public void LeavingTheHorn_ClearsStoredHistory()
    {
        var config = ConfigWithSighting(new DateTime(2026, 7, 31, 11, 50, 0, DateTimeKind.Utc));

        // Limsa — not an Occult territory.
        var tracker = Tracker(config, 129, out _);
        tracker.Update();

        Assert.Empty(config.PotFateHistory);
        Assert.Null(tracker.LastSeenUtc(NorthPot));
    }

    [Fact]
    public void StayingInTheHorn_KeepsStoredHistory()
    {
        var config = ConfigWithSighting(new DateTime(2026, 7, 31, 11, 50, 0, DateTimeKind.Utc));

        var tracker = Tracker(config, PhantomJobData.NorthHornTerritoryId, out _);
        tracker.Update();

        Assert.Single(config.PotFateHistory);
    }

    /// <summary>
    /// Update runs every framework tick. Clearing must be a guarded no-op once the history is
    /// already empty, or standing in Limsa rewrites the config file continuously.
    /// </summary>
    [Fact]
    public void ClearingAnEmptyHistory_DoesNotSave()
    {
        var config = new PhantomConfig();
        var saveCount = 0;
        var clientState = new Mock<IClientState>();
        clientState.Setup(x => x.TerritoryType).Returns(129);

        var tracker = new PotFateTracker(null, clientState.Object, null, null, config, () => saveCount++);
        tracker.Update();
        tracker.Update();
        tracker.Update();

        Assert.Equal(0, saveCount);
    }

    [Fact]
    public void ClearingAPopulatedHistory_SavesExactlyOnce()
    {
        var config = ConfigWithSighting(new DateTime(2026, 7, 31, 11, 50, 0, DateTimeKind.Utc));
        var saveCount = 0;
        var clientState = new Mock<IClientState>();
        clientState.Setup(x => x.TerritoryType).Returns(129);

        var tracker = new PotFateTracker(null, clientState.Object, null, null, config, () => saveCount++);
        tracker.Update();
        tracker.Update();

        Assert.Equal(1, saveCount);
    }

    [Fact]
    public void MalformedHistoryKeys_AreIgnoredRatherThanThrowing()
    {
        var config = new PhantomConfig
        {
            PotFateHistory =
            {
                ["not-a-key"] = new PotFateSighting { LastSeenUnixSeconds = 1 },
                [":"] = new PotFateSighting { LastSeenUnixSeconds = 1 },
                ["99999999999:x"] = new PotFateSighting { LastSeenUnixSeconds = 1 },
            },
        };

        var tracker = Tracker(config, PhantomJobData.NorthHornTerritoryId, out _);

        Assert.Null(tracker.SecondsUntilNextPot());
    }
}
