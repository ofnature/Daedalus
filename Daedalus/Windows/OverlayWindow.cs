using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Daedalus.Data;
using Daedalus.Localization;
using Daedalus.Rotation;
using Daedalus.Rotation.Common;
using Daedalus.Services.Content;
using Daedalus.Services.Prediction;
using Daedalus.Timeline;
using Daedalus.Timeline.Models;

namespace Daedalus.Windows;

public sealed class OverlayWindow : Window
{
    private readonly Configuration _configuration;
    private readonly Action _saveConfiguration;
    private readonly RotationManager _rotationManager;
    private readonly IPartyList _partyList;
    private readonly ITimelineService? _timelineService;
    private readonly IDutyContentService? _dutyContentService;
    private readonly IBossModForecastService? _bossModForecast;

    private const float ForecastWindowSeconds = 60f;

    private static readonly Vector4 ActiveColor    = Common.DaedalusTheme.StatusGreen;
    private static readonly Vector4 InactiveColor  = Common.DaedalusTheme.StatusGrey;
    private static readonly Vector4 ActionColor    = Common.DaedalusTheme.AccentGold;
    private static readonly Vector4 AlertColor     = Common.DaedalusTheme.StatusYellow;
    private static readonly Vector4 MechanicImminentColor   = Common.DaedalusTheme.StatusRed;
    private static readonly Vector4 MechanicSoonColor       = Common.DaedalusTheme.StatusYellow;
    private static readonly Vector4 MechanicUpcomingColor   = Common.DaedalusTheme.TextSecondary;
    private static readonly Vector4 RaidwideLabelColor      = Common.DaedalusTheme.StatusYellow;
    private static readonly Vector4 TankBusterLabelColor    = Common.DaedalusTheme.StatusRed;
    private static readonly Vector4 PhaseLabelColor         = Common.DaedalusTheme.AccentDim;

    public OverlayWindow(
        Configuration configuration,
        Action saveConfiguration,
        RotationManager rotationManager,
        IPartyList partyList,
        ITimelineService? timelineService = null,
        IDutyContentService? dutyContentService = null,
        IBossModForecastService? bossModForecast = null)
        : base(
            "##DaedalusOverlay",
            ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoScrollWithMouse
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.AlwaysAutoResize
            | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoNav)
    {
        _configuration = configuration;
        _saveConfiguration = saveConfiguration;
        _rotationManager = rotationManager;
        _partyList = partyList;
        _timelineService = timelineService;
        _dutyContentService = dutyContentService;
        _bossModForecast = bossModForecast;

        Position = new Vector2(configuration.Overlay.X, configuration.Overlay.Y);
        PositionCondition = ImGuiCond.FirstUseEver;
        BgAlpha = 0.88f;
    }

    public override void Draw()
    {
        ImGui.Dummy(new Vector2(200, 0));

        var rotation = _rotationManager.ActiveRotation;

        DrawStatusPillAndRotation(rotation);

        if (rotation != null)
        {
            var state = rotation.DebugState;
            DrawNextAction(state);
            DrawCombatInfo(state, rotation);
        }

        if (_configuration.Overlay.ShowMechanicsForecast)
            DrawMechanicsForecast();

        ImGui.Separator();
        DrawToggles();
    }

    private void DrawStatusPillAndRotation(IRotation? rotation)
    {
        var isActive   = _configuration.Enabled;
        var color      = isActive ? ActiveColor : InactiveColor;
        var statusText = isActive
            ? Loc.T(LocalizedStrings.Overlay.StatusActive,   "Running")
            : Loc.T(LocalizedStrings.Overlay.StatusInactive, "Paused");

        if (Common.DaedalusTheme.StatusChip(statusText, color, "##OverlayToggle"))
        {
            _configuration.Enabled = !_configuration.Enabled;
            _saveConfiguration();
        }

        ImGui.SameLine();
        if (rotation != null)
        {
            var jobId   = rotation.SupportedJobIds[0];
            var jobName = JobRegistry.GetJobName(jobId);
            ImGui.TextDisabled($"{rotation.Name} ({jobName})");
        }
        else
        {
            ImGui.TextDisabled(Loc.T(LocalizedStrings.Overlay.NoRotation, "No rotation active"));
        }

        if (_configuration.EnableAutoDutyConfig && _dutyContentService != null)
        {
            var profile = _dutyContentService.EffectiveProfile;
            var text = profile == EffectiveDutyProfile.None
                ? Loc.TFormat(LocalizedStrings.Overlay.DutyDetected, "Duty: {0}", _dutyContentService.DutyLabel)
                : Loc.TFormat(LocalizedStrings.Overlay.DutyProfile, "Duty: {0} ({1})", _dutyContentService.DutyLabel, profile);
            ImGui.TextDisabled(text);
        }
    }

    private void DrawNextAction(DebugState state)
    {
        var action    = state.PlannedAction;
        var hasAction = !string.IsNullOrEmpty(action) && action != "None";

        ImGui.Text(Loc.T(LocalizedStrings.Overlay.NextActionLabel, "Next:"));
        ImGui.SameLine();
        if (hasAction)
            ImGui.TextColored(ActionColor, action);
        else
            ImGui.TextDisabled(Loc.T(LocalizedStrings.Overlay.NoAction, "—"));
    }

    private void DrawCombatInfo(DebugState state, IRotation rotation)
    {
        // Player HP + injured party on one strip
        if (state.PlayerHpPercent > 0f)
        {
            var hpColor = Common.DaedalusTheme.HpColor(state.PlayerHpPercent);
            ImGui.TextColored(Common.DaedalusTheme.TextSecondary, Loc.T(LocalizedStrings.Overlay.HpLabel, "HP:"));
            ImGui.SameLine();
            ImGui.TextColored(hpColor, $"{state.PlayerHpPercent:P0}");
        }

        // Injured party members
        if (state.AoEInjuredCount > 0)
        {
            if (state.PlayerHpPercent > 0f) ImGui.SameLine(0, 14);
            ImGui.TextColored(Common.DaedalusTheme.TextSecondary, Loc.T(LocalizedStrings.Overlay.PartyLabel, "Party:"));
            ImGui.SameLine();
            var partySize = _partyList.Length > 0 ? _partyList.Length : 1;
            ImGui.TextColored(AlertColor, Loc.TFormat(
                LocalizedStrings.Overlay.PartyInjured, "{0}/{1} injured",
                state.AoEInjuredCount, partySize));
        }

        // Raise alert
        if (state.RaiseState != "Idle"
            && !string.IsNullOrEmpty(state.RaiseTarget)
            && state.RaiseTarget != "None")
        {
            ImGui.TextColored(AlertColor, Loc.TFormat(
                LocalizedStrings.Overlay.RaiseAlert, "Raise: {0}",
                state.RaiseTarget));
        }

        // Positional indicator (melee DPS only)
        if (rotation is IHasPositionals posRotation)
            PositionalDisplayHelper.DrawOverlay(posRotation.Positionals);
    }

    private void DrawMechanicsForecast()
    {
        if (_timelineService is { IsActive: true })
            DrawCactbotForecast();
        else
            DrawBossModForecast();
    }

    /// <summary>Embedded Cactbot timeline forecast — mapped savage/ultimate zones only.</summary>
    private void DrawCactbotForecast()
    {
        if (_timelineService == null)
            return;

        var mechanics = _timelineService.GetUpcomingMechanics(ForecastWindowSeconds);
        if (mechanics.Count == 0)
            return;

        // First pass: check if any player-relevant mechanics exist
        var hasRelevant = false;
        foreach (var m in mechanics)
        {
            if (m.Type is not (TimelineEntryType.Ability or TimelineEntryType.Sync or TimelineEntryType.Cast))
            {
                hasRelevant = true;
                break;
            }
        }
        if (!hasRelevant)
            return;

        ImGui.Separator();

        var fightName = _timelineService.FightName;
        if (!string.IsNullOrEmpty(fightName))
            ImGui.TextDisabled(fightName);

        // Second pass: render up to 3 relevant mechanics
        var shown = 0;
        foreach (var m in mechanics)
        {
            if (shown >= 3) break;
            if (m.Type is TimelineEntryType.Ability or TimelineEntryType.Sync or TimelineEntryType.Cast)
                continue;

            DrawMechanicRow(m, dimName: shown > 0);
            shown++;
        }
    }

    /// <summary>
    /// BossMod (BMR) timeline forecast — fallback for any duty BMR has a module for
    /// when no embedded Cactbot timeline is loaded. BMR exposes timings only, so rows
    /// render as tag + countdown without a mechanic name.
    /// </summary>
    private void DrawBossModForecast()
    {
        if (_bossModForecast is not { IsAvailable: true, HasActiveModule: true })
            return;

        var mechanics = BossModForecastService.BuildForecast(
            _bossModForecast.NextRaidwideInSeconds,
            _bossModForecast.NextTankbusterInSeconds,
            ForecastWindowSeconds);
        if (mechanics.Count == 0)
            return;

        ImGui.Separator();

        var moduleName = _bossModForecast.ActiveModuleName;
        if (!string.IsNullOrEmpty(moduleName))
            ImGui.TextDisabled(moduleName);

        for (var i = 0; i < mechanics.Count; i++)
            DrawMechanicRow(mechanics[i], dimName: i > 0);
    }

    private void DrawMechanicRow(in MechanicPrediction m, bool dimName)
    {
        var timeColor = m.IsImminent ? MechanicImminentColor
                      : m.IsSoon    ? MechanicSoonColor
                      :               MechanicUpcomingColor;

        var (typeTag, typeColor) = m.Type switch
        {
            TimelineEntryType.TankBuster => ("TANKBUSTER", TankBusterLabelColor),
            TimelineEntryType.Raidwide   => ("RAIDWIDE", RaidwideLabelColor),
            TimelineEntryType.Enrage     => ("ENRAGE", MechanicImminentColor),
            TimelineEntryType.Phase      => ("PHASE", PhaseLabelColor),
            TimelineEntryType.Stack      => ("STACK", MechanicUpcomingColor),
            TimelineEntryType.Spread     => ("SPREAD", MechanicUpcomingColor),
            TimelineEntryType.Movement   => ("MOVE", MechanicUpcomingColor),
            TimelineEntryType.Adds       => ("ADDS", MechanicUpcomingColor),
            _                            => ("·", MechanicUpcomingColor),
        };

        ImGui.TextColored(typeColor, typeTag);
        ImGui.SameLine();
        ImGui.TextColored(timeColor, $"{m.SecondsUntil:F1}s");

        if (string.IsNullOrEmpty(m.Name))
            return;

        ImGui.SameLine();
        if (dimName)
            ImGui.TextColored(Common.DaedalusTheme.TextSecondary, m.Name);
        else
            ImGui.Text(m.Name);
    }

    private void DrawToggles()
    {
        DrawToggle(
            Loc.T(LocalizedStrings.Overlay.HealingToggle, "Healing"),
            _configuration.EnableHealing,
            v => _configuration.EnableHealing = v);

        DrawToggle(
            Loc.T(LocalizedStrings.Overlay.DamageToggle, "Damage"),
            _configuration.EnableDamage,
            v => _configuration.EnableDamage = v);

        DrawToggle(
            Loc.T(LocalizedStrings.Overlay.HardcastToggle, "Hardcast"),
            _configuration.Resurrection.AllowHardcastRaise,
            v => _configuration.Resurrection.AllowHardcastRaise = v);
    }

    private void DrawToggle(string label, bool value, Action<bool> set)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, value ? ActiveColor : InactiveColor);
        if (ImGui.Checkbox(label, ref value))
        {
            set(value);
            _saveConfiguration();
        }
        ImGui.PopStyleColor();
    }
}
