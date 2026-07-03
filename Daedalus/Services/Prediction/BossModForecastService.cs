using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Daedalus.Timeline.Models;

namespace Daedalus.Services.Prediction;

/// <summary>
/// BossModReborn timeline IPC adapter feeding the overlay mechanic forecast.
/// Reads are cached for 250ms since the overlay queries every frame.
/// Fail-open: unavailable IPC reads as "nothing upcoming".
/// </summary>
public sealed class BossModForecastService : IBossModForecastService
{
    private const string PluginInternalName = "BossModReborn";
    private const double CacheRefreshSeconds = 0.25;

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IPluginLog? _log;

    private ICallGateSubscriber<bool>? _hasActiveModule;
    private ICallGateSubscriber<string?>? _activeModuleName;
    private ICallGateSubscriber<float>? _nextRaidwideIn;
    private ICallGateSubscriber<float>? _nextTankbusterIn;

    private DateTime _lastRefreshUtc = DateTime.MinValue;
    private bool _cachedAvailable;
    private bool _cachedHasActiveModule;
    private string? _cachedModuleName;
    private float _cachedRaidwideIn = float.MaxValue;
    private float _cachedTankbusterIn = float.MaxValue;

    public BossModForecastService(IDalamudPluginInterface pluginInterface, IPluginLog? log = null)
    {
        _pluginInterface = pluginInterface;
        _log = log;
    }

    public bool IsAvailable
    {
        get { Refresh(); return _cachedAvailable; }
    }

    public bool HasActiveModule
    {
        get { Refresh(); return _cachedHasActiveModule; }
    }

    public string? ActiveModuleName
    {
        get { Refresh(); return _cachedModuleName; }
    }

    public float NextRaidwideInSeconds
    {
        get { Refresh(); return _cachedRaidwideIn; }
    }

    public float NextTankbusterInSeconds
    {
        get { Refresh(); return _cachedTankbusterIn; }
    }

    /// <summary>
    /// Builds overlay forecast rows from raw BMR countdowns. Entries outside
    /// [0, windowSeconds] are dropped (float.MaxValue means "no such mechanic");
    /// results are sorted soonest-first. Names stay empty — BMR's timeline IPC
    /// exposes timings only, so rows render as tag + countdown.
    /// </summary>
    public static List<MechanicPrediction> BuildForecast(float raidwideIn, float tankbusterIn, float windowSeconds)
    {
        var results = new List<MechanicPrediction>(2);

        if (raidwideIn >= 0f && raidwideIn <= windowSeconds)
            results.Add(new MechanicPrediction(raidwideIn, TimelineEntryType.Raidwide, string.Empty, 1f));

        if (tankbusterIn >= 0f && tankbusterIn <= windowSeconds)
            results.Add(new MechanicPrediction(tankbusterIn, TimelineEntryType.TankBuster, string.Empty, 1f));

        results.Sort((a, b) => a.SecondsUntil.CompareTo(b.SecondsUntil));
        return results;
    }

    private void Refresh()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastRefreshUtc).TotalSeconds < CacheRefreshSeconds)
            return;
        _lastRefreshUtc = now;

        _cachedAvailable = IsPluginLoaded(PluginInternalName);
        if (!_cachedAvailable)
        {
            ResetCache();
            return;
        }

        EnsureSubscribers();

        try
        {
            _cachedHasActiveModule = _hasActiveModule?.InvokeFunc() ?? false;
        }
        catch (Exception ex)
        {
            _log?.Debug(ex, "[BossModForecastService] HasActiveModule failed; fail-open false.");
            _cachedHasActiveModule = false;
        }

        if (!_cachedHasActiveModule)
        {
            _cachedModuleName = null;
            _cachedRaidwideIn = float.MaxValue;
            _cachedTankbusterIn = float.MaxValue;
            return;
        }

        try { _cachedModuleName = _activeModuleName?.InvokeFunc(); }
        catch { _cachedModuleName = null; }

        try { _cachedRaidwideIn = _nextRaidwideIn?.InvokeFunc() ?? float.MaxValue; }
        catch { _cachedRaidwideIn = float.MaxValue; }

        try { _cachedTankbusterIn = _nextTankbusterIn?.InvokeFunc() ?? float.MaxValue; }
        catch { _cachedTankbusterIn = float.MaxValue; }
    }

    private void ResetCache()
    {
        _cachedHasActiveModule = false;
        _cachedModuleName = null;
        _cachedRaidwideIn = float.MaxValue;
        _cachedTankbusterIn = float.MaxValue;
    }

    private void EnsureSubscribers()
    {
        _hasActiveModule ??= _pluginInterface.GetIpcSubscriber<bool>("BossMod.HasActiveModule");
        _activeModuleName ??= _pluginInterface.GetIpcSubscriber<string?>("BossMod.ActiveModuleName");
        _nextRaidwideIn ??= _pluginInterface.GetIpcSubscriber<float>("BossMod.Timeline.NextRaidwideIn");
        _nextTankbusterIn ??= _pluginInterface.GetIpcSubscriber<float>("BossMod.Timeline.NextTankbusterIn");
    }

    private bool IsPluginLoaded(string internalName)
    {
        return _pluginInterface.InstalledPlugins.Any(p =>
            (p.InternalName.Equals(internalName, StringComparison.OrdinalIgnoreCase)
             || p.Name.Equals(internalName, StringComparison.OrdinalIgnoreCase))
            && p.IsLoaded);
    }
}
