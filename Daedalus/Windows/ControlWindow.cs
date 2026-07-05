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
using Daedalus.Windows.Config;

namespace Daedalus.Windows;

/// <summary>
/// Quick-access control panel (styled like the main window) for toggling auto movement on the fly.
/// Exposes the global master kill-switch plus contextual per-job movement toggles for the active job.
/// </summary>
public sealed class ControlWindow : Window
{
    private readonly Configuration configuration;
    private readonly Action saveConfiguration;
    private readonly RotationManager rotationManager;
    private readonly ITextureProvider textureProvider;

    public ControlWindow(
        Configuration configuration,
        Action saveConfiguration,
        RotationManager rotationManager,
        ITextureProvider textureProvider)
        : base("Daedalus Control", ImGuiWindowFlags.NoCollapse)
    {
        this.configuration = configuration;
        this.saveConfiguration = saveConfiguration;
        this.rotationManager = rotationManager;
        this.textureProvider = textureProvider;

        Size = new Vector2(280, 220);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        DrawActiveRotationHeader();

        ImGui.Separator();

        ImGui.TextDisabled("Auto Movement");

        var autoMovement = configuration.EnableAutoMovement;
        if (ConfigUIHelpers.ToggleCheckbox(
                "Enable Auto Movement",
                ref autoMovement,
                "Master switch for all vNav movement (flank/rear repositioning, burst approach). "
                + "Flank/rear and burst movement are off when solo (no party); max-melee maintenance still runs.",
                saveConfiguration))
        {
            configuration.EnableAutoMovement = autoMovement;
        }

        // Max-melee maintenance applies to every melee job and (unlike positional/burst movement) also runs
        // solo, so surface it as a shared toggle whenever a melee rotation is active.
        if (rotationManager.ActiveRotation is IHasPositionals)
        {
            ImGui.BeginDisabled(!configuration.EnableAutoMovement);

            var maintainMaxMelee = configuration.MaintainMaxMelee;
            if (ConfigUIHelpers.ToggleCheckbox(
                    "Maintain Max Melee",
                    ref maintainMaxMelee,
                    "All melee jobs: step back to the outer melee edge when hugging the target. "
                    + "Pure range-keeping, so this also runs solo (only needs Enable Auto Movement).",
                    saveConfiguration))
            {
                configuration.MaintainMaxMelee = maintainMaxMelee;
            }

            ImGui.EndDisabled();
        }

        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "Solo (no party): flank/rear & burst movement disabled.");

        ImGui.Separator();

        DrawTankToggles();

        DrawJobMovementToggles();
    }

    private void DrawTankToggles()
    {
        var activeRotation = rotationManager.ActiveRotation;
        if (activeRotation == null)
            return;

        var jobId = activeRotation.SupportedJobIds[0];
        if (!JobRegistry.IsTank(jobId))
            return;

        ImGui.TextDisabled("Tank");

        var pullRanged = configuration.Tank.PullRangedMobsWithRangedAttack;
        if (ConfigUIHelpers.ToggleCheckbox(
                "Ranged Pull",
                ref pullRanged,
                "Pull out-of-melee mobs with the job's ranged GCD (Lightning Shot / Shield Lob / "
                + "Tomahawk / Unmend) and stay put instead of dashing in. Gap-closers still weave as "
                + "damage once in melee range.",
                saveConfiguration))
        {
            configuration.Tank.PullRangedMobsWithRangedAttack = pullRanged;
        }

        var transitFiller = configuration.Tank.TransitRangedFiller;
        if (ConfigUIHelpers.ToggleCheckbox(
                "Transit Ranged Filler",
                ref transitFiller,
                "During wall-to-wall transit (aggroed mobs chasing while you run), fire the ranged GCD "
                + "at the chasing pack instead of idling. Only hits enemies already in combat with you — "
                + "never pulls anything new.",
                saveConfiguration))
        {
            configuration.Tank.TransitRangedFiller = transitFiller;
        }

        var ignoreAdds = configuration.Tank.IgnoreAddsWithCoTank;
        if (ConfigUIHelpers.ToggleCheckbox(
                "Ignore Adds With Co-Tank",
                ref ignoreAdds,
                "When another tank is in the party, stick to your current target instead of auto-"
                + "acquiring loose adds. No effect solo / single-tank. Does not change Provoke or "
                + "tank-swap behavior.",
                saveConfiguration))
        {
            configuration.Tank.IgnoreAddsWithCoTank = ignoreAdds;
        }

        var noChaseLost = configuration.Tank.SuppressGapCloserOnLostMob;
        if (ConfigUIHelpers.ToggleCheckbox(
                "Don't Chase Lost Mobs",
                ref noChaseLost,
                "When a mob slips to another player, don't dash after it. Provoke (auto-fired) plus the "
                + "ranged GCD reclaim it in place so you stay on the pack. Initial dash-to-engage and "
                + "in-melee gap-closer weaving are unaffected.",
                saveConfiguration))
        {
            configuration.Tank.SuppressGapCloserOnLostMob = noChaseLost;
        }

        var tagAdds = configuration.Tank.TagAddsWhileMovingWithRangedAttack;
        if (ConfigUIHelpers.ToggleCheckbox(
                "Tag Adds While Moving",
                ref tagAdds,
                "While moving in a wall-to-wall pull, ranged-tag stray adds that aren't on you yet so none "
                + "are left behind. Only fires while moving and never interrupts a combo.",
                saveConfiguration))
        {
            configuration.Tank.TagAddsWhileMovingWithRangedAttack = tagAdds;
        }

        ImGui.Separator();
    }

    private void DrawActiveRotationHeader()
    {
        var statusColor = configuration.EffectiveEnabled
            ? new Vector4(0.0f, 1.0f, 0.0f, 1.0f)
            : new Vector4(1.0f, 0.0f, 0.0f, 1.0f);
        var statusText = configuration.EffectiveEnabled
            ? configuration.Enabled
                ? Loc.T(LocalizedStrings.Main.Active, "ACTIVE")
                : Loc.T(LocalizedStrings.Main.Active, "ACTIVE") + " (Auto)"
            : Loc.T(LocalizedStrings.Main.Inactive, "INACTIVE");

        ImGui.TextColored(statusColor, statusText);

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

            ImGui.TextColored(ConfigUIHelpers.AccentBlue, $"{activeRotation.Name} ({jobName})");
        }
        else
        {
            ImGui.TextDisabled(Loc.T(LocalizedStrings.Main.SwitchToSupported, "No rotation \u2014 switch to a supported job"));
        }
    }

    private void DrawJobMovementToggles()
    {
        var activeRotation = rotationManager.ActiveRotation;
        if (activeRotation == null)
        {
            ImGui.TextDisabled("No active job.");
            return;
        }

        var jobId = activeRotation.SupportedJobIds[0];
        var jobName = JobRegistry.GetJobName(jobId);

        ImGui.TextDisabled($"{jobName} Movement");

        // Per-job toggles only make sense while the master switch is on.
        ImGui.BeginDisabled(!configuration.EnableAutoMovement);

        switch (jobId)
        {
            case JobRegistry.Ninja:
            case JobRegistry.Rogue:
            {
                var positional = configuration.Ninja.EnablePositionalMovement;
                if (ConfigUIHelpers.ToggleCheckbox(
                        "Positional Reposition",
                        ref positional,
                        "Use vNav to move to the flank/rear before Aeolian Edge / Armor Crush.",
                        saveConfiguration))
                {
                    configuration.Ninja.EnablePositionalMovement = positional;
                }

                break;
            }

            case JobRegistry.Samurai:
            {
                var positional = configuration.Samurai.EnablePositionalMovement;
                if (ConfigUIHelpers.ToggleCheckbox(
                        "Positional Reposition",
                        ref positional,
                        "Use vNav to move to the flank/rear before Gekko / Kasha.",
                        saveConfiguration))
                {
                    configuration.Samurai.EnablePositionalMovement = positional;
                }

                break;
            }

            default:
                ImGui.TextDisabled("No quick movement options for this job.");
                break;
        }

        ImGui.EndDisabled();
    }
}
