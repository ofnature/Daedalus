using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Daedalus.Config;
using Daedalus.Data;
using Daedalus.Localization;
using Daedalus.Rotation;
using Daedalus.Rotation.Common;
using Daedalus.Windows.Common;

namespace Daedalus.Windows;

public sealed class MainWindow : Window
{
    private static readonly string[] PresetNames = Enum.GetNames<ConfigurationPreset>();

    private readonly Configuration configuration;
    private readonly Action saveConfiguration;
    private readonly Action openSettings;
    private readonly Action openDebug;
    private readonly Action openAnalytics;
    private readonly Action openTraining;
    private readonly Action openChangelog;
    private readonly Action openOverlay;
    private readonly Action openControl;
    private readonly Action openNavControl;
    private readonly Action openRaid;
    private readonly Action openMissing;
    private readonly string version;

    /// <summary>Opens the LAN party window. Null-tolerant: the button only renders when the LAN
    /// coordinator is enabled in Party Coordination settings.</summary>
    public Action? OpenLanParty { get; set; }

    /// <summary>True while the LAN coordinator has live peers — draws the presence dot on the
    /// LAN Party button. Optional; no dot when unset.</summary>
    public Func<bool>? LanConnected { get; set; }

    private readonly RotationManager rotationManager;
    private readonly ITextureProvider textureProvider;
    private readonly Daedalus.Services.ActionTracker? actionTracker;
    private readonly Daedalus.Services.Content.IDutyContentService? dutyContent;

    public MainWindow(
        Configuration configuration,
        Action saveConfiguration,
        Action openSettings,
        Action openDebug,
        Action openAnalytics,
        Action openTraining,
        Action openChangelog,
        Action openOverlay,
        Action openControl,
        Action openNavControl,
        Action openRaid,
        Action openMissing,
        string version,
        RotationManager rotationManager,
        ITextureProvider textureProvider,
        Daedalus.Services.ActionTracker? actionTracker = null,
        Daedalus.Services.Content.IDutyContentService? dutyContent = null)
        : base("Daedalus##Main", ImGuiWindowFlags.NoCollapse)
    {
        this.configuration = configuration;
        this.saveConfiguration = saveConfiguration;
        this.openSettings = openSettings;
        this.openDebug = openDebug;
        this.openAnalytics = openAnalytics;
        this.openTraining = openTraining;
        this.openChangelog = openChangelog;
        this.openOverlay = openOverlay;
        this.openControl = openControl;
        this.openNavControl = openNavControl;
        this.openRaid = openRaid;
        this.openMissing = openMissing;
        this.version = version;
        this.rotationManager = rotationManager;
        this.textureProvider = textureProvider;
        this.actionTracker = actionTracker;
        this.dutyContent = dutyContent;

        Size = new Vector2(288, 240);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        // Status row: state dot + active preset + last-fight GCD uptime (right-aligned)
        var statusColor = configuration.Enabled ? DaedalusTheme.StatusGreen : DaedalusTheme.StatusGrey;
        var statusText = configuration.Enabled
            ? Loc.T(LocalizedStrings.Main.Active, "Enabled")
            : Loc.T(LocalizedStrings.Main.Inactive, "Disabled");
        DaedalusTheme.StatusDot(statusColor, statusText);
        ImGui.SameLine();
        ImGui.TextDisabled(PresetNames[(int)configuration.ActivePreset]);

        if (actionTracker != null)
        {
            var uptime = actionTracker.GetGcdUptime();
            if (uptime > 0f)
            {
                var label = $"{uptime:F0}% uptime";
                ImGui.SameLine();
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(label).X);
                ImGui.TextColored(uptime >= 90f ? DaedalusTheme.StatusGreen : DaedalusTheme.TextSecondary, label);
            }
        }

        // Active rotation: job icon + codename in gold + job in secondary
        var activeRotation = rotationManager.ActiveRotation;
        if (activeRotation != null)
        {
            var activeJobId = activeRotation.SupportedJobIds[0];
            var jobName = JobRegistry.GetJobName(activeJobId);

            var iconId = JobRegistry.GetJobIconId(activeJobId);
            if (iconId != 0)
            {
                var wrap = textureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
                ImGui.Image(wrap.Handle, new Vector2(20, 20));
                ImGui.SameLine();
            }

            ImGui.TextColored(DaedalusTheme.AccentGold, activeRotation.Name);
            ImGui.SameLine();
            ImGui.TextColored(DaedalusTheme.TextSecondary, $"({jobName})");
        }
        else
        {
            ImGui.TextDisabled(Loc.T(LocalizedStrings.Main.SwitchToSupported, "No rotation — switch to a supported job"));
        }

        // Duty context line (open world shows nothing)
        var dutyLabel = dutyContent?.DutyLabel;
        if (!string.IsNullOrEmpty(dutyLabel))
            ImGui.TextDisabled(dutyLabel);

        // Positional indicator — only shown for melee DPS jobs with an active target
        if (activeRotation is IHasPositionals posRotation)
        {
            PositionalDisplayHelper.DrawMainWindow(posRotation.Positionals);
        }

        ImGui.Spacing();

        // The master switch — the one gold-filled control in the plugin.
        var enableDisableText = configuration.Enabled
            ? Loc.T(LocalizedStrings.Main.Disable, "Disable")
            : Loc.T(LocalizedStrings.Main.Enable, "Enable");

        ImGui.PushStyleColor(ImGuiCol.Button, DaedalusTheme.AccentGold);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.95f, 0.75f, 0.30f, 1.00f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, DaedalusTheme.AccentDim);
        ImGui.PushStyleColor(ImGuiCol.Text, DaedalusTheme.BgDeep);
        if (ImGui.Button(enableDisableText, new Vector2(-1, 28)))
        {
            configuration.Enabled = !configuration.Enabled;
            saveConfiguration();
        }
        ImGui.PopStyleColor(4);

        ImGui.TextColored(DaedalusTheme.TextSecondary, Loc.T(LocalizedStrings.Main.Preset, "Preset"));
        ImGui.SameLine();
        var currentPreset = (int)configuration.ActivePreset;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##MainPresetCombo", ref currentPreset, PresetNames, PresetNames.Length))
        {
            var selected = (ConfigurationPreset)currentPreset;
            if (selected != ConfigurationPreset.Custom)
            {
                ConfigurationPresets.ApplyPreset(configuration, selected);
                saveConfiguration();
            }
            else
            {
                configuration.ActivePreset = ConfigurationPreset.Custom;
                saveConfiguration();
            }
        }

        ImGui.Separator();

        // Navigation grid
        var buttonWidth = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2;

        if (ImGui.Button(Loc.T(LocalizedStrings.Main.Settings, "Settings"), new Vector2(buttonWidth, 0)))
        {
            openSettings();
        }
        ImGui.SameLine();
        if (ImGui.Button(Loc.T(LocalizedStrings.Main.Overlay, "Overlay"), new Vector2(buttonWidth, 0)))
        {
            openOverlay();
        }

        if (ImGui.Button(Loc.T(LocalizedStrings.Main.Analytics, "Analytics"), new Vector2(buttonWidth, 0)))
        {
            openAnalytics();
        }
        ImGui.SameLine();
        if (ImGui.Button(Loc.T(LocalizedStrings.Main.Training, "Training"), new Vector2(buttonWidth, 0)))
        {
            openTraining();
        }

        if (ImGui.Button(Loc.T(LocalizedStrings.Main.Control, "Control"), new Vector2(buttonWidth, 0)))
        {
            openControl();
        }
        ImGui.SameLine();
        if (ImGui.Button(Loc.T(LocalizedStrings.Main.NavControl, "Nav Control"), new Vector2(buttonWidth, 0)))
        {
            openNavControl();
        }

        if (ImGui.Button(Loc.T(LocalizedStrings.Main.Raid, "Raid"), new Vector2(buttonWidth, 0)))
        {
            openRaid();
        }
        // LAN party window — only offered while the LAN coordinator is enabled (Party Coordination
        // settings); a small green dot marks live peer presence.
        if (configuration.PartyCoordination.LanCoordinatorEnabled && OpenLanParty != null)
        {
            ImGui.SameLine();
            if (ImGui.Button("LAN Party", new Vector2(buttonWidth, 0)))
            {
                OpenLanParty();
            }
            if (LanConnected?.Invoke() == true)
            {
                var max = ImGui.GetItemRectMax();
                var min = ImGui.GetItemRectMin();
                ImGui.GetWindowDrawList().AddCircleFilled(
                    new Vector2(max.X - 7, min.Y + 7), 3f,
                    ImGui.ColorConvertFloat4ToU32(DaedalusTheme.StatusGreen));
            }
        }

        // Footer links + version
        if (ImGui.SmallButton(Loc.T(LocalizedStrings.Main.Changelog, "Changelog")))
        {
            openChangelog();
        }
        ImGui.SameLine();
        ImGui.TextDisabled("·");
        ImGui.SameLine();
        if (ImGui.SmallButton(Loc.T(LocalizedStrings.Main.Debug, "Debug")))
        {
            openDebug();
        }
        ImGui.SameLine();
        ImGui.TextDisabled("·");
        ImGui.SameLine();
        if (ImGui.SmallButton(Loc.T(LocalizedStrings.Main.Missing, "Missing")))
        {
            openMissing();
        }
        var versionLabel = $"v{version}";
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(versionLabel).X);
        ImGui.TextDisabled(versionLabel);
    }
}
