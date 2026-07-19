using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface;
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

    // Greek places for machine names under the scramble toggle (hostnames identify the PC).
    private static readonly string[] MachineAliasPool =
    [
        "Olympus", "Delphi", "Ithaca", "Crete", "Rhodes", "Sparta", "Argos", "Thebes",
    ];

    private readonly CoordinationBus _bus;
    private readonly LanCoordinator _lan;
    private readonly Configuration _config;
    private readonly Action _save;
    private readonly IObjectTable _objectTable;
    private readonly ITargetManager _targetManager;
    private readonly Dictionary<string, string> _aliases = new();
    private readonly Dictionary<string, string> _machineAliases = new();

    // Party-group indicator: distinct color per in-game party, assigned first-seen (session-stable).
    private readonly Dictionary<ulong, int> _groupColorIndex = new();
    private static readonly Vector4[] GroupPalette =
    [
        new(0.85f, 0.65f, 0.20f, 1f), // gold
        new(0.35f, 0.75f, 0.75f, 1f), // teal
        new(0.70f, 0.50f, 0.90f, 1f), // purple
        new(0.90f, 0.55f, 0.40f, 1f), // coral
    ];

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
        _bus.OnTankSwapActivity += OnTankSwapAlert;
        _bus.OnAddSpawn += OnAddSpawnAlert;
    }

    public void Dispose()
    {
        _bus.OnHealerDown -= OnHealerDownAlert;
        _bus.OnPhoenixDown -= OnPhoenixDownAlert;
        _bus.OnTankSwapActivity -= OnTankSwapAlert;
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

    private void OnTankSwapAlert(string description) => PushAlert($"tank swap: {description}", Yellow);

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

        DrawAgreement(roster, now, distinctTargets, eligibleDps);
        DrawBurstStrip(roster, now, burstReady);
        DrawBluCoilAssignments(roster, now);
        DrawBluFleetControls(roster, now);
        DrawAlerts(now);

        ImGui.Spacing();
        ImGui.Separator();
        DrawToggles();
    }

    /// <summary>The local territory id — set by Plugin (the window has no IClientState).</summary>
    public static Func<ushort>? TerritorySource;

    /// <summary>
    /// BLU v3.5 pre-pull checklist: inside a Coil turn with BLU toons on the bus, show each
    /// utility slot's deterministic carrier assignment — red when fewer capable toons than the
    /// fight needs (fix loadouts BEFORE the pull; the mechanics themselves stay manual).
    /// </summary>
    private void DrawBluCoilAssignments(LanPeerInfo[] roster, DateTime now)
    {
        var territory = TerritorySource?.Invoke() ?? 0;
        if (territory == 0 || !Daedalus.Data.BluDutyAssignments.HasRequirements(territory))
            return;

        var bluRoster = roster
            .Where(p => !p.IsStale(now) && p.JobId == Daedalus.Data.JobRegistry.BlueMage)
            .Select(p => new Daedalus.Services.Blu.BluPeerCapability(
                p.SenderId, (Daedalus.Services.Blu.BluCapabilities)p.BluCapabilities))
            .ToArray();
        if (bluRoster.Length == 0)
            return;

        var results = Daedalus.Data.BluDutyAssignments.Evaluate(territory, bluRoster);
        if (results.Count == 0)
            return;

        ImGui.Spacing();
        ImGui.TextColored(Dim, $"BLU assignments — {results[0].Requirement.DutyName}");
        foreach (var result in results)
        {
            var req = result.Requirement;
            if (result.Satisfied)
            {
                var names = string.Join(", ", result.Assigned.Select(DisplayName));
                ImGui.TextColored(Green, $"  ✓ {req.RoleLabel}: {names}");
            }
            else
            {
                ImGui.TextColored(Red,
                    $"  ✗ {req.RoleLabel}: needs {req.RequiredCount}, only {result.Assigned.Count} capable"
                    + (result.Assigned.Count > 0
                        ? $" ({string.Join(", ", result.Assigned.Select(DisplayName))})"
                        : " — slot the spell before pulling"));
            }
        }

        if (Daedalus.Data.BluDutyAssignments.UsesMoonFluteStagger(territory))
        {
            var fluteCapable = bluRoster
                .Where(p => p.Has(Daedalus.Services.Blu.BluCapabilities.MoonFlute))
                .Select(p => p.SenderId)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray();
            if (fluteCapable.Length >= 2)
            {
                var groupA = fluteCapable.Where((_, i) => i % 2 == 0).Select(DisplayName);
                var groupB = fluteCapable.Where((_, i) => i % 2 == 1).Select(DisplayName);
                ImGui.TextColored(Dim,
                    $"  Flute stagger — A: {string.Join(", ", groupA)} · B (+30s): {string.Join(", ", groupB)}");
            }
        }
    }

    private string DisplayName(string senderId)
        => _config.PartyCoordination.LanScrambleNames ? AliasFor(senderId) : senderId.Split('@')[0];

    /// <summary>
    /// BLU fleet controls (v3.4): fleet-wide mimicry buttons (every BLU box scans + casts its own
    /// mimicry / removes it) and the manual FLEET STING trigger — plans the stinger order from the
    /// advertised capabilities and the operator's CURRENT TARGET, then broadcasts it. Ctrl-gated:
    /// the cast kills every planned stinger.
    /// </summary>
    private void DrawBluFleetControls(LanPeerInfo[] roster, DateTime now)
    {
        var bluRoster = roster
            .Where(p => !p.IsStale(now) && p.JobId == Daedalus.Data.JobRegistry.BlueMage)
            .Select(p => new Daedalus.Services.Blu.BluPeerCapability(
                p.SenderId, (Daedalus.Services.Blu.BluCapabilities)p.BluCapabilities))
            .ToArray();
        if (bluRoster.Length == 0)
            return;

        ImGui.Spacing();
        ImGui.TextColored(Dim, $"BLU fleet ({bluRoster.Length} toon{(bluRoster.Length == 1 ? "" : "s")})");

        ImGui.TextUnformatted("Mimic:");
        ImGui.SameLine();
        if (ImGui.SmallButton("Tank##fleetmim"))
            _bus.BroadcastBluMimicry(new LanBluMimicryPayload { Role = (int)Daedalus.Config.DPS.BluRole.Tank });
        ImGui.SameLine();
        if (ImGui.SmallButton("DPS##fleetmim"))
            _bus.BroadcastBluMimicry(new LanBluMimicryPayload { Role = (int)Daedalus.Config.DPS.BluRole.Dps });
        ImGui.SameLine();
        if (ImGui.SmallButton("Healer##fleetmim"))
            _bus.BroadcastBluMimicry(new LanBluMimicryPayload { Role = (int)Daedalus.Config.DPS.BluRole.Healer });
        ImGui.SameLine();
        if (ImGui.SmallButton("Remove##fleetmim"))
            _bus.BroadcastBluMimicry(new LanBluMimicryPayload { Remove = true });
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Every BLU box applies the command to ITSELF (scan + cast / targetless removal).");

        // Freeze/shatter owner pick: the chosen toon outranks the automatic election (a
        // preference bit on its heartbeat — every box converges within ~2s). "Auto" restores
        // the SenderId-sort election among capable toons.
        ImGui.TextUnformatted("Shatter:");
        ImGui.SameLine();
        var currentPick = bluRoster.FirstOrDefault(p =>
            p.Has(Daedalus.Services.Blu.BluCapabilities.PreferredFreezeShatter)).SenderId ?? "";
        ImGui.SetNextItemWidth(160);
        if (ImGui.BeginCombo("##shatterpick", currentPick.Length == 0 ? "Auto" : DisplayName(currentPick)))
        {
            if (ImGui.Selectable("Auto", currentPick.Length == 0))
                _bus.BroadcastBluPreferShatter(new LanBluPreferShatterPayload { SenderId = "" });
            foreach (var peer in bluRoster)
            {
                var capable = peer.Has(Daedalus.Services.Blu.BluCapabilities.Ultravibration)
                              || peer.Has(Daedalus.Services.Blu.BluCapabilities.RamsVoice);
                using var _ = Dalamud.Interface.Utility.Raii.ImRaii.Disabled(!capable);
                var label = DisplayName(peer.SenderId)
                            + (capable ? "" : " (no Ram's Voice/Ultravibration slotted)");
                if (ImGui.Selectable(label + $"##sp{peer.SenderId}", peer.SenderId == currentPick))
                    _bus.BroadcastBluPreferShatter(new LanBluPreferShatterPayload { SenderId = peer.SenderId });
            }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Which toon starts the freeze and shatters (Ram's Voice → Ultravibration)."
                + "\nAuto = first capable toon alphabetically. The pick survives until changed or the boxes restart.");

        // Fleet sting: plan from the operator's current target.
        var boss = _targetManager.Target as Dalamud.Game.ClientState.Objects.Types.IBattleNpc;
        var estSting = Daedalus.Rotation.ProteusCore.Helpers.FinalStingCalculator.Estimate(
            _config.BlueMage.FinalStingBaselineDamage, _config.BlueMage.FinalStingBaselinePotency);
        var order = boss is { CurrentHp: > 0 }
            ? Daedalus.Services.Blu.BluFleetStingPlanner.Plan(
                boss.CurrentHp, estSting, _config.BlueMage.FleetStingSafetyPercent / 100f, bluRoster)
            : [];

        var ctrlHeld = ImGui.GetIO().KeyCtrl;
        using (Dalamud.Interface.Utility.Raii.ImRaii.Disabled(order.Count == 0 || !ctrlHeld))
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.15f, 0.15f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.75f, 0.20f, 0.20f, 1f));
            var pressed = ImGui.SmallButton($"FLEET STING ({order.Count})##fleetsting");
            ImGui.PopStyleColor(2);
            if (pressed && boss != null && order.Count > 0)
            {
                _bus.BroadcastExecuteSting(new LanExecuteStingPayload
                {
                    TargetId = boss.GameObjectId,
                    Stingers = order.ToArray(),
                });
                PushAlert($"fleet sting → {order.Count} stinger(s)", Red);
            }
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(order.Count == 0
                ? "Needs a targeted enemy and at least one opted-in stinger (BLU settings → fleet sting)."
                : $"Hold CTRL to fire. Staggered 3s apart, in order: {string.Join(" → ", order.Select(DisplayName))}."
                  + "\nEach sting KILLS its caster. Later slots auto-abort if the boss dies first.");
        }
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

    /// <summary>
    /// Mode-aware compliance line: under Focus it reads "Focus: X/N DPS on {enemy} · {toon} down ·
    /// MT holds boss"; without a mode it falls back to the focused/split summary.
    /// </summary>
    private void DrawAgreement(LanPeerInfo[] roster, DateTime now, int distinctTargets, int eligibleDps)
    {
        var mode = _bus.CurrentTargetMode;
        var deadName = FindDeadToonName(roster, now);

        if (mode == PartyTargetMode.None && eligibleDps == 0 && deadName == null)
            return;

        ImGui.Spacing();

        if (mode == PartyTargetMode.Focus && _bus.FocusTargetId != 0)
        {
            var onFocus = roster.Count(p =>
                !p.IsStale(now) && p.InCombat && IsDpsRole(p.Role) && p.TargetId == _bus.FocusTargetId);
            var totalDps = roster.Count(p => !p.IsStale(now) && IsDpsRole(p.Role));
            var focusName = _objectTable.SearchById(_bus.FocusTargetId)?.Name.TextValue ?? "target";
            ImGui.TextColored(DaedalusTheme.AccentGold, $"Focus: {onFocus}/{Math.Max(totalDps, onFocus)} DPS on {focusName}");
        }
        else if (mode == PartyTargetMode.Split && eligibleDps > 0)
        {
            ImGui.TextColored(DaedalusTheme.AccentGold,
                $"Split: {eligibleDps} DPS across {Math.Max(distinctTargets, 1)} targets");
        }
        else if (mode == PartyTargetMode.KillAdds)
        {
            ImGui.TextColored(DaedalusTheme.AccentGold, "Kill adds");
        }
        else if (eligibleDps > 0)
        {
            if (distinctTargets <= 1)
                ImGui.TextColored(Green, $"focused — {eligibleDps} DPS on one target");
            else
                ImGui.TextColored(Yellow, $"split — {distinctTargets} targets across {eligibleDps} DPS");
        }

        if (deadName != null)
        {
            ImGui.SameLine();
            ImGui.TextColored(Dim, " · ");
            ImGui.SameLine();
            ImGui.TextColored(Red, $"{deadName} down");
        }

        if (mode != PartyTargetMode.None && HasProtectedTank(roster, now))
        {
            ImGui.SameLine();
            ImGui.TextColored(Dim, " · MT holds boss");
        }
    }

    private string? FindDeadToonName(LanPeerInfo[] roster, DateTime now)
    {
        foreach (var peer in roster)
        {
            if (!peer.IsStale(now) && peer.HpPercent <= 0f && (peer.Role.Length > 0 || peer.JobAbbrev.Length > 0))
                return _config.PartyCoordination.LanScrambleNames ? AliasFor(peer.SenderId) : peer.CharacterName;
        }

        return null;
    }

    private bool HasProtectedTank(LanPeerInfo[] roster, DateTime now) =>
        roster.Any(p => !p.IsStale(now) && p.Role == "Tank" && p.SenderId != _bus.OffTankSenderId);

    /// <summary>
    /// Bordered burst strip (mock parity): readiness count + per-toon pips + a gold Force button.
    /// Shown whenever there is a roster; flips to a BURST OPEN banner while the window is live.
    /// </summary>
    private void DrawBurstStrip(LanPeerInfo[] roster, DateTime now, HashSet<string> burstReady)
    {
        if (roster.Length == 0)
            return;

        ImGui.Spacing();
        var height = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().WindowPadding.Y * 2f - ImGui.GetStyle().ItemSpacing.Y;
        ImGui.PushStyleColor(ImGuiCol.Border, DaedalusTheme.AccentDim);
        ImGui.BeginChild("burststrip", new Vector2(0f, height), true);

        var fresh = roster.Where(p => !p.IsStale(now)).ToArray();
        var readyCount = fresh.Count(p => burstReady.Contains(p.SenderId));

        ImGui.AlignTextToFramePadding();
        if (_bus.IsBurstFireActive)
        {
            ImGui.TextColored(DaedalusTheme.AccentGold, "⚡ BURST WINDOW OPEN");
        }
        else
        {
            ImGui.TextColored(DaedalusTheme.AccentGold, "⚡ Burst readiness");
            ImGui.SameLine();
            ImGui.Text($"{readyCount}/{Math.Max(fresh.Length, 1)}");
            ImGui.SameLine(0f, 10f);
            foreach (var peer in fresh)
            {
                ImGui.TextColored(burstReady.Contains(peer.SenderId) ? DaedalusTheme.AccentGold : DaedalusTheme.TextDisabled, "⚡");
                ImGui.SameLine(0f, 2f);
            }
        }

        // Gold Force button flush right, like the mock.
        const string forceLabel = "Force burst now";
        var buttonWidth = ImGui.CalcTextSize(forceLabel).X + ImGui.GetStyle().FramePadding.X * 2f;
        var rightX = ImGui.GetWindowContentRegionMax().X - buttonWidth;
        if (rightX > ImGui.GetCursorPosX())
            ImGui.SameLine(rightX);
        else
            ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Button, DaedalusTheme.AccentGold);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, DaedalusTheme.AccentGold);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, DaedalusTheme.AccentDim);
        ImGui.PushStyleColor(ImGuiCol.Text, DaedalusTheme.BgDeep);
        if (ImGui.Button(forceLabel))
            _bus.ForceBurstFire();
        ImGui.PopStyleColor(4);

        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawAlerts(DateTime now)
    {
        if (_alerts.Count == 0)
            return;

        ImGui.Spacing();
        ImGui.TextColored(DaedalusTheme.TextDisabled, "Alerts");
        var scramble = _config.PartyCoordination.LanScrambleNames;
        foreach (var alert in _alerts)
        {
            var age = (int)Math.Max(0, (now - alert.Time).TotalSeconds);
            // Alerts capture raw names at event time; scramble is applied at draw so the toggle
            // covers alerts that fired before it was switched on.
            ImGui.TextColored(alert.Color, scramble ? ScrambleNamesIn(alert.Text) : alert.Text);
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

        // Latency sits flush right, like the mock.
        var latency = _bus.RemoteLatencyMs;
        if (latency > 0)
        {
            var text = $"{latency:F0} ms";
            var rightX = ImGui.GetWindowContentRegionMax().X - ImGui.CalcTextSize(text).X;
            if (rightX > ImGui.GetCursorPosX())
                ImGui.SameLine(rightX);
            else
                ImGui.SameLine();
            ImGui.TextColored(latency < 50 ? Green : Dim, text);
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

        DrawSwapTanksButton();

        if (mode == PartyTargetMode.Focus)
            DrawFocusEnemyList();
    }

    /// <summary>
    /// Manual coordinated tank swap — arms the swap on every tank box (which then run the
    /// Provoke/Shirk handshake per live aggro). Needs two tanks; shown disabled otherwise so the
    /// feature is discoverable instead of silently missing.
    /// </summary>
    private void DrawSwapTanksButton()
    {
        var tankCount = _bus.Roster.Count(p => p.Role == "Tank");
        var enabled = tankCount >= 2;

        if (!enabled) ImGui.BeginDisabled();
        if (ImGui.Button("Swap tanks") && enabled)
            _bus.BroadcastTankSwapCommand();
        if (!enabled) ImGui.EndDisabled();

        ImGui.SameLine();
        if (!enabled)
        {
            ImGui.TextColored(DaedalusTheme.TextDisabled, $"needs 2 tanks in roster ({tankCount}/2)");
        }
        else
        {
            ImGui.TextColored(Dim, _bus.OffTankSenderId.Length > 0
                ? $"off-tank: {DisplayNameFor(_bus.OffTankSenderId)}"
                : "swaps by live aggro (no off-tank set)");
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "Coordinated tank swap: the incoming tank pre-mitigates and Provokes after the\n"
                + "current tank confirms, then the current tank Shirks once the boss flips.\n"
                + "Both tanks must enable it: Settings → Tanks → Shared → Tank Coordination.");
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
        var localGroupId = roster.FirstOrDefault(p => p.SenderId == _bus.LocalSenderId)?.PartyGroupId ?? 0;

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
                var machineLabel = cfg.LanScrambleNames ? MachineAliasFor(peer.MachineId) : peer.MachineId;
                ImGui.TextColored(isLocal ? DaedalusTheme.AccentGold : DaedalusTheme.AccentDim, $"■ {machineLabel}");
                ImGui.SameLine();
                ImGui.TextColored(DaedalusTheme.TextDisabled, isLocal ? "(Local)" : "(Remote)");

                tableOpen = BeginRosterTable(currentMachine);
            }

            if (tableOpen)
                DrawToonRow(peer, now, cfg.LanCompactMode, cfg.LanShowHpBars, cfg.LanScrambleNames, burstReady, localGroupId);
        }

        if (tableOpen)
            ImGui.EndTable();
    }

    private static bool BeginRosterTable(string machineId)
    {
        if (!ImGui.BeginTable($"roster##{machineId}", 7, ImGuiTableFlags.NoBordersInBody | ImGuiTableFlags.PadOuterX))
            return false;

        ImGui.TableSetupColumn("##role", ImGuiTableColumnFlags.WidthFixed, 20f);
        ImGui.TableSetupColumn("##name", ImGuiTableColumnFlags.WidthFixed, 150f);
        ImGui.TableSetupColumn("##grp", ImGuiTableColumnFlags.WidthFixed, 42f);
        ImGui.TableSetupColumn("##job", ImGuiTableColumnFlags.WidthFixed, 40f);
        ImGui.TableSetupColumn("##hp", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("##slot", ImGuiTableColumnFlags.WidthFixed, 52f);
        ImGui.TableSetupColumn("##status", ImGuiTableColumnFlags.WidthFixed, 80f);
        return true;
    }

    private void DrawToonRow(LanPeerInfo peer, DateTime now, bool compact, bool showHp, bool scramble,
        IReadOnlyCollection<string> burstReady, ulong localGroupId)
    {
        var (state, stateColor, diagnosis) = SyncStateFor(peer, now);
        var isDead = state == SyncState.Synced && peer.HpPercent <= 0f;

        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        DrawRoleSyncIcon(peer.Role, state, isDead ? Red : stateColor);

        ImGui.TableNextColumn();
        var name = scramble ? AliasFor(peer.SenderId) : peer.CharacterName;
        if (name.Length == 0) name = scramble ? AliasFor(peer.SenderId) : peer.SenderId;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(name);

        // Party-group column: same-colored dot = same in-game party. Invite actions moved to
        // Charon (Group Management) — this window is a pure status/coordination display; the
        // roster IPC carries contentId/worldId for Charon's native invites.
        ImGui.TableNextColumn();
        if (peer.PartyGroupId != 0)
        {
            if (!_groupColorIndex.TryGetValue(peer.PartyGroupId, out var groupIdx))
            {
                groupIdx = _groupColorIndex.Count;
                _groupColorIndex[peer.PartyGroupId] = groupIdx;
            }

            DaedalusTheme.StatusIcon(FontAwesomeIcon.Circle, GroupPalette[groupIdx % GroupPalette.Length]);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Party group {(char)('A' + groupIdx % 26)} — same in-game party");
        }

        ImGui.TableNextColumn();
        ImGui.TextColored(Dim, peer.JobAbbrev.Length > 0 ? peer.JobAbbrev : "???");

        ImGui.TableNextColumn();
        if (isDead)
        {
            // A synced toon at 0 HP is dead — dark empty bar with red DEAD text (mock parity).
            ImGui.PushStyleColor(ImGuiCol.Text, Red);
            ImGui.ProgressBar(0f, new Vector2(-1f, 14f), "DEAD");
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

        // Focus compliance marker: a DPS actually on the party focus target (mock's "→ focus").
        if (_bus.CurrentTargetMode == PartyTargetMode.Focus
            && _bus.FocusTargetId != 0
            && IsDpsRole(peer.Role)
            && peer.TargetId == _bus.FocusTargetId)
        {
            ImGui.TextColored(DaedalusTheme.AccentGold, "→ focus");
            return;
        }

        // Off-target marker: an in-combat DPS on a different enemy than the party majority.
        if (IsDpsRole(peer.Role) && peer.InCombat && peer.TargetId != 0
            && _majorityTargetId != 0 && peer.TargetId != _majorityTargetId)
        {
            ImGui.TextColored(Yellow, "off");
            return;
        }

        // Synced toons show their LAST BURST instead of a static "synced" (user ask, raid prep):
        // gold "BURST" while the window runs, "burst Xs/Xm ago" dim after, plain "synced" until
        // the toon's first window. Heartbeat age (≤5s here) is folded in so the number is honest.
        if (state == SyncState.Synced && peer.SecondsSinceLastBurst > 0f)
        {
            var burstAge = peer.SecondsSinceLastBurst + (float)(DateTime.UtcNow - peer.LastSeenUtc).TotalSeconds;
            if (burstAge <= 20f)
            {
                ImGui.TextColored(DaedalusTheme.AccentGold, burstAge <= 5f ? "BURST" : $"burst {burstAge:F0}s");
            }
            else
            {
                var label = burstAge < 120f ? $"burst {burstAge:F0}s ago" : $"burst {burstAge / 60f:F0}m ago";
                ImGui.TextColored(DaedalusTheme.TextDisabled, label);
            }
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

    /// <summary>Session-consistent machine alias — hostnames identify the PC, so scramble covers them too.</summary>
    private string MachineAliasFor(string machineId)
    {
        if (_machineAliases.TryGetValue(machineId, out var alias))
            return alias;

        alias = MachineAliasPool[_machineAliases.Count % MachineAliasPool.Length];
        if (_machineAliases.Count >= MachineAliasPool.Length)
            alias += $" {_machineAliases.Count / MachineAliasPool.Length + 1}";

        _machineAliases[machineId] = alias;
        return alias;
    }

    /// <summary>
    /// Replaces every known roster character name in <paramref name="text"/> with its alias.
    /// Applied at DRAW time so alerts captured before the scramble toggle flipped stay covered.
    /// </summary>
    private string ScrambleNamesIn(string text)
    {
        foreach (var peer in _bus.Roster)
        {
            if (peer.CharacterName.Length > 0 && text.Contains(peer.CharacterName, StringComparison.Ordinal))
                text = text.Replace(peer.CharacterName, AliasFor(peer.SenderId), StringComparison.Ordinal);
        }

        return text;
    }
}
