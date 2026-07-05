using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Daedalus.Services.Positional.Navigation;
using Daedalus.Windows.Config;

namespace Daedalus.Windows;

/// <summary>
/// Global navigation control panel. All vNav / max-melee tuning lives here in one place (not per-job):
/// the vNav Flex grace band, solo position lock, debug rings, and the tank-feature stubs.
/// </summary>
public sealed class NavControlWindow : Window
{
    private readonly Configuration configuration;
    private readonly Action saveConfiguration;
    private readonly BmrAiConfigService bmrAiConfigService;
    private readonly IMovementArbiter? movementArbiter;

    public NavControlWindow(
        Configuration configuration,
        Action saveConfiguration,
        BmrAiConfigService bmrAiConfigService,
        IMovementArbiter? movementArbiter = null)
        : base("Nav Control", ImGuiWindowFlags.NoCollapse)
    {
        this.configuration = configuration;
        this.saveConfiguration = saveConfiguration;
        this.bmrAiConfigService = bmrAiConfigService;
        this.movementArbiter = movementArbiter;

        Size = new Vector2(340, 280);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var nav = configuration.Nav;

        ImGui.TextDisabled("Max Melee Positioning");
        ImGui.Separator();

        nav.VNavFlex = ConfigUIHelpers.FloatSlider(
            "vNav Flex (yalms)",
            nav.VNavFlex,
            0.0f,
            2.0f,
            "%.1f",
            "Grace dead-band around max melee before vNav is called to reposition. Larger = fewer, lazier "
            + "moves (less twitching); smaller = tighter range-keeping.",
            saveConfiguration);

        var camping = nav.EnableBoundaryCamping;
        if (ConfigUIHelpers.ToggleCheckbox(
                "Boundary Camping (experimental)",
                ref camping,
                "Melee stands just inside the required flank/rear zone next to the 135° boundary, so a "
                + "positional swap is a short hop instead of a quarter-circle run. With Auto-Manage BMR "
                + "AI on, BossMod is told 'any positional' while this is active (it keeps range, Daedalus "
                + "owns the angle). OFF by default while the movement cadence is being validated.",
                saveConfiguration))
        {
            nav.EnableBoundaryCamping = camping;
        }

        if (nav.EnableBoundaryCamping)
        {
            ImGui.Indent();
            nav.PositionalBoundaryBiasDegrees = ConfigUIHelpers.FloatSlider(
                "Positional Boundary Bias (deg)",
                nav.PositionalBoundaryBiasDegrees,
                0.0f,
                30.0f,
                "%.0f",
                "How many degrees inside the required arc to stand from the flank/rear boundary. "
                + "0 = stand at arc centers (old behavior).",
                saveConfiguration);
            ImGui.Unindent();
        }

        var soloLock = nav.SoloPositionLock;
        if (ConfigUIHelpers.ToggleCheckbox(
                "Solo Position Lock",
                ref soloLock,
                "Disable max-melee positioning when solo (in a solo duty or with no party members).",
                saveConfiguration))
        {
            nav.SoloPositionLock = soloLock;
        }

        var rings = nav.MaxMeleeDebugRings;
        if (ConfigUIHelpers.ToggleCheckbox(
                "Max Melee Debug Rings",
                ref rings,
                "Draw the enemy-hitbox / combined / max-melee rings (and the vNav Flex grace band) around "
                + "the current target.",
                saveConfiguration))
        {
            nav.MaxMeleeDebugRings = rings;
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Movement Cadence");
        ImGui.Separator();

        var yield = nav.YieldToBmrMovement;
        if (ConfigUIHelpers.ToggleCheckbox(
                "Yield movement to BossMod",
                ref yield,
                "Pause Daedalus pathing while BossMod is dodging or danger zones are up, and for a short "
                + "cooldown after they clear. BMR steers by input injection and defers to vNav whenever a "
                + "path runs — without this the two systems fight and stutter the screen. With Auto-Manage "
                + "BMR AI on, BMR owns positioning near-full-time and Daedalus movement stays quiet. Does "
                + "nothing when BossMod isn't loaded. Leave ON.",
                saveConfiguration))
        {
            nav.YieldToBmrMovement = yield;
        }

        DrawArbiterStatus();

        ImGui.Spacing();
        ImGui.TextDisabled("Auto-Manage BossMod AI (groups — experimental)");
        ImGui.Separator();

        var autoBmr = nav.AutoManageBmrAi;
        if (ConfigUIHelpers.ToggleCheckbox(
                "Auto-Manage BMR AI by role",
                ref autoBmr,
                "For group content (not Trust): feeds BossMod Reborn's AI a role-based stand distance "
                + "(healers/ranged hold at range, melee hug) and the live next-GCD positional, in movement-only "
                + "mode so BMR positions while Daedalus keeps the rotation. You still enable BMR AI yourself "
                + "(/bmrai). Does nothing if BossMod Reborn isn't loaded. Off by default.",
                saveConfiguration))
        {
            nav.AutoManageBmrAi = autoBmr;
        }

        if (nav.AutoManageBmrAi)
        {
            nav.BmrRangedStandDistance = ConfigUIHelpers.FloatSlider(
                "Ranged Stand Distance (yalms)",
                nav.BmrRangedStandDistance,
                8f,
                24f,
                "%.0f",
                "How far healers/ranged/casters stand from the target. 15y is inside cast range but out of "
                + "most melee/AoE. Melee always hug (2.6y).",
                saveConfiguration);

            DrawBmrStatus();
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Tank (experimental)");
        ImGui.Separator();

        var addPull = nav.AddPull;
        if (ConfigUIHelpers.ToggleCheckbox(
                "Add Pull",
                ref addPull,
                "Ranged-pull adds to the camp. Stub \u2014 the toggle is saved but the behavior is not wired yet.",
                saveConfiguration))
        {
            nav.AddPull = addPull;
        }

        var tankMode = nav.TankMode;
        if (ConfigUIHelpers.ToggleCheckbox(
                "Tank Mode",
                ref tankMode,
                "Toggle boss-anchor vs add-puller mode. Stub \u2014 the toggle is saved but the behavior is not "
                + "wired yet.",
                saveConfiguration))
        {
            nav.TankMode = tankMode;
        }
    }

    private static readonly Vector4 Green = new(0.40f, 0.85f, 0.40f, 1f);
    private static readonly Vector4 Yellow = new(0.95f, 0.80f, 0.30f, 1f);
    private static readonly Vector4 Red = new(0.90f, 0.45f, 0.45f, 1f);

    /// <summary>
    /// Live diagnostics for the BMR push — without this it's a black box, since BMR can't be exercised in
    /// tests. Shows whether BMR is loaded, whether a preset is blocking our config, and the last IPC result.
    /// </summary>
    private void DrawBmrStatus()
    {
        ImGui.Spacing();
        ImGui.Indent();

        if (!bmrAiConfigService.BmrAvailable)
        {
            ImGui.TextColored(Red, "BossMod Reborn: not loaded");
            ImGui.Unindent();
            return;
        }

        ImGui.TextColored(Green, "BossMod Reborn: loaded");

        var preset = bmrAiConfigService.CurrentAiPreset();
        if (string.IsNullOrEmpty(preset))
        {
            ImGui.TextColored(Green, "AI preset: none (movement config active)");
        }
        else
        {
            ImGui.TextColored(Yellow, $"AI preset loaded: {preset}");
            ImGui.TextWrapped("A loaded AI preset does its own positioning and ignores Daedalus's distance/"
                + "positional. Clear it in BMR's AI window for this to control movement.");
        }

        var result = bmrAiConfigService.LastPushResult;
        if (!string.IsNullOrEmpty(result))
        {
            var ok = result.EndsWith(": ok", StringComparison.Ordinal);
            ImGui.TextColored(ok ? Green : Yellow, $"Last push: {result}");
        }

        ImGui.TextDisabled("Enable BMR AI itself with /bmrai.");
        ImGui.Unindent();
    }

    /// <summary>Two-line live view of the movement arbiter: who owns steering, and why we're held back.</summary>
    private void DrawArbiterStatus()
    {
        if (movementArbiter is not { } arbiter)
            return;

        var snap = arbiter.Snapshot;

        ImGui.Indent();

        var (ownerText, ownerColor) = snap.Owner switch
        {
            MovementOwner.BossMod => ("BossMod (dodging / danger)", Yellow),
            MovementOwner.Daedalus => (
                snap.LastGrantIntent == MovementIntent.PositionalArc
                    ? "Daedalus (positional arc)"
                    : "Daedalus (vNav path running)", Green),
            _ => ("idle", Green),
        };
        ImGui.TextColored(ownerColor, $"Movement owner: {ownerText}");

        var suppressionText = snap.Suppression switch
        {
            MovementSuppression.BmrDanger =>
                $"held: BMR danger ({snap.ForbiddenZonesCount} zones)",
            MovementSuppression.BmrNavigating => "held: BMR AI steering",
            MovementSuppression.RegrabCooldown =>
                $"held: re-grab cooldown {snap.RegrabCooldownRemainingSeconds:0.0}s",
            MovementSuppression.RepathInterval => "held: re-path interval",
            MovementSuppression.PathCommitment => "held: path commitment",
            MovementSuppression.DestinationDelta => "held: destination unchanged",
            _ => "free",
        };
        ImGui.TextColored(snap.Suppression == MovementSuppression.None ? Green : Yellow,
            $"Daedalus pathing: {suppressionText}");

        ImGui.Unindent();
    }
}
