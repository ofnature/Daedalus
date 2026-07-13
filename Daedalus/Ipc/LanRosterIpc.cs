using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Daedalus.Services.Network;

namespace Daedalus.Ipc;

/// <summary>
/// Read-only LAN roster IPC for companion plugins (Charon consumes both endpoints).
/// </summary>
/// <remarks>
/// Available IPC endpoints:
/// - Daedalus.Party.GetRosterJson: JSON array of {"name","world","machine","online"} per LAN toon
/// - Daedalus.Party.GetTrustListJson: JSON array of trusted character names (currently the roster)
/// Registered even while the LAN coordinator is disabled — both return "[]" then, so consumers can
/// distinguish "Daedalus loaded, no roster" from "Daedalus absent" (call-gate failure).
/// </remarks>
public sealed class LanRosterIpc : IDisposable
{
    private readonly Func<CoordinationBus?> _getBus;
    private readonly IPluginLog _log;

    private readonly ICallGateProvider<string> _getRosterJson;
    private readonly ICallGateProvider<string> _getTrustListJson;

    // Extend-only schema: Charon ignores unknown fields, so new fields may be ADDED freely but
    // existing ones must never be renamed or removed (see .cursor/rules/charon-lan-integration.md).
    private sealed record RosterEntryDto(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("world")] string World,
        [property: JsonPropertyName("machine")] string Machine,
        [property: JsonPropertyName("online")] bool Online,
        [property: JsonPropertyName("hp")] float Hp,
        [property: JsonPropertyName("entityId")] uint EntityId,
        // Invite addressing (Charon's single/mass invite — the native InviteToParty call
        // wants content id + home world id; 0 on toons whose heartbeat predates the fields).
        [property: JsonPropertyName("contentId")] ulong ContentId,
        [property: JsonPropertyName("worldId")] ushort WorldId);

    public LanRosterIpc(IDalamudPluginInterface pluginInterface, Func<CoordinationBus?> getBus, IPluginLog log)
    {
        _getBus = getBus;
        _log = log;

        _getRosterJson = pluginInterface.GetIpcProvider<string>("Daedalus.Party.GetRosterJson");
        _getRosterJson.RegisterFunc(GetRosterJson);

        _getTrustListJson = pluginInterface.GetIpcProvider<string>("Daedalus.Party.GetTrustListJson");
        _getTrustListJson.RegisterFunc(GetTrustListJson);

        _log.Info("LAN roster IPC initialized (Daedalus.Party.GetRosterJson / GetTrustListJson)");
    }

    private string GetRosterJson()
    {
        try
        {
            return JsonSerializer.Serialize(BuildRoster());
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "GetRosterJson failed — returning empty roster");
            return "[]";
        }
    }

    private string GetTrustListJson()
    {
        try
        {
            // v1 trust list = every rostered toon (they are all our own boxes). A curated list can
            // replace this later without changing the endpoint shape.
            return JsonSerializer.Serialize(BuildRoster().Select(e => e.Name).ToList());
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "GetTrustListJson failed — returning empty list");
            return "[]";
        }
    }

    /// <summary>Roster in the LAN window's display order: local machine first, then by machine/slot.</summary>
    private List<RosterEntryDto> BuildRoster()
    {
        var bus = _getBus();
        if (bus == null)
            return new List<RosterEntryDto>();

        var now = DateTime.UtcNow;
        return bus.Roster
            .OrderBy(p => p.MachineId == bus.LocalMachineId ? 0 : 1)
            .ThenBy(p => p.MachineId, StringComparer.Ordinal)
            .ThenBy(p => p.AssignedSlot.Length == 0 ? p.SenderId : p.AssignedSlot, StringComparer.Ordinal)
            .Where(p => p.CharacterName.Length > 0)
            .Select(p => new RosterEntryDto(
                p.CharacterName,
                WorldOf(p.SenderId),
                p.MachineId,
                !p.IsStale(now),
                // Heartbeat-stale by ~1-2s — Charon's Heal Watch re-checks live HP via its own
                // object table before casting; the roster value is detection only.
                p.HpPercent,
                p.PlayerEntityId,
                p.ContentId,
                p.HomeWorldId))
            .ToList();
    }

    /// <summary>The @World half of a "Name@World" sender id ("" when absent — consumers match name-only).</summary>
    internal static string WorldOf(string senderId)
    {
        var at = senderId.LastIndexOf('@');
        return at >= 0 && at < senderId.Length - 1 ? senderId[(at + 1)..] : "";
    }

    public void Dispose()
    {
        _getRosterJson.UnregisterFunc();
        _getTrustListJson.UnregisterFunc();
        _log.Info("LAN roster IPC disposed");
    }
}
