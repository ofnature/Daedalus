using System;
using System.Diagnostics;
using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace Daedalus.Ipc;

/// <summary>
/// Henchman bridge (BumpOnALog hunt-log grinding, OnYourMark hunt bills, BringYour[A/B]Game rank
/// farming). Henchman picks its rotation plugin by installed-plugin internal name ("RotationSolver"),
/// so the RSR-compat gates alone can't hook it — its rotation enable silently no-ops and the kill
/// loop would wait forever for the mark to die. Instead we poll Henchman's own <c>Henchman.IsBusy</c>
/// gate: while a Henchman task runs, the external-combat override keeps the rotation on. Henchman
/// does everything else itself — targets each mark (ITargetManager), moves to it via vnavmesh, and
/// waits for it to die.
/// </summary>
/// <remarks>
/// Fail-open to idle: Henchman missing or IPC errors read as not busy. The override is only cleared
/// on a busy→idle edge, never re-cleared while idle, so this poll can't stomp an override another
/// plugin (Questionable via <see cref="RsrCompatIpc"/>) set. While busy the override is re-asserted
/// every poll, so a Questionable fight-end Off mid-task can't strand a Henchman grind.
/// </remarks>
public sealed class HenchmanIpc : IDisposable
{
    private const string PluginInternalName = "Henchman";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly Configuration _configuration;
    private readonly IPluginLog _log;
    private readonly HenchmanOverrideTracker _tracker = new();
    private readonly Stopwatch _pollClock = Stopwatch.StartNew();

    private ICallGateSubscriber<bool>? _isBusy;

    /// <summary>Fired when this bridge flips the external-combat override.</summary>
    public event Action<bool>? OverrideChanged;

    public HenchmanIpc(IDalamudPluginInterface pluginInterface, Configuration configuration, IPluginLog log)
    {
        _pluginInterface = pluginInterface;
        _configuration = configuration;
        _log = log;
    }

    /// <summary>Framework-thread poll; internally throttled to once per second.</summary>
    public void Update()
    {
        if (_pollClock.Elapsed < PollInterval)
            return;
        _pollClock.Restart();

        switch (_tracker.Observe(ReadIsBusy()))
        {
            case HenchmanOverrideTracker.OverrideAction.Assert:
                if (!_configuration.ExternalCombatOverride)
                {
                    _configuration.ExternalCombatOverride = true;
                    _log.Info("Henchman task running — external-combat override on.");
                    OverrideChanged?.Invoke(true);
                }
                break;

            case HenchmanOverrideTracker.OverrideAction.Clear:
                if (_configuration.ExternalCombatOverride)
                {
                    _configuration.ExternalCombatOverride = false;
                    _log.Info("Henchman task finished — external-combat override off.");
                    OverrideChanged?.Invoke(false);
                }
                break;
        }
    }

    private bool ReadIsBusy()
    {
        if (!IsHenchmanLoaded())
            return false;

        try
        {
            _isBusy ??= _pluginInterface.GetIpcSubscriber<bool>("Henchman.IsBusy");
            return _isBusy.InvokeFunc();
        }
        catch
        {
            return false;
        }
    }

    private bool IsHenchmanLoaded()
    {
        try
        {
            return _pluginInterface.InstalledPlugins.Any(p =>
                p.InternalName.Equals(PluginInternalName, StringComparison.OrdinalIgnoreCase) && p.IsLoaded);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        // Only release the override if this bridge is the one currently holding it.
        if (_tracker.Observe(false) == HenchmanOverrideTracker.OverrideAction.Clear)
            _configuration.ExternalCombatOverride = false;
    }
}

/// <summary>
/// Pure busy-signal edge logic for the Henchman bridge: assert while busy (level-triggered, so a
/// cleared override is re-asserted mid-task), clear only on the busy→idle edge (so an idle Henchman
/// never stomps an override another plugin owns).
/// </summary>
public sealed class HenchmanOverrideTracker
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
