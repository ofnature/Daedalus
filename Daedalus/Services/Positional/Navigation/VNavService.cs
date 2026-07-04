using System;
using System.Linq;
using System.Numerics;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace Daedalus.Services.Positional.Navigation;

/// <summary>
/// vnavmesh IPC adapter. Fail-open when the plugin or navmesh is unavailable.
/// </summary>
public sealed class VNavService : IVNavService
{
    private const string PluginInternalName = "vnavmesh";
    private const float DefaultFloorQueryHalfExtent = 1f;

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IPluginLog? _log;

    private ICallGateSubscriber<bool>? _navIsReady;
    private ICallGateSubscriber<bool>? _pathIsRunning;
    private ICallGateSubscriber<bool>? _pathfindInProgress;
    private ICallGateSubscriber<Vector3, bool, bool>? _pathfindAndMoveTo;
    private ICallGateSubscriber<Vector3, bool, float, bool>? _pathfindAndMoveCloseTo;
    private ICallGateSubscriber<object>? _pathStop;
    private ICallGateSubscriber<Vector3, bool, float, Vector3?>? _queryPointOnFloor;

    public VNavService(IDalamudPluginInterface pluginInterface, IPluginLog? log = null)
    {
        _pluginInterface = pluginInterface;
        _log = log;
    }

    public bool IsAvailable => IsPluginLoaded(PluginInternalName);

    public bool IsNavReady => TryInvoke(() => _navIsReady?.InvokeFunc() ?? false);

    public bool IsPathRunning => TryInvoke(() => _pathIsRunning?.InvokeFunc() ?? false);

    public bool IsPathfindInProgress => TryInvoke(() => _pathfindInProgress?.InvokeFunc() ?? false);

    public VNavMoveResult PathfindAndMoveTo(Vector3 destination, bool fly = false)
    {
        if (!IsAvailable)
            return VNavMoveResult.PluginUnavailable;

        if (!IsNavReady)
            return VNavMoveResult.NavmeshNotReady;

        EnsureSubscribers();

        try
        {
            return _pathfindAndMoveTo?.InvokeFunc(destination, fly) == true
                ? VNavMoveResult.Queued
                : VNavMoveResult.Busy;
        }
        catch (Exception ex)
        {
            _log?.Warning(ex, "[VNavService] PathfindAndMoveTo failed.");
            return VNavMoveResult.PluginUnavailable;
        }
    }

    public VNavMoveResult PathfindAndMoveCloseTo(Vector3 destination, float toleranceYalms, bool fly = false)
    {
        if (!IsAvailable)
            return VNavMoveResult.PluginUnavailable;

        if (!IsNavReady)
            return VNavMoveResult.NavmeshNotReady;

        EnsureSubscribers();

        try
        {
            return _pathfindAndMoveCloseTo?.InvokeFunc(destination, fly, toleranceYalms) == true
                ? VNavMoveResult.Queued
                : VNavMoveResult.Busy;
        }
        catch (Exception ex)
        {
            // SimpleMove.PathfindAndMoveCloseTo is missing on older vnavmesh builds — fall back
            // to a plain move to the destination so callers still get there (they computed the
            // destination; the tolerance only relaxed the arrival point).
            _log?.Warning(ex, "[VNavService] PathfindAndMoveCloseTo unavailable — falling back to PathfindAndMoveTo.");
            return PathfindAndMoveTo(destination, fly);
        }
    }

    public void Stop()
    {
        if (!IsAvailable)
            return;

        EnsureSubscribers();
        TryInvoke(() => _pathStop?.InvokeAction());
    }

    public Vector3 SnapToFloor(Vector3 position)
    {
        if (!IsAvailable)
            return position;

        EnsureSubscribers();

        try
        {
            var snapped = _queryPointOnFloor?.InvokeFunc(position, false, DefaultFloorQueryHalfExtent);
            return snapped ?? position;
        }
        catch (Exception ex)
        {
            _log?.Debug(ex, "[VNavService] SnapToFloor failed; using raw position.");
            return position;
        }
    }

    private void EnsureSubscribers()
    {
        _navIsReady ??= _pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        _pathIsRunning ??= _pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        _pathfindInProgress ??= _pluginInterface.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");
        _pathfindAndMoveTo ??= _pluginInterface.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo");
        _pathfindAndMoveCloseTo ??= _pluginInterface.GetIpcSubscriber<Vector3, bool, float, bool>("vnavmesh.SimpleMove.PathfindAndMoveCloseTo");
        _pathStop ??= _pluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
        _queryPointOnFloor ??= _pluginInterface.GetIpcSubscriber<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor");
    }

    private bool IsPluginLoaded(string internalName)
    {
        return _pluginInterface.InstalledPlugins.Any(p =>
            (p.InternalName.Equals(internalName, StringComparison.OrdinalIgnoreCase)
             || p.Name.Equals(internalName, StringComparison.OrdinalIgnoreCase))
            && p.IsLoaded);
    }

    private static T TryInvoke<T>(Func<T> func, T fallback = default!)
    {
        try
        {
            return func();
        }
        catch
        {
            return fallback;
        }
    }

    private static void TryInvoke(System.Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // fail-open
        }
    }
}
