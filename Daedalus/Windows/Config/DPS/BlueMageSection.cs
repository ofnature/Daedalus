using System;
using Daedalus.Config.DPS;

namespace Daedalus.Windows.Config.DPS;

/// <summary>
/// Renders the Blue Mage (Proteus) settings section. The role dropdown is the centerpiece:
/// it selects the module set AND which archetype Aetheric Mimicry copies.
/// </summary>
public sealed class BlueMageSection
{
    private readonly Configuration config;
    private readonly Action save;

    public BlueMageSection(Configuration config, Action save)
    {
        this.config = config;
        this.save = save;
    }

    public void Draw()
    {
        ConfigUIHelpers.JobHeader("Blue Mage", "Proteus", ConfigUIHelpers.BlackMageColor);

        if (ConfigUIHelpers.SectionHeader("Role", "BLU"))
        {
            ConfigUIHelpers.BeginIndent();

            var role = config.BlueMage.Role;
            if (ConfigUIHelpers.EnumCombo("Role", ref role,
                "What you're playing as. Drives the rotation (tank stance/mitigation, healer thresholds, DPS filler) AND which archetype Aetheric Mimicry copies from nearby party members.", save))
            {
                config.BlueMage.Role = role;
            }

            ConfigUIHelpers.Toggle(
                "Auto Aetheric Mimicry",
                () => config.BlueMage.EnableMimicry,
                v => config.BlueMage.EnableMimicry = v,
                "Scan the party (players and Trust NPCs), then the surrounding AREA, for someone matching your role and copy them automatically. In an all-BLU party a Tank/Healer mimicry needs a REAL tank/healer player nearby — grab it in town before queuing (the buff survives zoning). Reapplies after death or role change.", save);

            ConfigUIHelpers.EndIndent();
        }

        if (ConfigUIHelpers.SectionHeader("Tank", "BLU"))
        {
            ConfigUIHelpers.BeginIndent();

            ConfigUIHelpers.Toggle(
                "Mighty Guard",
                () => config.BlueMage.EnableMightyGuard,
                v => config.BlueMage.EnableMightyGuard = v,
                "Maintain the tank stance while Role = Tank (dropped automatically when leaving tank role).", save);

            ConfigUIHelpers.Toggle(
                "Diamondback",
                () => config.BlueMage.EnableDiamondback,
                v => config.BlueMage.EnableDiamondback = v,
                "Emergency ~90% mitigation below the HP threshold (tank role). Locks all actions for its duration.", save);

            config.BlueMage.DiamondbackHpPercent = ConfigUIHelpers.IntSlider(
                "Diamondback HP %",
                config.BlueMage.DiamondbackHpPercent, 10, 90,
                "HP% at or below which Diamondback fires.", save,
                v => config.BlueMage.DiamondbackHpPercent = v);

            ConfigUIHelpers.EndIndent();
        }

        if (ConfigUIHelpers.SectionHeader("Healing", "BLU"))
        {
            ConfigUIHelpers.BeginIndent();

            ConfigUIHelpers.Toggle(
                "White Wind",
                () => config.BlueMage.EnableWhiteWind,
                v => config.BlueMage.EnableWhiteWind = v,
                "Party heal (healer role: 2+ injured below threshold) and tank self-sustain. Heals scale with YOUR current HP, so it's skipped when you're nearly dead.", save);

            config.BlueMage.WhiteWindHpPercent = ConfigUIHelpers.IntSlider(
                "White Wind HP %",
                config.BlueMage.WhiteWindHpPercent, 20, 90,
                "HP% at or below which White Wind is considered.", save,
                v => config.BlueMage.WhiteWindHpPercent = v);

            ConfigUIHelpers.EndIndent();
        }

        if (ConfigUIHelpers.SectionHeader("Damage", "BLU"))
        {
            ConfigUIHelpers.BeginIndent();

            ConfigUIHelpers.Toggle(
                "Song of Torment (DoT)",
                () => config.BlueMage.EnableSongOfTorment,
                v => config.BlueMage.EnableSongOfTorment = v,
                "Maintain the 30s Bleeding DoT.", save);

            ConfigUIHelpers.Toggle(
                "The Rose of Destruction",
                () => config.BlueMage.EnableRoseOfDestruction,
                v => config.BlueMage.EnableRoseOfDestruction = v,
                "30s-cooldown single-target nuke, used on cooldown.", save);

            ConfigUIHelpers.Toggle(
                "AoE Rotation (Plaincracker)",
                () => config.BlueMage.EnableAoERotation,
                v => config.BlueMage.EnableAoERotation = v,
                "6-yalm self-centered AoE — only fires when enough enemies are around YOU (never counts a distant pack).", save);

            config.BlueMage.AoEMinTargets = ConfigUIHelpers.IntSlider(
                "AoE Min Targets",
                config.BlueMage.AoEMinTargets, 2, 8,
                "Enemies within 6y of you before AoE replaces the single-target filler.", save,
                v => config.BlueMage.AoEMinTargets = v);

            ConfigUIHelpers.EndIndent();
        }
    }
}
