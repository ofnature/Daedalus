using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Daedalus.Services.Network;

namespace Daedalus.Windows;

/// <summary>
/// LAN party coordination window — visible while the LAN coordinator is enabled. Shows every toon
/// (grouped per machine, local first) from heartbeat data: online dot, name (or scrambled alias),
/// job, HP bar, assigned role slot; status bar with connection state / peer count / port / latency.
/// </summary>
public sealed class LanPartyWindow : Window
{
    private static readonly Vector4 Green = new(0.3f, 0.9f, 0.3f, 1f);
    private static readonly Vector4 Grey = new(0.5f, 0.5f, 0.5f, 1f);
    private static readonly Vector4 Red = new(1f, 0.35f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.62f, 0.62f, 0.62f, 1f);
    private static readonly Vector4 HpColor = new(0.25f, 0.75f, 0.35f, 1f);

    // Mythological aliases for the scramble toggle — assigned first-seen, stable per session.
    private static readonly string[] AliasPool =
    [
        "Eos", "Iris", "Dike", "Selene", "Helios", "Nyx", "Rhea", "Metis",
        "Leto", "Eris", "Gaia", "Thea", "Crius", "Ceto", "Pallas", "Doris",
        "Maia", "Clio", "Erato", "Thalia", "Urania", "Calliope", "Astraea", "Hemera",
    ];

    private readonly CoordinationBus _bus;
    private readonly LanCoordinator _lan;
    private readonly Configuration _config;
    private readonly Action _save;
    private readonly Dictionary<string, string> _aliases = new();

    public LanPartyWindow(CoordinationBus bus, LanCoordinator lan, Configuration config, Action save)
        : base("Daedalus — Party Coordination")
    {
        _bus = bus;
        _lan = lan;
        _config = config;
        _save = save;
        Size = new Vector2(420, 380);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override bool DrawConditions() => _config.PartyCoordination.LanCoordinatorEnabled;

    public override void Draw()
    {
        DrawStatusBar();
        ImGui.Separator();

        var cfg = _config.PartyCoordination;
        var now = DateTime.UtcNow;
        var roster = _bus.Roster
            .OrderBy(p => p.MachineId == _bus.LocalMachineId ? 0 : 1)
            .ThenBy(p => p.MachineId, StringComparer.Ordinal)
            .ThenBy(p => p.AssignedSlot.Length == 0 ? p.SenderId : p.AssignedSlot, StringComparer.Ordinal)
            .ToArray();

        if (roster.Length == 0)
        {
            ImGui.TextColored(Dim, "No heartbeats yet — waiting for toons to report in.");
        }
        else
        {
            string? currentMachine = null;
            var machineIndex = 0;
            foreach (var peer in roster)
            {
                var isLocal = peer.MachineId == _bus.LocalMachineId;
                if (isLocal && cfg.LanShowRemoteOnly)
                    continue;

                if (peer.MachineId != currentMachine)
                {
                    currentMachine = peer.MachineId;
                    machineIndex++;
                    ImGui.Spacing();
                    ImGui.TextColored(Dim, $"Machine {machineIndex} ({(isLocal ? "Local" : "Remote")})");
                }

                DrawToonRow(peer, now, cfg.LanCompactMode, cfg.LanShowHpBars, cfg.LanScrambleNames);
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        DrawToggles();
    }

    private void DrawStatusBar()
    {
        var (statusText, statusColor) = _lan.Status switch
        {
            LanStatus.Connected => ("Connected", Green),
            LanStatus.NoPeers => ("No peers", Dim),
            LanStatus.Error => (_lan.LastError.Length > 0 ? _lan.LastError : "Error", Red),
            _ => ("Disabled", Grey),
        };

        ImGui.Text("Status:");
        ImGui.SameLine();
        ImGui.TextColored(statusColor, statusText);
        ImGui.SameLine();
        ImGui.TextColored(Dim, $"  Peers: {_bus.PeerToonCount}  Port: {_config.PartyCoordination.LanPort}");

        var latency = _bus.RemoteLatencyMs;
        if (latency > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(latency < 50 ? Green : Dim, $"  {latency:F0} ms");
        }
    }

    private void DrawToonRow(LanPeerInfo peer, DateTime now, bool compact, bool showHp, bool scramble)
    {
        var stale = peer.IsStale(now);
        ImGui.TextColored(stale ? Grey : Green, "●");
        ImGui.SameLine();

        var name = scramble ? AliasFor(peer.SenderId) : peer.CharacterName;
        if (name.Length == 0) name = scramble ? AliasFor(peer.SenderId) : peer.SenderId;
        ImGui.Text($"{name,-18}");
        ImGui.SameLine();
        ImGui.TextColored(Dim, peer.JobAbbrev.Length > 0 ? peer.JobAbbrev : "???");

        if (showHp && !compact)
        {
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, HpColor);
            ImGui.ProgressBar(Math.Clamp(peer.HpPercent, 0f, 1f), new Vector2(120, 14), $"{peer.HpPercent:P0}");
            ImGui.PopStyleColor();
        }

        if (peer.AssignedSlot.Length > 0 || peer.Role.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Dim, peer.AssignedSlot.Length > 0 ? peer.AssignedSlot : peer.Role);
        }
    }

    private void DrawToggles()
    {
        var cfg = _config.PartyCoordination;
        var changed = false;

        var scramble = cfg.LanScrambleNames;
        if (ImGui.Checkbox("Scramble Names", ref scramble)) { cfg.LanScrambleNames = scramble; changed = true; }
        ImGui.SameLine();

        var hp = cfg.LanShowHpBars;
        if (ImGui.Checkbox("HP Bars", ref hp)) { cfg.LanShowHpBars = hp; changed = true; }
        ImGui.SameLine();

        var remoteOnly = cfg.LanShowRemoteOnly;
        if (ImGui.Checkbox("Remote Only", ref remoteOnly)) { cfg.LanShowRemoteOnly = remoteOnly; changed = true; }
        ImGui.SameLine();

        var compactMode = cfg.LanCompactMode;
        if (ImGui.Checkbox("Compact", ref compactMode)) { cfg.LanCompactMode = compactMode; changed = true; }
        ImGui.SameLine();

        if (ImGui.Button("Copy Party Data"))
            ImGui.SetClipboardText(BuildPartyDataText());

        if (changed) _save();
    }

    /// <summary>Plain-text roster for sharing/debugging. Respects the scramble toggle.</summary>
    private string BuildPartyDataText()
    {
        var cfg = _config.PartyCoordination;
        var now = DateTime.UtcNow;
        var sb = new StringBuilder();
        sb.AppendLine($"Daedalus LAN party — {_lan.Status}, {_bus.PeerToonCount} remote toons, port {cfg.LanPort}");
        foreach (var peer in _bus.Roster.OrderBy(p => p.MachineId, StringComparer.Ordinal))
        {
            var name = cfg.LanScrambleNames ? AliasFor(peer.SenderId) : peer.CharacterName;
            var local = peer.MachineId == _bus.LocalMachineId ? "local" : "remote";
            sb.AppendLine($"{name} | {peer.JobAbbrev} | {peer.HpPercent:P0} | {peer.AssignedSlot} | {local} | {(peer.IsStale(now) ? "stale" : "online")}");
        }
        return sb.ToString();
    }

    /// <summary>Session-consistent alias: same toon always gets the same mythological name.</summary>
    private string AliasFor(string senderId)
    {
        if (_aliases.TryGetValue(senderId, out var alias))
            return alias;

        alias = AliasPool[_aliases.Count % AliasPool.Length];
        if (_aliases.Count >= AliasPool.Length)
            alias += $" {_aliases.Count / AliasPool.Length + 1}"; // pool exhausted — suffix

        _aliases[senderId] = alias;
        return alias;
    }
}
