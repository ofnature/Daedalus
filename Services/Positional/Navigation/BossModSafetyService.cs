using System;
using System.Linq;
using System.Numerics;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace Olympus.Services.Positional.Navigation;

/// <summary>
/// BossModReborn IPC adapter. Fail-open: unavailable IPC reads as safe for rotation continuity.
/// </summary>
public sealed class BossModSafetyService : IBossModSafetyService
{
    private const string PluginInternalName = "BossModReborn";

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IPluginLog? _log;

    private ICallGateSubscriber<Vector3, bool>? _isPositionSafe;
    private ICallGateSubscriber<Vector3, Vector3, bool>? _isDashSafe;
    private ICallGateSubscriber<float>? _nextDamageIn;
    private ICallGateSubscriber<float>? _forbiddenZonesNextActivation;

    private float _snapshotNextDamageIn = float.MaxValue;
    private float _snapshotForbiddenZoneIn = float.MaxValue;

    public BossModSafetyService(IDalamudPluginInterface pluginInterface, IPluginLog? log = null)
    {
        _pluginInterface = pluginInterface;
        _log = log;
    }

    public bool IsAvailable => IsPluginLoaded(PluginInternalName);

    public float NextDamageInSeconds => ReadNextDamageIn();

    public float ForbiddenZoneActivationInSeconds => ReadForbiddenZoneActivationIn();

    public void BeginUpdateSnapshot()
    {
        _snapshotNextDamageIn = ReadNextDamageIn();
        _snapshotForbiddenZoneIn = ReadForbiddenZoneActivationIn();
    }

    public bool ShouldAbortMovement()
    {
        if (!IsAvailable)
            return false;

        var nextDamage = ReadNextDamageIn();
        var forbidden = ReadForbiddenZoneActivationIn();

        // New telegraph: predicted activation moved closer since the snapshot.
        if (nextDamage + PositionalMovementConstants.TelegraphAbortEpsilonSeconds < _snapshotNextDamageIn)
            return true;

        if (forbidden + PositionalMovementConstants.TelegraphAbortEpsilonSeconds < _snapshotForbiddenZoneIn)
            return true;

        return false;
    }

    public PositionSafety QueryPositionSafety(Vector3 destination, float imminentWindowSeconds = PositionalMovementConstants.DefaultImminentWindowSeconds)
    {
        if (!IsAvailable)
            return PositionSafety.Safe;

        EnsureSubscribers();

        try
        {
            if (_isPositionSafe == null)
                return PositionSafety.Safe;

            if (!_isPositionSafe.InvokeFunc(destination))
                return PositionSafety.Unsafe;

            var nextDamage = ReadNextDamageIn();
            var forbidden = ReadForbiddenZoneActivationIn();
            if (nextDamage <= imminentWindowSeconds || forbidden <= imminentWindowSeconds)
                return PositionSafety.Imminent;

            return PositionSafety.Safe;
        }
        catch (Exception ex)
        {
            _log?.Debug(ex, "[BossModSafetyService] QueryPositionSafety failed; fail-open Safe.");
            return PositionSafety.Safe;
        }
    }

    public bool IsSegmentSafe(Vector3 from, Vector3 to)
    {
        if (!IsAvailable)
            return true;

        EnsureSubscribers();

        try
        {
            return _isDashSafe?.InvokeFunc(from, to) ?? true;
        }
        catch (Exception ex)
        {
            _log?.Debug(ex, "[BossModSafetyService] IsSegmentSafe failed; fail-open true.");
            return true;
        }
    }

    private float ReadNextDamageIn()
    {
        if (!IsAvailable)
            return float.MaxValue;

        EnsureSubscribers();

        try
        {
            return _nextDamageIn?.InvokeFunc() ?? float.MaxValue;
        }
        catch
        {
            return float.MaxValue;
        }
    }

    private float ReadForbiddenZoneActivationIn()
    {
        if (!IsAvailable)
            return float.MaxValue;

        EnsureSubscribers();

        try
        {
            return _forbiddenZonesNextActivation?.InvokeFunc() ?? float.MaxValue;
        }
        catch
        {
            return float.MaxValue;
        }
    }

    private void EnsureSubscribers()
    {
        _isPositionSafe ??= _pluginInterface.GetIpcSubscriber<Vector3, bool>("BossMod.Hints.IsPositionSafe");
        _isDashSafe ??= _pluginInterface.GetIpcSubscriber<Vector3, Vector3, bool>("BossMod.Hints.IsDashSafe");
        _nextDamageIn ??= _pluginInterface.GetIpcSubscriber<float>("BossMod.Hints.NextDamageIn");
        _forbiddenZonesNextActivation ??= _pluginInterface.GetIpcSubscriber<float>("BossMod.Hints.ForbiddenZonesNextActivation");
    }

    private bool IsPluginLoaded(string internalName)
    {
        return _pluginInterface.InstalledPlugins.Any(p =>
            (p.InternalName.Equals(internalName, StringComparison.OrdinalIgnoreCase)
             || p.Name.Equals(internalName, StringComparison.OrdinalIgnoreCase))
            && p.IsLoaded);
    }
}
