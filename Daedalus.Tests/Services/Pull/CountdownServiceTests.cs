using System;
using System.Collections.Generic;
using Daedalus.Services.Pull;
using Xunit;

namespace Daedalus.Tests.Services.Pull;

/// <summary>
/// LAN Phase 2: the shared pull T0 (countdown agent read + LAN mirror) and the pot-only
/// pre-pull scheduler.
/// </summary>
public class CountdownServiceTests
{
    private static (CountdownService svc, List<long> broadcasts, Func<DateTime> clock) Harness(
        Func<float?> agent, DateTime start)
    {
        var broadcasts = new List<long>();
        var now = start;
        var svc = new CountdownService(log: null, broadcastT0Ticks: broadcasts.Add);
        svc.ReadLocalCountdown = agent;
        svc.UtcNow = () => now;
        return (svc, broadcasts, () => now);
    }

    [Fact]
    public void LocalCountdown_SetsT0_AndBroadcastsOnStartEdge()
    {
        var start = DateTime.UtcNow;
        float? remaining = null;
        var broadcasts = new List<long>();
        var now = start;
        var svc = new CountdownService(log: null, broadcastT0Ticks: broadcasts.Add)
        {
            ReadLocalCountdown = () => remaining,
            UtcNow = () => now,
        };

        svc.Update();
        Assert.Null(svc.T0Utc);
        Assert.Empty(broadcasts);

        remaining = 15f; // countdown starts
        now = now.AddSeconds(0.3);
        svc.Update();

        Assert.NotNull(svc.T0Utc);
        Assert.InRange(svc.SecondsUntilPull!.Value, 14.5f, 15.5f);
        Assert.Single(broadcasts); // start edge mirrored once

        remaining = 14.7f; // ticking down — same countdown, no re-broadcast
        now = now.AddSeconds(0.3);
        svc.Update();
        Assert.Single(broadcasts);
    }

    [Fact]
    public void LocalCancel_BeforeT0_BroadcastsCancel_AndClears()
    {
        var start = DateTime.UtcNow;
        float? remaining = 20f;
        var broadcasts = new List<long>();
        var now = start;
        var svc = new CountdownService(log: null, broadcastT0Ticks: broadcasts.Add)
        {
            ReadLocalCountdown = () => remaining,
            UtcNow = () => now,
        };

        svc.Update();
        Assert.Single(broadcasts);

        remaining = null; // cancelled 20s early
        now = now.AddSeconds(1);
        svc.Update();

        Assert.Null(svc.T0Utc);
        Assert.Equal(2, broadcasts.Count);
        Assert.Equal(0, broadcasts[^1]); // 0 ticks = the cancel sentinel
    }

    [Fact]
    public void RemoteT0_AdoptedWhenNoLocal_LocalWins_SkewRejected()
    {
        var start = DateTime.UtcNow;
        float? remaining = null;
        var now = start;
        var svc = new CountdownService(log: null, broadcastT0Ticks: null)
        {
            ReadLocalCountdown = () => remaining,
            UtcNow = () => now,
        };

        // Adopt a sane remote T0.
        svc.OnRemoteCountdown(start.AddSeconds(12).Ticks);
        Assert.InRange(svc.SecondsUntilPull!.Value, 11.5f, 12.5f);

        // Reject one with a broken clock (>30s out).
        svc.OnRemoteCountdown(start.AddSeconds(120).Ticks);
        Assert.InRange(svc.SecondsUntilPull!.Value, 11.5f, 12.5f); // unchanged

        // A local countdown outranks the adopted remote.
        remaining = 5f;
        now = now.AddSeconds(0.3);
        svc.Update();
        Assert.InRange(svc.SecondsUntilPull!.Value, 4.5f, 5.5f);

        // Remote cancel clears the remote only.
        svc.OnRemoteCountdown(0);
        Assert.InRange(svc.SecondsUntilPull!.Value, 4.5f, 5.5f);
    }

    [Fact]
    public void RemoteT0_ExpiresAfterPull()
    {
        var start = DateTime.UtcNow;
        var now = start;
        var svc = new CountdownService(log: null, broadcastT0Ticks: null)
        {
            ReadLocalCountdown = () => null,
            UtcNow = () => now,
        };

        svc.OnRemoteCountdown(start.AddSeconds(3).Ticks);
        Assert.NotNull(svc.T0Utc);

        now = start.AddSeconds(4); // T0 passed
        Assert.Null(svc.T0Utc);
        Assert.Null(svc.SecondsUntilPull);
    }

    // ── Pot scheduler ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, 2f, false, false)]  // no countdown
    [InlineData(10f, 2f, false, false)]   // too early
    [InlineData(1.8f, 2f, false, true)]   // inside the window
    [InlineData(1.8f, 2f, true, false)]   // already potted this countdown
    [InlineData(-0.5f, 2f, false, false)] // T0 passed — never pot late
    public void PotScheduler_WindowMath(float? untilPull, float offset, bool fired, bool expected)
        => Assert.Equal(expected, PrePullPotScheduler.ShouldFirePot(untilPull, offset, fired));

    [Fact]
    public void PotScheduler_CastersPotEarlier()
    {
        Assert.Equal(2.0f, PrePullPotScheduler.PotOffsetSeconds(Daedalus.Data.JobRegistry.BlackMage));
        Assert.Equal(2.0f, PrePullPotScheduler.PotOffsetSeconds(Daedalus.Data.JobRegistry.WhiteMage));
        Assert.Equal(1.2f, PrePullPotScheduler.PotOffsetSeconds(Daedalus.Data.JobRegistry.Samurai));
        Assert.Equal(1.2f, PrePullPotScheduler.PotOffsetSeconds(Daedalus.Data.JobRegistry.Paladin));
    }
}
