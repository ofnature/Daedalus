using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Daedalus.Services.Network;
using Daedalus.Services.Targeting;
using Daedalus.Windows.Common;

namespace Daedalus.Windows;

/// <summary>
/// LAN party coordination window — visible while the LAN coordinator is enabled. Shows every toon
/// (grouped per machine, local first) from heartbeat data: online dot, name (or scrambled alias),
/// job, HP bar, assigned role slot; status bar with connection state / peer count / port / latency.
/// </summary>
public sealed class LanPartyWindow : Window, IDisposable
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
    private readonly IObjectTable _objectTable;
    private readonly ITargetManager _targetManager;
    private readonly Dictionary<string, string> _aliases = new();

    /// <summary>One coordination event surfaced in the alert feed.</summary>
    private readonly record struct AlertEntry(DateTime Time, string Text, Vector4 Color);

    private readonly List<AlertEntry> _alerts = new();
    private const int MaxAlerts = 6;

    // Majority enemy target among in-combat DPS this frame — drives the per-row "off" marker.
    private ulong _majorityTargetId;

    public LanPartyWindow(CoordinationBus bus, LanCoordinator lan, Configuration config, Action save,
        IObjectTable objectTable, ITargetManager targetManager)
        : base("Daedalus — Party Coordination")
    {
        _bus = bus;
        _lan = lan;
        _config = config;
        _save = save;
        _objectTable = objectTable;
        _targetManager = targetManager;
        Size = new Vector2(420, 380);
        SizeCondition = ImGuiCond.FirstUseEver;

        // Surface the coordination signals the bus already raises but nothing displayed.
        _bus.OnHealerDown += OnHealerDownAlert;
        _bus.OnPhoenixDown += OnPhoenixDownAlert;
        _bus.OnTankSwap += OnTankSwapAlert;
        _bus.OnAddSpawn += OnAddSpawnAlert;
    }

    public void Dispose()
    {
        _bus.OnHealerDown -= OnHealerDownAlert;
        _bus.OnPhoenixDown -= OnPhoenixDownAlert;
        _bus.OnTankSwap -= OnTankSwapAlert;
        _bus.OnAddSpawn -= OnAddSpawnAlert;
    }

    private void PushAlert(string text, Vector4 color)
    {
        _alerts.Insert(0, new AlertEntry(DateTime.UtcNow, text, color));
        if (_alerts.Count > MaxAlerts)
            _alerts.RemoveRange(MaxAlerts, _alerts.Count - MaxAlerts);
    }

    // Bus events fire from CoordinationBus.Update on the framework thread — same thread as Draw, so
    // mutating the alert list here needs no synchronization.
    private void OnHealerDownAlert(string sender, LanMessage msg)
        => PushAlert(msg.Payload.Length > 0 ? $"all healers down: {msg.Payload}" : "all healers down", Red);

    private void OnPhoenixDownAlert(string sender, string targetName)
        => PushAlert($"raise → {targetName}", DaedalusTheme.AccentGold);

    private void OnTankSwapAlert(string sender, LanMessage msg) => PushAlert("tank swap", Yellow);

    private void OnAddSpawnAlert(string sender, LanMessage msg) => PushAlert("add spawn", Yellow);

    public override bool DrawConditions() => _config.PartyCoordination.LanCoordinatorEnabled;

    public override void Draw()
    {
        // Rounded frames/bars/grabs for the whole window — closes most of the gap to the mockup.
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 4f);
        try
        {
            DrawBody();
        }
        finally
        {
            ImGui.PopStyleVar(3);
        }
    }

    private void DrawBody()
    {
        DrawStatusBar();
        ImGui.Separator();

        DrawTargetModeControls();
        ImGui.Separator();

        var cfg = _config.PartyCoordination;
        var now = DateTime.UtcNow;
        var burstReady = new HashSet<string>(_bus.BurstReadySenders, StringComparer.Ordinal);
        var roster = _bus.Roster
            .OrderBy(p => p.MachineId == _bus.LocalMachineId ? 0 : 1)
            .ThenBy(p => p.MachineId, StringComparer.Ordinal)
            .ThenBy(p => p.AssignedSlot.Length == 0 ? p.SenderId : p.AssignedSlot, StringComparer.Ordinal)
            .ToArray();

        var (majorityTarget, distinctTargets, eligibleDps) = ComputeTargetAgreement(roster, now);
        _majorityTargetId = majorityTarget;

        if (roster.Length == 0)
            ImGui.TextColored(Dim, "No heartbeats yet — waiting for toons to report in.");
        else
            DrawRoster(roster, now, cfg, burstReady);

        DrawAgreement(distinctTargets, eligibleDps);
        DrawBurstStrip(burstReady.Count);
        DrawAlerts(now);

        ImGui.Spacing();
        ImGui.Separator();
        DrawToggles();
    }

    private static bool IsDpsRole(string role) =>
        role.Length > 0
        && !role.Contains("Tank", StringComparison.OrdinalIgnoreCase)
        && !role.Contains("Heal", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Majority enemy target among fresh, in-combat DPS toons, plus how many distinct enemies the DPS
    /// are spread across. Drives the focused/split summary and the per-row off-target marker.
    /// </summary>
    private (ulong majority, int distinct, int eligible) ComputeTargetAgreement(LanPeerInfo[] roster, DateTime now)
    {
        var counts = new Dictionary<ulong, int>();
        var eligible = 0;
        foreach (var peer in roster)
        {
            if (peer.IsStale(now) || !peer.InCombat || !IsDpsRole(peer.Role) || peer.TargetId == 0)
                continue;
            eligible++;
            counts[peer.TargetId] = counts.GetValueOrDefault(peer.TargetId) + 1;
        }

        if (counts.Count == 0)
            return (0, 0, 0);

        ulong majority = 0;
        var best = -1;
        foreach (var (id, count) in counts)
        {
            if (count > best) { best = count; majority = id; }
        }

        return (majority, counts.Count, eligible);
    }

    private void DrawAgreement(int distinctTargets, int eligibleDps)
    {
        if (eligibleDps == 0)
            return;

        ImGui.Spacing();
        if (distinctTargets <= 1)
            ImGui.TextColored(Green, $"focused — {eligibleDps} DPS on one target");
        else
            ImGui.TextColored(Yellow, $"split — {distinctTargets} targets across {eligibleDps} DPS");
    }

    private void DrawBurstStrip(int readyCount)
    {
        if (_bus.IsBurstFireActive)
        {
            ImGui.Spacing();
            ImGui.TextColored(DaedalusTheme.AccentGold, "⚡ BURST WINDOW OPEN");
            return;
        }

        if (readyCount > 0)
        {
            var total = Math.Max(readyCount, _bus.Roster.Count);
            ImGui.Spacing();
            ImGui.TextColored(Yellow, $"⚡ Burst readiness {readyCount}/{total}");
        }
    }

    private void DrawAlerts(DateTime now)
    {
        if (_alerts.Count == 0)
            return;

        ImGui.Spacing();
        ImGui.TextColored(DaedalusTheme.TextDisabled, "Alerts");
        foreach (var alert in _alerts)
        {
            var age = (int)Math.Max(0, (now - alert.Time).TotalSeconds);
            ImGui.TextColored(alert.Color, alert.Text);
            ImGui.SameLine();
            ImGui.TextColored(DaedalusTheme.TextDisabled, $"{age}s ago");
        }
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

    private void DrawTargetModeControls()
    {
        var mode = _bus.CurrentTargetMode;

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(DaedalusTheme.AccentGold, "Target mode");
        ImGui.SameLine();
        DrawModeSegmented(mode);

        // Off-tank picker: right-aligned on the same row (label + 150px combo).
        const float offTankBlock = 216f;
        var rightX = ImGui.GetWindowContentRegionMax().X - offTankBlock;
        if (rightX > ImGui.GetCursorPosX())
            ImGui.SameLine(rightX);
        else
            ImGui.SameLine();
        DrawOffTankPicker();

        if (mode == PartyTargetMode.Focus)
            DrawFocusEnemyList();
    }

    private static readonly (string Label, PartyTargetMode Mode)[] ModeSegments =
    [
        ("None", PartyTargetMode.None),
        ("Focus", PartyTargetMode.Focus),
        ("Split", PartyTargetMode.Split),
        ("Kill Adds", PartyTargetMode.KillAdds),
    ];

    /// <summary>
    /// Segmented mode control — buttons sit flush (1px gutter) so they read as one pill; the active
    /// segment is a solid gold fill with dark text, matching the mockup.
    /// </summary>
    private void DrawModeSegmented(PartyTargetMode current)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(1f, ImGui.GetStyle().ItemSpacing.Y));
        for (var i = 0; i < ModeSegments.Length; i++)
        {
            var (label, target) = ModeSegments[i];
            if (i > 0) ImGui.SameLine();

            var active = target == current;
            if (active)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, DaedalusTheme.AccentGold);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, DaedalusTheme.AccentGold);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, DaedalusTheme.AccentGold);
                ImGui.PushStyleColor(ImGuiCol.Text, DaedalusTheme.BgDeep);
            }

            if (ImGui.Button(label))
                _bus.BroadcastTargetMode(target, _bus.FocusTargetId, _bus.OffTankSenderId);

            if (active)
                ImGui.PopStyleColor(4);
        }
        ImGui.PopStyleVar();
    }

    private void DrawOffTankPicker()
    {
        var current = _bus.OffTankSenderId;
        var tanks = _bus.Roster
            .Where(p => p.Role == "Tank")
            .OrderBy(p => p.SenderId, StringComparer.Ordinal)
            .ToArray();

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Dim, "Off-tank:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        var currentLabel = current.Length == 0 ? "none (all tanks protected)" : DisplayNameFor(current);
        if (ImGui.BeginCombo("##offtank", currentLabel))
        {
            if (ImGui.Selectable("none (all tanks protected)", current.Length == 0))
                SetOffTank("");
            foreach (var tank in tanks)
            {
                var scramble = _config.PartyCoordination.LanScrambleNames;
                var label = (scramble ? AliasFor(tank.SenderId) : tank.CharacterName) + $"##ot{tank.SenderId}";
                if (ImGui.Selectable(label, tank.SenderId == current))
                    SetOffTank(tank.SenderId);
            }
            ImGui.EndCombo();
        }
    }

    private void SetOffTank(string senderId)
        => _bus.BroadcastTargetMode(_bus.CurrentTargetMode, _bus.FocusTargetId, senderId);

    private void DrawFocusEnemyList()
    {
        ImGui.Spacing();

        var player = _objectTable.LocalPlayer;
        var enemies = player == null
            ? Array.Empty<IBattleNpc>()
            : EnumerateNearbyEnemies(player.Position).Take(8).ToArray();

        var myTarget = _targetManager.Target as IBattleNpc;
        var hasMyTarget = myTarget != null && myTarget.CurrentHp > 0;

        var style = ImGui.GetStyle();
        var avail = ImGui.GetContentRegionAvail().X - style.WindowPadding.X * 2f;
        var rows = enemies.Length == 0 ? 1 : CountChipRows(enemies, avail);
        var height = ImGui.GetTextLineHeightWithSpacing()
                     + (hasMyTarget ? ImGui.GetFrameHeightWithSpacing() : 0f)
                     + rows * ImGui.GetFrameHeightWithSpacing()
                     + style.WindowPadding.Y * 2f;

        ImGui.PushStyleColor(ImGuiCol.Border, DaedalusTheme.AccentDim);
        ImGui.BeginChild("focusbox", new Vector2(0f, height), true);

        ImGui.TextColored(Dim, "Focus target — click an enemy");

        if (hasMyTarget && ImGui.Button($"Use my target: {myTarget!.Name.TextValue}##focusmine"))
            _bus.BroadcastTargetMode(PartyTargetMode.Focus, myTarget.GameObjectId, _bus.OffTankSenderId);

        if (enemies.Length == 0)
            ImGui.TextColored(Dim, "no enemies nearby");
        else
            DrawFocusChips(enemies);

        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawFocusChips(IBattleNpc[] enemies)
    {
        var focusId = _bus.FocusTargetId;
        var maxX = ImGui.GetWindowContentRegionMax().X;
        var padX = ImGui.GetStyle().FramePadding.X * 2f;
        var spacing = ImGui.GetStyle().ItemSpacing.X;

        for (var i = 0; i < enemies.Length; i++)
        {
            var enemy = enemies[i];
            var hp = enemy.MaxHp > 0 ? (float)enemy.CurrentHp / enemy.MaxHp : 0f;
            var active = enemy.GameObjectId == focusId;

            if (active) ImGui.PushStyleColor(ImGuiCol.Button, DaedalusTheme.AccentGold);
            if (active) ImGui.PushStyleColor(ImGuiCol.Text, DaedalusTheme.BgDeep);
            if (ImGui.Button($"{enemy.Name.TextValue}  {hp:P0}##foc{enemy.GameObjectId}"))
                _bus.BroadcastTargetMode(PartyTargetMode.Focus, enemy.GameObjectId, _bus.OffTankSenderId);
            if (active) ImGui.PopStyleColor(2);

            // Flow chips horizontally; keep the next on this row only if it fits before the edge.
            if (i + 1 < enemies.Length)
            {
                var next = enemies[i + 1];
                var nextHp = next.MaxHp > 0 ? (float)next.CurrentHp / next.MaxHp : 0f;
                var nextWidth = ImGui.CalcTextSize($"{next.Name.TextValue}  {nextHp:P0}").X + padX;
                var itemRightLocal = ImGui.GetItemRectMax().X - ImGui.GetWindowPos().X;
                if (itemRightLocal + spacing + nextWidth < maxX)
                    ImGui.SameLine();
            }
        }
    }

    private static int CountChipRows(IBattleNpc[] enemies, float availWidth)
    {
        var style = ImGui.GetStyle();
        var padX = style.FramePadding.X * 2f;
        var spacing = style.ItemSpacing.X;
        var rows = 1;
        var x = 0f;
        foreach (var enemy in enemies)
        {
            var hp = enemy.MaxHp > 0 ? (float)enemy.CurrentHp / enemy.MaxHp : 0f;
            var w = ImGui.CalcTextSize($"{enemy.Name.TextValue}  {hp:P0}").X + padX;
            if (x <= 0f)
                x = w;
            else if (x + spacing + w <= availWidth)
                x += spacing + w;
            else { rows++; x = w; }
        }
        return rows;
    }

    private IEnumerable<IBattleNpc> EnumerateNearbyEnemies(Vector3 center)
    {
        foreach (var obj in _objectTable)
        {
            if (obj is not IBattleNpc npc)
                continue;
            if ((byte)npc.BattleNpcKind != Daedalus.Compat.BattleNpcKinds.Combatant && npc.SubKind != 0)
                continue;
            if (npc.CurrentHp == 0 || !npc.IsTargetable || !EnemyAttackability.IsPlayerAttackable(npc))
                continue;
            if (Vector3.Distance(center, npc.Position) > 30f)
                continue;

            yield return npc;
        }
    }

    private string DisplayNameFor(string senderId)
    {
        if (_config.PartyCoordination.LanScrambleNames)
            return AliasFor(senderId);

        foreach (var peer in _bus.Roster)
        {
            if (peer.SenderId == senderId)
                return peer.CharacterName.Length > 0 ? peer.CharacterName : senderId;
        }

        return senderId;
    }

    /// <summary>
    /// Roster grouped per machine, each group in its own table so the role / name / job / HP / slot /
    /// status columns line up (a proportional font makes space-padding useless — tables are the fix).
    /// </summary>
    private void DrawRoster(LanPeerInfo[] roster, DateTime now, Daedalus.Config.PartyCoordinationConfig cfg,
        HashSet<string> burstReady)
    {
        string? currentMachine = null;
        var tableOpen = false;

        foreach (var peer in roster)
        {
            var isLocal = peer.MachineId == _bus.LocalMachineId;
            if (isLocal && cfg.LanShowRemoteOnly)
                continue;

            if (peer.MachineId != currentMachine)
            {
                if (tableOpen) { ImGui.EndTable(); tableOpen = false; }

                currentMachine = peer.MachineId;
                ImGui.Spacing();
                ImGui.TextColored(isLocal ? DaedalusTheme.AccentGold : DaedalusTheme.AccentDim, $"■ {peer.MachineId}");
                ImGui.SameLine();
                ImGui.TextColored(DaedalusTheme.TextDisabled, isLocal ? "(Local)" : "(Remote)");

                tableOpen = BeginRosterTable(currentMachine);
            }

            if (tableOpen)
                DrawToonRow(peer, now, cfg.LanCompactMode, cfg.LanShowHpBars, cfg.LanScrambleNames, burstReady);
        }

        if (tableOpen)
            ImGui.EndTable();
    }

    private static bool BeginRosterTable(string machineId)
    {
        if (!ImGui.BeginTable($"roster##{machineId}", 6, ImGuiTableFlags.NoBordersInBody | ImGuiTableFlags.PadOuterX))
            return false;

        ImGui.TableSetupColumn("##role", ImGuiTableColumnFlags.WidthFixed, 20f);
        ImGui.TableSetupColumn("##name", ImGuiTableColumnFlags.WidthFixed, 150f);
        ImGui.TableSetupColumn("##job", ImGuiTableColumnFlags.WidthFixed, 40f);
        ImGui.TableSetupColumn("##hp", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("##slot", ImGuiTableColumnFlags.WidthFixed, 52f);
        ImGui.TableSetupColumn("##status", ImGuiTableColumnFlags.WidthFixed, 80f);
        return true;
    }

    private void DrawToonRow(LanPeerInfo peer, DateTime now, bool compact, bool showHp, bool scramble,
        IReadOnlyCollection<string> burstReady)
    {
        var (state, stateColor, diagnosis) = SyncStateFor(peer, now);
        var isDead = state == SyncState.Synced && peer.HpPercent <= 0f;

        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        DrawRoleSyncIcon(peer.Role, state, stateColor);

        ImGui.TableNextColumn();
        var name = scramble ? AliasFor(peer.SenderId) : peer.CharacterName;
        if (name.Length == 0) name = scramble ? AliasFor(peer.SenderId) : peer.SenderId;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(name);

        ImGui.TableNextColumn();
        ImGui.TextColored(Dim, peer.JobAbbrev.Length > 0 ? peer.JobAbbrev : "???");

        ImGui.TableNextColumn();
        if (isDead)
        {
            // A synced toon at 0 HP is dead — call it out instead of an empty green bar.
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, Red);
            ImGui.ProgressBar(1f, new Vector2(-1f, 14f), "DEAD");
            ImGui.PopStyleColor();
        }
        else if (showHp && !compact)
        {
            // Stale data renders a grey bar — the number is a memory, not a reading.
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, state == SyncState.Synced ? HpColor : Grey);
            ImGui.ProgressBar(Math.Clamp(peer.HpPercent, 0f, 1f), new Vector2(-1f, 14f), $"{peer.HpPercent:P0}");
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.TextColored(state == SyncState.Synced ? HpColor : Grey, $"{peer.HpPercent:P0}");
        }

        ImGui.TableNextColumn();
        if (peer.AssignedSlot.Length > 0)
            ImGui.TextColored(Dim, peer.AssignedSlot);

        ImGui.TableNextColumn();
        DrawRowStatus(peer, state, stateColor, diagnosis, isDead, burstReady);
    }

    private void DrawRowStatus(LanPeerInfo peer, SyncState state, Vector4 stateColor, string diagnosis,
        bool isDead, IReadOnlyCollection<string> burstReady)
    {
        // Burst-ready pip: this toon has signaled its burst cooldowns are up this cycle.
        if (burstReady.Contains(peer.SenderId))
        {
            ImGui.TextColored(DaedalusTheme.AccentGold, "⚡");
            ImGui.SameLine(0f, 4f);
        }

        if (isDead)
        {
            ImGui.TextColored(Red, "raise");
            return;
        }

        // Off-target marker: an in-combat DPS on a different enemy than the party majority.
        if (IsDpsRole(peer.Role) && peer.InCombat && peer.TargetId != 0
            && _majorityTargetId != 0 && peer.TargetId != _majorityTargetId)
        {
            ImGui.TextColored(Yellow, "off");
            return;
        }

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

        if (ImGui.Button("Force Burst"))
            _bus.ForceBurstFire();
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
