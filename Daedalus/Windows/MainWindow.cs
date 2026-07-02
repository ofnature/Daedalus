using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Daedalus.Config;
using Daedalus.Data;
using Daedalus.Windows.Config;
using Daedalus.Localization;
using Daedalus.Rotation;
using Daedalus.Rotation.Common;

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

    /// <summary>Opens the LAN party window. Null-tolerant: the button only renders when the LAN
    /// coordinator is enabled in Party Coordination settings.</summary>
    public Action? OpenLanParty { get; set; }
    private readonly RotationManager rotationManager;
    private readonly ITextureProvider textureProvider;

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
        ITextureProvider textureProvider)
        : base($"Daedalus v{version}", ImGuiWindowFlags.NoCollapse)
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
        this.rotationManager = rotationManager;
        this.textureProvider = textureProvider;

        Size = new Vector2(250, 200);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var statusColor = configuration.Enabled
            ? new Vector4(0.0f, 1.0f, 0.0f, 1.0f)
            : new Vector4(1.0f, 0.0f, 0.0f, 1.0f);
        var statusText = configuration.Enabled
            ? Loc.T(LocalizedStrings.Main.Active, "ACTIVE")
            : Loc.T(LocalizedStrings.Main.Inactive, "INACTIVE");

        ImGui.TextColored(statusColor, statusText);
        ImGui.SameLine();
        ImGui.TextDisabled(PresetNames[(int)configuration.ActivePreset]);

        // Show active rotation
        var activeRotation = rotationManager.ActiveRotation;
        if (activeRotation != null)
        {
            var activeJobId = activeRotation.SupportedJobIds[0];
            var jobName = JobRegistry.GetJobName(activeJobId);
            var activeColor = ConfigUIHelpers.AccentBlue;

            var iconId = JobRegistry.GetJobIconId(activeJobId);
            if (iconId != 0)
            {
                var wrap = textureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
                ImGui.Image(wrap.Handle, new Vector2(20, 20));
                ImGui.SameLine();
            }

            ImGui.TextColored(activeColor, $"{activeRotation.Name} ({jobName})");
        }
        else
        {
            ImGui.TextDisabled(Loc.T(LocalizedStrings.Main.SwitchToSupported, "No rotation \u2014 switch to a supported job"));
        }

        // Positional indicator — only shown for melee DPS jobs with an active target
        if (activeRotation is IHasPositionals posRotation)
        {
            PositionalDisplayHelper.DrawMainWindow(posRotation.Positionals);
        }

        ImGui.Separator();

        var enableDisableText = configuration.Enabled
            ? Loc.T(LocalizedStrings.Main.Disable, "Disable")
            : Loc.T(LocalizedStrings.Main.Enable, "Enable");

        if (ImGui.Button(enableDisableText, new Vector2(-1, 28)))
        {
            configuration.Enabled = !configuration.Enabled;
            saveConfiguration();
        }

        ImGui.Text(Loc.T(LocalizedStrings.Main.Preset, "Preset:"));
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
        // settings); the window itself hosts the cross-machine roster/network UI.
        if (configuration.PartyCoordination.LanCoordinatorEnabled && OpenLanParty != null)
        {
            ImGui.SameLine();
            if (ImGui.Button("LAN Party", new Vector2(buttonWidth, 0)))
            {
                OpenLanParty();
            }
        }

        // Footer links
        if (ImGui.SmallButton(Loc.T(LocalizedStrings.Main.Changelog, "Changelog")))
        {
            openChangelog();
        }
        ImGui.SameLine();
        ImGui.Text("\u00B7");
        ImGui.SameLine();
        if (ImGui.SmallButton(Loc.T(LocalizedStrings.Main.Debug, "Debug")))
        {
            openDebug();
        }
        ImGui.SameLine();
        ImGui.Text("\u00B7");
        ImGui.SameLine();
        if (ImGui.SmallButton(Loc.T(LocalizedStrings.Main.Missing, "Missing")))
        {
            openMissing();
        }
    }
}
