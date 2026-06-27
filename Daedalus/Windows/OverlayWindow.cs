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

    private static readonly Vector4 ActiveColor    = new(0.4f, 0.9f, 0.4f, 1f);
    private static readonly Vector4 InactiveColor  = new(0.5f, 0.5f, 0.5f, 1f);
    private static readonly Vector4 ActionColor    = new(0.5f, 1.0f, 0.5f, 1f);
    private static readonly Vector4 AlertColor     = new(1.0f, 0.65f, 0.2f, 1f);
    private static readonly Vector4 HpHighColor    = new(0.5f, 1.0f, 0.5f, 1f);
    private static readonly Vector4 HpMidColor     = new(1.0f, 1.0f, 0.4f, 1f);
    private static readonly Vector4 HpLowColor     = new(1.0f, 0.4f, 0.4f, 1f);
    private static readonly Vector4 MechanicImminentColor   = new(1.0f, 0.3f, 0.3f, 1f);
    private static readonly Vector4 MechanicSoonColor       = new(1.0f, 0.65f, 0.2f, 1f);
    private static readonly Vector4 MechanicUpcomingColor   = new(0.85f, 0.85f, 0.85f, 1f);
    private static readonly Vector4 RaidwideLabelColor      = new(1.0f, 0.5f, 0.5f, 1f);
    private static readonly Vector4 TankBusterLabelColor    = new(0.5f, 0.8f, 1.0f, 1f);
    private static readonly Vector4 PhaseLabelColor         = new(0.7f, 0.9f, 0.5f, 1f);

    public OverlayWindow(
        Configuration configuration,
        Action saveConfiguration,
        RotationManager rotationManager,
        IPartyList partyList,
        ITimelineService? timelineService = null,
        IDutyContentService? dutyContentService = null)
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

        Position = new Vector2(configuration.Overlay.X, configuration.Overlay.Y);
        PositionCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        ImGui.Dummy(new Vector2(200, 0));

        var rotation = _rotationManager.ActiveRotation;

        DrawHeader(rotation);
        ImGui.Separator();
        DrawStatus();

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

    private void DrawHeader(IRotation? rotation)
    {
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
    }

    private void DrawStatus()
    {
        var isActive   = _configuration.Enabled;
        var color      = isActive ? ActiveColor : InactiveColor;
        var statusText = isActive
            ? Loc.T(LocalizedStrings.Overlay.StatusActive,   "ACTIVE")
            : Loc.T(LocalizedStrings.Overlay.StatusInactive, "INACTIVE");

        ImGui.PushStyleColor(ImGuiCol.Text, color);
        if (ImGui.SmallButton($"● {statusText}##OverlayToggle"))
        {
            _configuration.Enabled = !_configuration.Enabled;
            _saveConfiguration();
        }
        ImGui.PopStyleColor();

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
        // Player HP bar
        if (state.PlayerHpPercent > 0f)
        {
            var hpColor = state.PlayerHpPercent < 0.3f ? HpLowColor
                        : state.PlayerHpPercent < 0.7f ? HpMidColor
                        : HpHighColor;
            ImGui.Text(Loc.T(LocalizedStrings.Overlay.HpLabel, "HP:"));
            ImGui.SameLine();
            ImGui.TextColored(hpColor, $"{state.PlayerHpPercent:P0}");
        }

        // Injured party members
        if (state.AoEInjuredCount > 0)
        {
            ImGui.Text(Loc.T(LocalizedStrings.Overlay.PartyLabel, "Party:"));
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
        if (_timelineService is not { IsActive: true })
            return;

        var mechanics = _timelineService.GetUpcomingMechanics(60f);
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

            var timeColor = m.IsImminent ? MechanicImminentColor
                          : m.IsSoon    ? MechanicSoonColor
                          :               MechanicUpcomingColor;

            var (typeTag, typeColor) = m.Type switch
            {
                TimelineEntryType.TankBuster => ("TB", TankBusterLabelColor),
                TimelineEntryType.Raidwide   => ("RW", RaidwideLabelColor),
                TimelineEntryType.Enrage     => ("!!", MechanicImminentColor),
                TimelineEntryType.Phase      => (">>", PhaseLabelColor),
                TimelineEntryType.Stack      => ("ST", MechanicUpcomingColor),
                TimelineEntryType.Spread     => ("SP", MechanicUpcomingColor),
                TimelineEntryType.Movement   => ("MV", MechanicUpcomingColor),
                TimelineEntryType.Adds       => ("AD", MechanicUpcomingColor),
                _                            => ("  ", MechanicUpcomingColor),
            };

            ImGui.TextColored(typeColor, typeTag);
            ImGui.SameLine();
            ImGui.TextColored(timeColor, $"{m.SecondsUntil:F1}s");
            ImGui.SameLine();
            ImGui.Text(m.Name);

            shown++;
        }
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
