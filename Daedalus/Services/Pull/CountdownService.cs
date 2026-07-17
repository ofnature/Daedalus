using System;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace Daedalus.Services.Pull;

/// <summary>
/// LAN Phase 2 — the shared pull T0. The in-game countdown is read LOCALLY from the countdown
/// agent (every partied client sees the same timer, so no protocol is needed for the standard
/// fleet-in-one-party case); a LAN mirror broadcast covers fleet toons outside the party. The
/// local read always wins over an adopted remote T0. Remote T0s more than 30s out are rejected
/// (clock-skew guard — Windows NTP plus LAN latency is single-digit ms; anything bigger is a
/// broken clock, log loudly and ignore).
/// </summary>
public interface ICountdownService
{
    /// <summary>Seconds until the shared pull T0 (null = no countdown running).</summary>
    float? SecondsUntilPull { get; }

    /// <summary>The absolute pull time (null = no countdown).</summary>
    DateTime? T0Utc { get; }
}

public sealed class CountdownService : ICountdownService
{
    private const double MaxRemoteSkewSeconds = 30.0;
    private const double UpdateThrottleSeconds = 0.2;

    private readonly IPluginLog? _log;
    private readonly Action<long>? _broadcastT0Ticks;

    private DateTime _lastUpdateUtc = DateTime.MinValue;
    private DateTime? _localT0;
    private DateTime? _remoteT0;
    private bool _localWasActive;

    /// <summary>Injectable clock + agent read for tests.</summary>
    internal Func<DateTime> UtcNow = () => DateTime.UtcNow;
    internal Func<float?> ReadLocalCountdown;

    public CountdownService(IPluginLog? log = null, Action<long>? broadcastT0Ticks = null)
    {
        _log = log;
        _broadcastT0Ticks = broadcastT0Ticks;
        ReadLocalCountdown = ReadCountdownAgent;
    }

    public DateTime? T0Utc
    {
        get
        {
            var now = UtcNow();
            if (_localT0 is { } local && local > now) return local;
            if (_remoteT0 is { } remote && remote > now) return remote;
            return null;
        }
    }

    public float? SecondsUntilPull
        => T0Utc is { } t0 ? (float)(t0 - UtcNow()).TotalSeconds : null;

    /// <summary>Framework tick (throttled): read the agent, keep T0, mirror edges onto the LAN.</summary>
    public void Update()
    {
        var now = UtcNow();
        if ((now - _lastUpdateUtc).TotalSeconds < UpdateThrottleSeconds)
            return;
        _lastUpdateUtc = now;

        var remaining = ReadLocalCountdown();
        if (remaining is { } r && r > 0f)
        {
            var t0 = now.AddSeconds(r);
            // Broadcast on the START edge (or a re-issued countdown shifting T0 by >1s).
            if (!_localWasActive || _localT0 is not { } prev || Math.Abs((t0 - prev).TotalSeconds) > 1.0)
                _broadcastT0Ticks?.Invoke(t0.Ticks);
            _localT0 = t0;
            _localWasActive = true;
        }
        else
        {
            // Agent inactive: a countdown that vanishes BEFORE its T0 was cancelled — mirror the
            // cancel; one that ran out just completed (T0 passed, nothing to say).
            if (_localWasActive && _localT0 is { } t0 && t0 > now.AddSeconds(0.5))
            {
                _broadcastT0Ticks?.Invoke(0);
                _log?.Debug("[Countdown] local countdown cancelled");
            }
            if (_localWasActive)
                _localT0 = null;
            _localWasActive = false;
        }
    }

    /// <summary>A CountdownStart mirror arrived from the fleet (0 ticks = cancel).</summary>
    public void OnRemoteCountdown(long t0Ticks)
    {
        if (t0Ticks == 0)
        {
            _remoteT0 = null;
            return;
        }

        var t0 = new DateTime(t0Ticks, DateTimeKind.Utc);
        var skew = Math.Abs((t0 - UtcNow()).TotalSeconds);
        if (skew > MaxRemoteSkewSeconds)
        {
            _log?.Warning($"[Countdown] rejected remote T0 {skew:F0}s away — check the machines' clocks (NTP)");
            return;
        }
        _remoteT0 = t0;
    }

    private static unsafe float? ReadCountdownAgent()
    {
        try
        {
            var agent = AgentCountDownSettingDialog.Instance();
            return agent != null && agent->Active ? agent->TimeRemaining : null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Phase 2's "pot-only cut": pure scheduling math for the countdown-aligned pre-pull tincture.
/// Casters pot earlier (their openers start with a hard cast at T0−cast); everyone else grabs it
/// in the last moments. One pot per countdown — the latch is keyed on T0 so a re-issued countdown
/// re-arms.
/// </summary>
public static class PrePullPotScheduler
{
    /// <summary>Job-specific pot offset before T0 (seconds).</summary>
    public static float PotOffsetSeconds(uint jobId)
        => Daedalus.Data.JobRegistry.IsCasterDps(jobId) || Daedalus.Data.JobRegistry.IsHealer(jobId)
            ? 2.0f
            : 1.2f;

    /// <summary>
    /// True when the pot should fire THIS frame: inside the offset window, before T0, and not
    /// already fired for this countdown.
    /// </summary>
    public static bool ShouldFirePot(float? secondsUntilPull, float offsetSeconds, bool alreadyFiredForThisT0)
        => !alreadyFiredForThisT0
           && secondsUntilPull is { } s
           && s > 0f
           && s <= offsetSeconds;
}
