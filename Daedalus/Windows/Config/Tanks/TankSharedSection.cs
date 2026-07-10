using System;
using Dalamud.Bindings.ImGui;
using Daedalus.Config;

namespace Daedalus.Windows.Config.Tanks;

/// <summary>
/// Settings shared by all tanks: coordinated tank swaps, Provoke/Shirk automation, and the
/// defensive-cooldown stagger windows (which previously had no UI anywhere).
/// </summary>
public sealed class TankSharedSection
{
    private static readonly string[] RoleNames = ["Auto", "Main tank", "Off-tank"];

    private readonly Configuration config;
    private readonly Action save;

    public TankSharedSection(Configuration config, Action save)
    {
        this.config = config;
        this.save = save;
    }

    public void Draw()
    {
        ImGui.TextColored(new System.Numerics.Vector4(0.85f, 0.65f, 0.20f, 1f), "Tank Coordination");
        ImGui.TextDisabled("Shared tank settings — coordinated swaps and threat automation.");
        ConfigUIHelpers.Spacing();

        if (ConfigUIHelpers.SectionHeader("Tank Swap", "TankShared"))
        {
            ConfigUIHelpers.BeginIndent();

            // Per-toon role, the healer Primary/Secondary analog: set MT on the main tank's box and
            // OT on the off-tank's box; Auto defers to the LAN window's off-tank picker.
            var role = (int)config.PartyCoordination.PreferredTankRole;
            ImGui.SetNextItemWidth(160);
            if (ImGui.Combo("This toon's tank role", ref role, RoleNames, RoleNames.Length))
            {
                config.PartyCoordination.PreferredTankRole = (TankRolePreference)role;
                save();
            }
            ImGui.TextDisabled(
                "Main tank: never moved off the boss by swaps; handles emergency Provoke.\n"
                + "Off-tank: takes aggro only through the coordinated swap handshake.\n"
                + "Auto: follow the off-tank picker in the Party Coordination window.");
            ConfigUIHelpers.Spacing();

            ConfigUIHelpers.Toggle(
                "Enable coordinated tank swaps",
                () => config.Tank.EnableTankSwap,
                v => config.Tank.EnableTankSwap = v,
                "Two Daedalus tanks hand the boss off cleanly: the incoming tank pre-mitigates and "
                + "Provokes only after the current tank confirms, then the current tank Shirks once the "
                + "boss flips. Off by default — enable on both tanks. Use the 'Swap tanks' button in the "
                + "Party Coordination window to swap on demand.",
                save);

            if (config.Tank.EnableTankSwap)
            {
                ConfigUIHelpers.Toggle(
                    "Pre-swap mitigation",
                    () => config.Tank.PreSwapMitigation,
                    v => config.Tank.PreSwapMitigation = v,
                    "The incoming tank pops a personal mitigation just before taking aggro, so it eats "
                    + "the first hit with a cooldown up.",
                    save);

                ConfigUIHelpers.Toggle(
                    "Auto-swap on buster stacks",
                    () => config.Tank.AutoTankSwap,
                    v => config.Tank.AutoTankSwap = v,
                    "Automatically start a swap when the current main tank reaches the stack count below. "
                    + "The manual 'Swap tanks' button always works regardless of this setting.",
                    save);

                if (config.Tank.AutoTankSwap)
                {
                    config.Tank.TankSwapStackCount = ConfigUIHelpers.IntSlider(
                        "Swap at stack count",
                        config.Tank.TankSwapStackCount, 1, 8,
                        "Number of stacks on the main tank that triggers an automatic swap.",
                        save, v => config.Tank.TankSwapStackCount = v);
                }
            }

            ConfigUIHelpers.EndIndent();
        }

        if (ConfigUIHelpers.SectionHeader("Threat (Provoke / Shirk)", "TankShared"))
        {
            ConfigUIHelpers.BeginIndent();

            ConfigUIHelpers.Toggle(
                "Auto Provoke",
                () => config.Tank.AutoProvoke,
                v => config.Tank.AutoProvoke = v,
                "Reclaim the boss with Provoke when it slips to a non-tank. Coordinated swaps use their "
                + "own handshake and are not affected by this.",
                save);

            if (config.Tank.AutoProvoke)
            {
                config.Tank.ProvokeDelay = ConfigUIHelpers.ThresholdSliderSmall(
                    "Provoke delay (seconds)",
                    config.Tank.ProvokeDelay, 0f, 5f,
                    "Wait this long after losing aggro before Provoking, so intended swaps aren't fought.",
                    save, v => config.Tank.ProvokeDelay = v);
            }

            ConfigUIHelpers.Toggle(
                "Auto Shirk (proactive off-tank)",
                () => config.Tank.AutoShirk,
                v => config.Tank.AutoShirk = v,
                "As the off-tank, periodically Shirk to stay comfortably below the main tank. Coordinated "
                + "swaps Shirk on their own regardless of this setting.",
                save);

            ConfigUIHelpers.EndIndent();
        }

        if (ConfigUIHelpers.SectionHeader("Defensive Coordination", "TankShared"))
        {
            ConfigUIHelpers.BeginIndent();

            ConfigUIHelpers.Toggle(
                "Stagger personal defensives with co-tank",
                () => config.Tank.EnableDefensiveCoordination,
                v => config.Tank.EnableDefensiveCoordination = v,
                "Avoid both tanks blowing personal mitigation on the same hit — hold ours briefly if the "
                + "co-tank just used one.",
                save);

            if (config.Tank.EnableDefensiveCoordination)
            {
                config.Tank.DefensiveStaggerWindowSeconds = ConfigUIHelpers.ThresholdSliderSmall(
                    "Defensive stagger window (seconds)",
                    config.Tank.DefensiveStaggerWindowSeconds, 1f, 10f,
                    null, save, v => config.Tank.DefensiveStaggerWindowSeconds = v);
            }

            ConfigUIHelpers.Toggle(
                "Stagger invulnerabilities with co-tank",
                () => config.Tank.EnableInvulnerabilityCoordination,
                v => config.Tank.EnableInvulnerabilityCoordination = v,
                "Don't overlap tank invulnerabilities (Hallowed Ground / Holmgang / Living Dead / "
                + "Superbolide) — space them across the co-tank's.",
                save);

            if (config.Tank.EnableInvulnerabilityCoordination)
            {
                config.Tank.InvulnerabilityStaggerWindowSeconds = ConfigUIHelpers.ThresholdSliderSmall(
                    "Invuln stagger window (seconds)",
                    config.Tank.InvulnerabilityStaggerWindowSeconds, 1f, 10f,
                    null, save, v => config.Tank.InvulnerabilityStaggerWindowSeconds = v);
            }

            ConfigUIHelpers.EndIndent();
        }
    }
}
