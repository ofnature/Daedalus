using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Daedalus.Services.Network;
using Daedalus.Windows.Common;

namespace Daedalus.Windows;

/// <summary>
/// LAN party coordination window — visible while the LAN coordinator is enabled. Shows every toon
/// (grouped per machine, local first) from heartbeat data: online dot, name (or scrambled alias),
/// job, HP bar, assigned role slot; status bar with connection state / peer count / port / latency.
/// </summary>
public sealed class LanPartyWindow : Window
{
    private static readonly Vector4 Green = DaedalusTheme.StatusGreen;
    private static readonly Vector4 Yellow = DaedalusTheme.StatusYellow;
    private static readonly Vector4 Grey = DaedalusTheme.StatusGrey;
    private static readonly Vector4 Red = DaedalusTheme.StatusRed;
    private static readonly Vector4 Dim = DaedalusTheme.TextSecondary;
    private static readonly Vector4 HpColor = DaedalusTheme.StatusGreen;

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
                    // Machine id is the hostname since the per-PC identity fix — show it.
                    ImGui.TextColored(isLocal ? DaedalusTheme.AccentGold : DaedalusTheme.AccentDim,
                        $"■ {peer.MachineId}");
                    ImGui.SameLine();
                    ImGui.TextColored(DaedalusTheme.TextDisabled, isLocal ? "(Local)" : "(Remote)");
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
        var (state, stateColor, diagnosis) = SyncStateFor(peer, now);
        DrawRoleSyncIcon(peer.Role, state, stateColor);
        ImGui.SameLine();

        var name = scramble ? AliasFor(peer.SenderId) : peer.CharacterName;
        if (name.Length == 0) name = scramble ? AliasFor(peer.SenderId) : peer.SenderId;
        ImGui.Text($"{name,-18}");
        ImGui.SameLine();
        ImGui.TextColored(Dim, peer.JobAbbrev.Length > 0 ? peer.JobAbbrev : "???");

        if (showHp && !compact)
        {
            ImGui.SameLine();
            // Stale data renders a grey bar — the number is a memory, not a reading.
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, state == SyncState.Synced ? HpColor : Grey);
            ImGui.ProgressBar(Math.Clamp(peer.HpPercent, 0f, 1f), new Vector2(110, 14), $"{peer.HpPercent:P0}");
            ImGui.PopStyleColor();
        }

        if (peer.AssignedSlot.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Dim, peer.AssignedSlot);
        }

        ImGui.SameLine();
        ImGui.TextColored(state == SyncState.Synced ? DaedalusTheme.TextDisabled : stateColor, diagnosis);
    }

    private enum SyncState { NoData, Synced, SyncIssue, ConnectionLost }

    /// <summary>
    /// Heartbeat freshness → sync state. Thresholds mirror the bus: fresh under 5s, stale 5-15s,
    /// beyond that the peer is effectively gone (the roster drops it entirely at 30s).
    /// </summary>
    private static (SyncState state, Vector4 color, string diagnosis) SyncStateFor(LanPeerInfo peer, DateTime now)
    {
        if (peer.Role.Length == 0 && peer.JobAbbrev.Length == 0)
            return (SyncState.NoData, Grey, "negotiating…");

        var age = (now - peer.LastSeenUtc).TotalSeconds;
        if (age <= 5)
            return (SyncState.Synced, Green, "synced");
        if (age <= 15)
            return (SyncState.SyncIssue, Yellow, $"hb {age:F1}s late");
        return (SyncState.ConnectionLost, Red, $"lost {age:F0}s ago");
    }

    /// <summary>
    /// Role glyph (T/H/D) doubling as sync health: filled chip in the state color when we have
    /// data, hollow grey outline while negotiating. Legend lives in the (?) tooltip below.
    /// </summary>
    private static void DrawRoleSyncIcon(string role, SyncState state, Vector4 color)
    {
        var glyph = role.Contains("Tank", StringComparison.OrdinalIgnoreCase) ? "T"
                  : role.Contains("Heal", StringComparison.OrdinalIgnoreCase) ? "H"
                  : role.Length > 0 ? "D" : "?";

        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var size = new Vector2(18, 18);
        var rectMin = pos;
        var rectMax = pos + size;

        if (state == SyncState.NoData)
        {
            dl.AddRect(rectMin, rectMax, ImGui.ColorConvertFloat4ToU32(Grey), 4f);
            var half = ImGui.CalcTextSize(glyph) / 2f;
            dl.AddText(pos + size / 2f - half, ImGui.ColorConvertFloat4ToU32(Grey), glyph);
        }
        else
        {
            dl.AddRectFilled(rectMin, rectMax, ImGui.ColorConvertFloat4ToU32(color), 4f);
            var half = ImGui.CalcTextSize(glyph) / 2f;
            dl.AddText(pos + size / 2f - half, ImGui.ColorConvertFloat4ToU32(DaedalusTheme.BgDeep), glyph);
        }

        ImGui.Dummy(size);
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

        ImGui.SameLine();
        DaedalusTheme.HelpMarker(
            "Role icon fill = sync state\n" +
            "green — synced (heartbeat fresh)\n" +
            "yellow — sync issue (heartbeat stale)\n" +
            "red — connection lost\n" +
            "hollow — no data yet");

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
