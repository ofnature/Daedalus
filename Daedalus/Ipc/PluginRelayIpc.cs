using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Daedalus.Services.Network;

namespace Daedalus.Ipc;

/// <summary>
/// Generic plugin relay over the LAN transport, for companion plugins (Charon ROADMAP #7).
/// Dalamud IPC is per-process, so a companion plugin cannot reach its siblings on other game
/// clients — this ferries opaque {channel, json} messages through the CoordinationBus UDP
/// broadcast, which covers both cross-machine peers AND same-machine sibling clients (loopback).
/// </summary>
/// <remarks>
/// IPC surface (contract: .cursor/rules/charon-lan-integration.md — extend-only):
/// - Daedalus.Relay.Publish (Action&lt;string channel, string json&gt;): broadcast to every other
///   toon's client. The publisher's own client never receives its own frame.
/// - Daedalus.Relay.Message (event, &lt;string channel, string json&gt;): fired on the framework
///   thread for every received relay message.
/// Registered even while the LAN coordinator is disabled — Publish is then a silent no-op, so
/// consumers can subscribe unconditionally and treat call-gate failure as "Daedalus absent".
/// </remarks>
public sealed class PluginRelayIpc : IDisposable
{
    private readonly Func<CoordinationBus?> _getBus;
    private readonly IPluginLog _log;

    private readonly ICallGateProvider<string, string, object?> _publish;
    private readonly ICallGateProvider<string, string, object?> _message;

    private CoordinationBus? _subscribedBus;

    public PluginRelayIpc(IDalamudPluginInterface pluginInterface, Func<CoordinationBus?> getBus, IPluginLog log)
    {
        _getBus = getBus;
        _log = log;

        _publish = pluginInterface.GetIpcProvider<string, string, object?>("Daedalus.Relay.Publish");
        _publish.RegisterAction(Publish);

        _message = pluginInterface.GetIpcProvider<string, string, object?>("Daedalus.Relay.Message");

        _log.Info("Plugin relay IPC initialized (Daedalus.Relay.Publish / Daedalus.Relay.Message)");
    }

    /// <summary>
    /// Late-binds the bus event (the bus is created after IPC when LAN is enabled). Call once the
    /// bus exists; safe to call repeatedly.
    /// </summary>
    public void WireBusEvents()
    {
        var bus = _getBus();
        if (bus == null || ReferenceEquals(bus, _subscribedBus)) return;

        bus.OnPluginRelay += OnRelayReceived;
        _subscribedBus = bus;
    }

    private void Publish(string channel, string json)
    {
        try
        {
            _getBus()?.PublishPluginRelay(channel, json);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, $"Relay publish failed (channel '{channel}')");
        }
    }

    private void OnRelayReceived(string sender, string channel, string json)
    {
        try
        {
            _message.SendMessage(channel, json);
        }
        catch (Exception ex)
        {
            // A subscriber threw — never let a companion plugin break the bus pump.
            _log.Warning(ex, $"Relay message delivery failed (channel '{channel}' from {sender})");
        }
    }

    public void Dispose()
    {
        if (_subscribedBus != null)
            _subscribedBus.OnPluginRelay -= OnRelayReceived;
        _publish.UnregisterAction();
        _log.Info("Plugin relay IPC disposed");
    }
}
