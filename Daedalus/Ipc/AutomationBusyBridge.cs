using System;
using System.Diagnostics;
using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace Daedalus.Ipc;

/// <summary>
/// Generic automation-plugin bridge: polls a bool IPC gate on another plugin and holds the
/// external-combat override while that plugin reports a running task. Instantiated per source:
/// <list type="bullet">
/// <item>Henchman — <c>Henchman.IsBusy</c> == true while a task runs (BumpOnALog hunt logs,
/// OnYourMark hunt bills, BringYour[A/B]Game rank farming). Henchman picks its rotation plugin by
/// installed internal name ("RotationSolver"), so the RSR-compat gates can't hook it; without a
/// rotation its kill loop waits forever for the mark to die.</item>
/// <item>AutoDuty — <c>AutoDuty.IsStopped</c> == false while a duty run is active. Covers both
/// standalone AutoDuty farming and Henchman's duty hunt-log marks, which Henchman delegates to
/// AutoDuty (solo-unsync or duty support) and merely waits on — inside the dungeon AutoDuty is
/// the driver, and it too selects its rotation plugin by internal name.</item>
/// </list>
/// </summary>
/// <remarks>
/// Fail-open to idle: plugin missing or IPC errors read as not busy. The override is only cleared
/// on a busy→idle edge, never re-cleared while idle, so one bridge can't stomp an override another
/// source (or Questionable via <see cref="RsrCompatIpc"/>) set. While busy the override is
/// re-asserted every poll, so a Questionable fight-end Off or another bridge's clear mid-task
/// can't strand a run.
/// </remarks>
public sealed class AutomationBusyBridge : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly Configuration _configuration;
    private readonly IPluginLog _log;
    private readonly string _pluginInternalName;
    private readonly string _gateName;
    private readonly bool _busyGateValue;
    private readonly AutomationOverrideTracker _tracker = new();
    private readonly Stopwatch _pollClock = Stopwatch.StartNew();

    private ICallGateSubscriber<bool>? _gate;
    private bool? _lastLoggedBusy;

    /// <summary>Fired when this bridge flips the external-combat override.</summary>
    public event Action<bool>? OverrideChanged;

    /// <param name="pluginInternalName">Dalamud internal name of the automation plugin (also used in log/chat text).</param>
    /// <param name="gateName">Bool IPC gate to poll (e.g. "Henchman.IsBusy", "AutoDuty.IsStopped").</param>
    /// <param name="busyGateValue">The gate value that means "task running" (true for IsBusy, false for IsStopped).</param>
    public AutomationBusyBridge(
        IDalamudPluginInterface pluginInterface,
        Configuration configuration,
        IPluginLog log,
        string pluginInternalName,
        string gateName,
        bool busyGateValue)
    {
        _pluginInterface = pluginInterface;
        _configuration = configuration;
        _log = log;
        _pluginInternalName = pluginInternalName;
        _gateName = gateName;
        _busyGateValue = busyGateValue;
    }

    public string PluginName => _pluginInternalName;

    /// <summary>Framework-thread poll; internally throttled to once per second.</summary>
    public void Update()
    {
        if (_pollClock.Elapsed < PollInterval)
            return;
        _pollClock.Restart();

        var busy = ReadIsBusy();
        if (busy != _lastLoggedBusy)
        {
            _log.Debug("[AutomationBridge:{0}] {1} -> busy={2}", _pluginInternalName, _gateName, busy);
            _lastLoggedBusy = busy;
        }

        switch (_tracker.Observe(busy))
        {
            case AutomationOverrideTracker.OverrideAction.Assert:
                if (!_configuration.ExternalCombatOverride)
                {
                    _configuration.ExternalCombatOverride = true;
                    ExternalCombatOverrideState.Source = _pluginInternalName;
                    _log.Info("{0} task running — external-combat override on.", _pluginInternalName);
                    OverrideChanged?.Invoke(true);
                }
                break;

            case AutomationOverrideTracker.OverrideAction.Clear:
                if (_configuration.ExternalCombatOverride)
                {
                    _configuration.ExternalCombatOverride = false;
                    ExternalCombatOverrideState.Source = "";
                    _log.Info("{0} task finished — external-combat override off.", _pluginInternalName);
                    OverrideChanged?.Invoke(false);
                }
                break;
        }
    }

    private bool ReadIsBusy()
    {
        if (!IsPluginLoaded())
            return false;

        try
        {
            _gate ??= _pluginInterface.GetIpcSubscriber<bool>(_gateName);
            return _gate.InvokeFunc() == _busyGateValue;
        }
        catch (Exception ex)
        {
            // Gate missing/not ready (old plugin version, plugin mid-load) — read as idle.
            if (_lastLoggedBusy != false)
                _log.Debug("[AutomationBridge:{0}] {1} unavailable ({2}) — reading idle.", _pluginInternalName, _gateName, ex.GetType().Name);
            return false;
        }
    }

    private bool IsPluginLoaded()
    {
        try
        {
            return _pluginInterface.InstalledPlugins.Any(p =>
                p.InternalName.Equals(_pluginInternalName, StringComparison.OrdinalIgnoreCase) && p.IsLoaded);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        // Only release the override if this bridge is the one currently holding it.
        if (_tracker.Observe(false) == AutomationOverrideTracker.OverrideAction.Clear)
        {
            _configuration.ExternalCombatOverride = false;
            ExternalCombatOverrideState.Source = "";
        }
    }
}

/// <summary>
/// Pure busy-signal edge logic for automation bridges: assert while busy (level-triggered, so a
/// cleared override is re-asserted mid-task), clear only on the busy→idle edge (so an idle source
/// never stomps an override another plugin owns).
/// </summary>
public sealed class AutomationOverrideTracker
{
    public enum OverrideAction
    {
        None,
        Assert,
        Clear,
    }

    private bool _wasBusy;

    public OverrideAction Observe(bool busy)
    {
        var wasBusy = _wasBusy;
        _wasBusy = busy;

        if (busy)
            return OverrideAction.Assert;

        return wasBusy ? OverrideAction.Clear : OverrideAction.None;
    }
}
