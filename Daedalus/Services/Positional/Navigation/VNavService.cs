using System;
using System.Linq;
using System.Numerics;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace Daedalus.Services.Positional.Navigation;

/// <summary>
/// Navmesh IPC adapter: vnavmesh, or Ariadne (a vnavmesh fork with the same call shapes under
/// <c>Ariadne.*</c>), chosen in Settings ▸ General ▸ Boss handling ▸ Navmesh plugin. Fail-open when the
/// plugin or navmesh is unavailable.
/// <para>
/// Only the calls that MOVE the character follow the choice. The floor query stays on the
/// <c>vnavmesh.*</c> name, answered by vnavmesh when it is loaded and by Ariadne's compatibility gates
/// when it is not: it moves nothing, and Ariadne's own version returns a task rather than an answer,
/// which a per-frame floor check cannot wait on.
/// </para>
/// </summary>
public sealed class VNavService : IVNavService
{
    private const float DefaultFloorQueryHalfExtent = 1f;

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IPluginLog? _log;
    private readonly Func<Daedalus.Config.NavmeshPlugin> _choice;

    /// <summary>One plugin's movement gates.</summary>
    private sealed class Gates
    {
        public ICallGateSubscriber<bool>? NavIsReady;
        public ICallGateSubscriber<bool>? PathIsRunning;
        public ICallGateSubscriber<bool>? PathfindInProgress;
        public ICallGateSubscriber<Vector3, bool, bool>? PathfindAndMoveTo;
        public ICallGateSubscriber<Vector3, bool, float, bool>? PathfindAndMoveCloseTo;
        public ICallGateSubscriber<object>? PathStop;
    }

    private readonly System.Collections.Generic.Dictionary<string, Gates> _gates = new();
    private ICallGateSubscriber<Vector3, bool, float, Vector3?>? _queryPointOnFloor;

    // The plugin that was driving when this service last started a move, so a switch mid-path still
    // stops the walk the old one is running.
    private string? _lastMover;

    /// <summary>The plugin driving movement right now, for the readouts. Static: the settings page reads it.</summary>
    public static string ActivePlugin { get; private set; } = Daedalus.Config.NavmeshPluginChoice.VnavmeshName;

    public VNavService(IDalamudPluginInterface pluginInterface, IPluginLog? log = null,
        Func<Daedalus.Config.NavmeshPlugin>? choice = null)
    {
        _pluginInterface = pluginInterface;
        _log = log;
        _choice = choice ?? (() => Daedalus.Config.NavmeshPlugin.Vnavmesh);
    }

    /// <summary>The plugin to drive this call, by the setting.</summary>
    private string Mover
    {
        get
        {
            var name = Daedalus.Config.NavmeshPluginChoice.Resolve(_choice(), AriadneLoaded);
            ActivePlugin = name;
            return name;
        }
    }

    // "Is Ariadne loaded" walks the installed-plugin list; movement asks several times a frame, and
    // a plugin loading or unloading is not a per-frame event.
    private bool _ariadneLoaded;
    private long _ariadneCheckedAt = long.MinValue;

    private bool AriadneLoaded
    {
        get
        {
            var now = Environment.TickCount64;
            if (now - _ariadneCheckedAt >= 1000)
            {
                _ariadneLoaded = IsPluginLoaded(Daedalus.Config.NavmeshPluginChoice.AriadneName);
                _ariadneCheckedAt = now;
            }
            return _ariadneLoaded;
        }
    }

    private Gates GatesFor(string plugin)
    {
        if (!_gates.TryGetValue(plugin, out var g))
        {
            var p = plugin + ".";
            g = new Gates
            {
                NavIsReady = _pluginInterface.GetIpcSubscriber<bool>(p + "Nav.IsReady"),
                PathIsRunning = _pluginInterface.GetIpcSubscriber<bool>(p + "Path.IsRunning"),
                PathfindInProgress = _pluginInterface.GetIpcSubscriber<bool>(p + "SimpleMove.PathfindInProgress"),
                PathfindAndMoveTo = _pluginInterface.GetIpcSubscriber<Vector3, bool, bool>(p + "SimpleMove.PathfindAndMoveTo"),
                PathfindAndMoveCloseTo = _pluginInterface.GetIpcSubscriber<Vector3, bool, float, bool>(p + "SimpleMove.PathfindAndMoveCloseTo"),
                PathStop = _pluginInterface.GetIpcSubscriber<object>(p + "Path.Stop"),
            };
            _gates[plugin] = g;
        }
        return g;
    }

    private Gates Current => GatesFor(Mover);

    public bool IsAvailable => IsPluginLoaded(Mover);

    public bool IsNavReady => TryInvoke(() => Current.NavIsReady?.InvokeFunc() ?? false);

    public bool IsPathRunning => TryInvoke(() => Current.PathIsRunning?.InvokeFunc() ?? false);

    public bool IsPathfindInProgress => TryInvoke(() => Current.PathfindInProgress?.InvokeFunc() ?? false);

    public VNavMoveResult PathfindAndMoveTo(Vector3 destination, bool fly = false)
    {
        if (!IsAvailable)
            return VNavMoveResult.PluginUnavailable;

        if (!IsNavReady)
            return VNavMoveResult.NavmeshNotReady;

        _lastMover = Mover;

        try
        {
            return Current.PathfindAndMoveTo?.InvokeFunc(destination, fly) == true
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

        _lastMover = Mover;

        try
        {
            return Current.PathfindAndMoveCloseTo?.InvokeFunc(destination, fly, toleranceYalms) == true
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
        // The plugin that started the walk, if the setting changed since — then the current one.
        if (_lastMover is { } last && last != Mover && IsPluginLoaded(last))
            TryInvoke(() => GatesFor(last).PathStop?.InvokeAction());

        if (!IsAvailable)
            return;

        TryInvoke(() => Current.PathStop?.InvokeAction());
    }

    public Vector3 SnapToFloor(Vector3 position)
    {
        if (!IsFloorQueryAvailable)
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

    public bool TryGetFloorPoint(Vector3 position, out Vector3 floor)
    {
        floor = position;
        if (!IsFloorQueryAvailable)
            return false;

        EnsureSubscribers();

        try
        {
            var snapped = _queryPointOnFloor?.InvokeFunc(position, false, DefaultFloorQueryHalfExtent);
            if (snapped is not { } value)
                return false;

            floor = value;
            return true;
        }
        catch (Exception ex)
        {
            _log?.Debug(ex, "[VNavService] TryGetFloorPoint failed.");
            return false;
        }
    }

    /// <summary>The <c>vnavmesh.*</c> floor query has an answerer: vnavmesh, or Ariadne's compatibility gates.</summary>
    private bool IsFloorQueryAvailable =>
        IsPluginLoaded(Daedalus.Config.NavmeshPluginChoice.VnavmeshName)
        || IsPluginLoaded(Daedalus.Config.NavmeshPluginChoice.AriadneName);

    private void EnsureSubscribers()
    {
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
