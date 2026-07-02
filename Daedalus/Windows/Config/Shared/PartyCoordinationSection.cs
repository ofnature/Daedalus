using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Daedalus.Localization;

namespace Daedalus.Windows.Config.Shared;

/// <summary>
/// Renders the Party Coordination settings section.
/// </summary>
public sealed class PartyCoordinationSection
{
    private readonly Configuration config;
    private readonly Action save;

    public PartyCoordinationSection(Configuration config, Action save)
    {
        this.config = config;
        this.save = save;
    }

    public void Draw()
    {
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 0.9f, 1.0f), Loc.T(LocalizedStrings.PartyCoordination.SectionTitle, "Party Coordination Settings"));
        ImGui.Separator();

        ConfigUIHelpers.Toggle(
            Loc.T(LocalizedStrings.PartyCoordination.EnablePartyCoordination, "Enable Party Coordination"),
            () => config.PartyCoordination.EnablePartyCoordination,
            v => config.PartyCoordination.EnablePartyCoordination = v,
            Loc.T(LocalizedStrings.PartyCoordination.EnablePartyCoordinationDesc,
                "Coordinate heals and cooldowns with other Daedalus users in your party. Changes take effect on next plugin reload."),
            save);

        DrawLanSection();

        if (!config.PartyCoordination.EnablePartyCoordination)
            return;

        DrawCoordinationSection();
        DrawConnectionSection();
    }

    /// <summary>
    /// Live LAN status for the settings UI. Set by Plugin after the coordinator is constructed;
    /// static because this section is built inside ConfigWindow before the LAN stack exists.
    /// </summary>
    public static Func<(string Status, string Detail)>? LanStatusSource;

    private void DrawLanSection()
    {
        if (!ConfigUIHelpers.SectionHeader("LAN Coordinator (Cross-Machine)", "PartyCoordLan"))
            return;

        ConfigUIHelpers.BeginIndent();

        ConfigUIHelpers.Toggle(
            "Enable LAN Coordinator",
            () => config.PartyCoordination.LanCoordinatorEnabled,
            v => config.PartyCoordination.LanCoordinatorEnabled = v,
            "Coordinate with Daedalus instances on OTHER machines via UDP broadcast (same VLAN). " +
            "Same-machine toons keep using Dalamud IPC; messages are deduplicated automatically. " +
            "Requires a Windows Firewall inbound rule for the UDP port on every machine. " +
            "Takes effect on next plugin reload.",
            save);

        var port = config.PartyCoordination.LanPort;
        if (ImGui.InputInt("LAN Port", ref port))
        {
            config.PartyCoordination.LanPort = port;
            save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("UDP broadcast port — must match on all machines. Default 47200.");

        var machineId = config.PartyCoordination.LanMachineId;
        ImGui.TextDisabled($"Machine ID: {(machineId.Length > 0 ? machineId : "(generated on first enable)")}");

        if (LanStatusSource != null)
        {
            var (status, detail) = LanStatusSource();
            ImGui.Text("Status:");
            ImGui.SameLine();
            var color = status switch
            {
                "Connected" => new Vector4(0.3f, 0.9f, 0.3f, 1f),
                "Error" => new Vector4(1f, 0.35f, 0.35f, 1f),
                _ => new Vector4(0.62f, 0.62f, 0.62f, 1f),
            };
            ImGui.TextColored(color, detail.Length > 0 ? $"{status} — {detail}" : status);
        }
        else if (config.PartyCoordination.LanCoordinatorEnabled)
        {
            ImGui.TextDisabled("Status: starts on next plugin reload");
        }

        ConfigUIHelpers.EndIndent();
    }

    private void DrawCoordinationSection()
    {
        if (ConfigUIHelpers.SectionHeader(Loc.T(LocalizedStrings.PartyCoordination.CoordinationSection, "Coordination"), "PartyCoordCoord"))
        {
            ConfigUIHelpers.BeginIndent();

            ConfigUIHelpers.Toggle(
                Loc.T(LocalizedStrings.PartyCoordination.EnableCooldownCoordination, "Enable Cooldown Coordination"),
                () => config.PartyCoordination.EnableCooldownCoordination,
                v => config.PartyCoordination.EnableCooldownCoordination = v,
                Loc.T(LocalizedStrings.PartyCoordination.EnableCooldownCoordinationDesc,
                    "Prevent stacking defensive cooldowns with other Daedalus users"),
                save);

            ConfigUIHelpers.Toggle(
                Loc.T(LocalizedStrings.PartyCoordination.BroadcastMajorCooldowns, "Broadcast Major Cooldowns"),
                () => config.PartyCoordination.BroadcastMajorCooldowns,
                v => config.PartyCoordination.BroadcastMajorCooldowns = v,
                Loc.T(LocalizedStrings.PartyCoordination.BroadcastMajorCooldownsDesc,
                    "Announce when you use major cooldowns so other instances can coordinate"),
                save);

            ConfigUIHelpers.Toggle(
                Loc.T(LocalizedStrings.PartyCoordination.EnableAoEHealCoordination, "Enable AoE Heal Coordination"),
                () => config.PartyCoordination.EnableAoEHealCoordination,
                v => config.PartyCoordination.EnableAoEHealCoordination = v,
                Loc.T(LocalizedStrings.PartyCoordination.EnableAoEHealCoordinationDesc,
                    "Prevent multiple healers casting party-wide heals simultaneously"),
                save);

            ConfigUIHelpers.EndIndent();
        }
    }

    private void DrawConnectionSection()
    {
        if (ConfigUIHelpers.SectionHeader(Loc.T(LocalizedStrings.PartyCoordination.ConnectionSection, "Connection (Advanced)"), "PartyCoordConn", false))
        {
            ConfigUIHelpers.BeginIndent();

            config.PartyCoordination.HeartbeatIntervalMs = ConfigUIHelpers.IntSlider(
                Loc.T(LocalizedStrings.PartyCoordination.HeartbeatInterval, "Heartbeat Interval (ms)"),
                config.PartyCoordination.HeartbeatIntervalMs, 500, 5000,
                Loc.T(LocalizedStrings.PartyCoordination.HeartbeatIntervalDesc, "How often to broadcast presence (lower = faster, more overhead)"),
                save, v => config.PartyCoordination.HeartbeatIntervalMs = v);

            config.PartyCoordination.InstanceTimeoutMs = ConfigUIHelpers.IntSlider(
                Loc.T(LocalizedStrings.PartyCoordination.InstanceTimeout, "Instance Timeout (ms)"),
                config.PartyCoordination.InstanceTimeoutMs, 2000, 15000,
                Loc.T(LocalizedStrings.PartyCoordination.InstanceTimeoutDesc, "Time before an instance is considered disconnected"),
                save, v => config.PartyCoordination.InstanceTimeoutMs = v);

            config.PartyCoordination.HealReservationExpiryMs = ConfigUIHelpers.IntSlider(
                Loc.T(LocalizedStrings.PartyCoordination.HealReservationExpiry, "Heal Reservation Expiry (ms)"),
                config.PartyCoordination.HealReservationExpiryMs, 1000, 5000,
                Loc.T(LocalizedStrings.PartyCoordination.HealReservationExpiryDesc, "How long heal reservations stay valid"),
                save, v => config.PartyCoordination.HealReservationExpiryMs = v);

            ConfigUIHelpers.EndIndent();
        }
    }
}
