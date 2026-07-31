using System;
using Daedalus.Services.Occult;
using Xunit;

namespace Daedalus.Tests.Services.Occult;

/// <summary>
/// Doom top-off board (2026-07-31): Necromancer Deep Freeze Dooms its caster and the Doom is
/// dispelled ONLY at 100% HP, so a Doomed toon at 90% outranks a healthy one at 40% — the
/// inversion HP-deficit healing gets wrong. Local healers already catch Doom 1769 from the
/// status list; this board is the cross-box announcement. Static state → serialized collection.
/// </summary>
[Collection("DoomTopOffWatchState")]
public class DoomTopOffWatchTests : IDisposable
{
    private DateTime _now = new(2026, 7, 31, 12, 0, 0, DateTimeKind.Utc);

    public DoomTopOffWatchTests()
    {
        DoomTopOffWatch.Reset();
        DoomTopOffWatch.UtcNow = () => _now;
    }

    public void Dispose() => DoomTopOffWatch.Reset();

    private void Advance(double seconds) => _now = _now.AddSeconds(seconds);

    [Fact]
    public void Request_MarksTheToonAndFiresTheAnnouncement()
    {
        string? announced = null;
        DoomTopOffWatch.OnLocalRequest = n => announced = n;

        DoomTopOffWatch.RequestTopOff("Saar Ishere");

        Assert.True(DoomTopOffWatch.NeedsTopOff("Saar Ishere"));
        Assert.Equal("Saar Ishere", announced);
    }

    [Fact]
    public void Record_DoesNotReBroadcast()
    {
        var announcements = 0;
        DoomTopOffWatch.OnLocalRequest = _ => announcements++;

        DoomTopOffWatch.Record("Korha Ishere"); // arrived over LAN from another box

        Assert.True(DoomTopOffWatch.NeedsTopOff("Korha Ishere"));
        Assert.Equal(0, announcements); // never echo a received request back onto the wire
    }

    [Fact]
    public void Request_ExpiresAfterTheDoomWindow()
    {
        DoomTopOffWatch.RequestTopOff("Saar Ishere");
        Advance(DoomTopOffWatch.RequestTtlSeconds + 0.1);

        Assert.False(DoomTopOffWatch.NeedsTopOff("Saar Ishere"));
        Assert.Empty(DoomTopOffWatch.ActiveRequests());
    }

    [Fact]
    public void Ttl_OutlivesTheTenSecondDoom()
    {
        // The Doom itself is 10s — the request must survive long enough for the heal to land.
        Assert.True(DoomTopOffWatch.RequestTtlSeconds > 10.0);
    }

    [Fact]
    public void Clear_EndsTheRequestEarly()
    {
        DoomTopOffWatch.RequestTopOff("Saar Ishere");
        DoomTopOffWatch.Clear("Saar Ishere");

        Assert.False(DoomTopOffWatch.NeedsTopOff("Saar Ishere"));
    }

    [Fact]
    public void UnknownOrEmptyNames_NeverNeedTopOff()
    {
        Assert.False(DoomTopOffWatch.NeedsTopOff("Nobody"));
        Assert.False(DoomTopOffWatch.NeedsTopOff(""));
        DoomTopOffWatch.RequestTopOff("   "); // whitespace is ignored, not stored
        Assert.Empty(DoomTopOffWatch.ActiveRequests());
    }

    [Fact]
    public void NameMatching_IsCaseInsensitive()
    {
        DoomTopOffWatch.Record("Saar Ishere");
        Assert.True(DoomTopOffWatch.NeedsTopOff("saar ishere"));
    }
}
