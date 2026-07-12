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
                // EnumCombo saves before this assignment lands — save again so the rotation-facing
                // config copy picks up the new role immediately (same ordering trap as Toggle).
                config.BlueMage.Role = role;
                save();
            }

            ConfigUIHelpers.Toggle(
                "Auto Aetheric Mimicry",
                () => config.BlueMage.EnableMimicry,
                v => config.BlueMage.EnableMimicry = v,
                "Scan the party (players and Trust NPCs), then the surrounding AREA, for someone matching your role and copy them automatically. In an all-BLU party a Tank/Healer mimicry needs a REAL tank/healer player nearby — grab it in town before queuing (the buff survives zoning). Reapplies after death or role change.", save);

            ConfigUIHelpers.Toggle(
                "Auto-load role loadout",
                () => config.BlueMage.AutoApplyRoleLoadout,
                v => config.BlueMage.AutoApplyRoleLoadout = v,
                "When the Role dropdown changes (out of combat, outside duties), REPLACE the active spell set with the Blue Academy reference loadout for that role — learned spells only. WARNING: this overwrites any hand-built loadout. Manual per-role Apply buttons live in the Missing window.", save);

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

        if (ConfigUIHelpers.SectionHeader("Survival", "BLU"))
        {
            ConfigUIHelpers.BeginIndent();

            ConfigUIHelpers.Toggle(
                "Basic Instinct (solo)",
                () => config.BlueMage.EnableBasicInstinct,
                v => config.BlueMage.EnableBasicInstinct = v,
                "+100% damage while playing WITHOUT a party — permanent until someone joins. Cast once at the start of solo combat. The solo farm/dungeon multiplier.", save);

            ConfigUIHelpers.Toggle(
                "Toad Oil",
                () => config.BlueMage.EnableToadOil,
                v => config.BlueMage.EnableToadOil = v,
                "+20% evasion for 180s. Maintained in tank role and while solo.", save);

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

            ConfigUIHelpers.Toggle(
                "Pom Cure",
                () => config.BlueMage.EnablePomCure,
                v => config.BlueMage.EnablePomCure = v,
                "Single-target heal on the most injured ally (healer role). Requires the healer mimicry — it's a 100-potency joke without it.", save);

            config.BlueMage.PomCureHpPercent = ConfigUIHelpers.IntSlider(
                "Pom Cure HP %",
                config.BlueMage.PomCureHpPercent, 20, 90,
                "HP% at or below which Pom Cure is cast.", save,
                v => config.BlueMage.PomCureHpPercent = v);

            ConfigUIHelpers.Toggle(
                "Gobskin",
                () => config.BlueMage.EnableGobskin,
                v => config.BlueMage.EnableGobskin = v,
                "Party barrier (20y) kept rolling while allies are injured (healer role). Does not stack with SCH/SGE shields.", save);

            ConfigUIHelpers.Toggle(
                "Exuviation (cleanse)",
                () => config.BlueMage.EnableExuviation,
                v => config.BlueMage.EnableExuviation = v,
                "The BLU esuna: heals and removes one debuff from party members within 6y of you (healer role).", save);

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
                "Breath of Magic (DoT)",
                () => config.BlueMage.EnableBreathOfMagic,
                v => config.BlueMage.EnableBreathOfMagic = v,
                "Maintain the 60s Breath of Magic DoT (the strongest DoT in the kit).", save);

            ConfigUIHelpers.Toggle(
                "Mortal Flame",
                () => config.BlueMage.EnableMortalFlame,
                v => config.BlueMage.EnableMortalFlame = v,
                "INFINITE DoT, cast exactly once per target (recasting replaces the snapshot).", save);

            ConfigUIHelpers.Toggle(
                "Bristle snapshot",
                () => config.BlueMage.EnableBristle,
                v => config.BlueMage.EnableBristle = v,
                "Cast Bristle (+50%) right before Breath of Magic / Mortal Flame so the whole DoT ticks harder.", save);

            ConfigUIHelpers.Toggle(
                "The Rose of Destruction",
                () => config.BlueMage.EnableRoseOfDestruction,
                v => config.BlueMage.EnableRoseOfDestruction = v,
                "30s-cooldown single-target nuke, used on cooldown.", save);

            ConfigUIHelpers.Toggle(
                "Matra Magic",
                () => config.BlueMage.EnableMatraMagic,
                v => config.BlueMage.EnableMatraMagic = v,
                "120s-cooldown single-target nuke, used on cooldown.", save);

            ConfigUIHelpers.Toggle(
                "Offensive oGCDs",
                () => config.BlueMage.EnableOffensiveOgcds,
                v => config.BlueMage.EnableOffensiveOgcds = v,
                "Weave Feather Rain, Glass Dance and Both Ends off cooldown.", save);

            ConfigUIHelpers.Toggle(
                "Surpanakha",
                () => config.BlueMage.EnableSurpanakha,
                v => config.BlueMage.EnableSurpanakha = v,
                "Dump all 4 charges back-to-back (each press buffs the next; anything in between drops the stack).", save);

            ConfigUIHelpers.Toggle(
                "Cold Fog / White Death",
                () => config.BlueMage.EnableColdFog,
                v => config.BlueMage.EnableColdFog = v,
                "Cast Cold Fog when something is about to hit you; getting hit unlocks 15s of instant 400-potency White Death spam.", save);

            ConfigUIHelpers.Toggle(
                "Moon Flute burst window",
                () => config.BlueMage.EnableMoonFlute,
                v => config.BlueMage.EnableMoonFlute = v,
                "+50% damage for 15s, then 15s of TOTAL action lockout (Waning Nocturne). Fires only when every slotted big cooldown (Matra, Rose, Both Ends, Glass Dance, Feather Rain, Surpanakha ×4) is ready and the fight will outlast the window. DoTs are re-snapshotted buffed inside it.", save);

            config.BlueMage.MoonFluteMinTtkSeconds = ConfigUIHelpers.IntSlider(
                "Moon Flute Min TTK (s)",
                config.BlueMage.MoonFluteMinTtkSeconds, 0, 120,
                "Hold the Flute when the pack is estimated to die within this many seconds (0 = no hold).", save,
                v => config.BlueMage.MoonFluteMinTtkSeconds = v);

            ConfigUIHelpers.EndIndent();
        }

        if (ConfigUIHelpers.SectionHeader("AoE", "BLU"))
        {
            ConfigUIHelpers.BeginIndent();

            ConfigUIHelpers.Toggle(
                "Freeze → Shatter",
                () => config.BlueMage.EnableFreezeShatter,
                v => config.BlueMage.EnableFreezeShatter = v,
                "The Ram's Voice (Deep Freeze) → Ultravibration: instantly KILLS every frozen enemy around you. All other damage is held between the freeze and the shatter (damage breaks the freeze). Freeze-immune packs are detected and skipped.", save);

            config.BlueMage.UltravibrationMinTargets = ConfigUIHelpers.IntSlider(
                "Freeze → Shatter Min Targets",
                config.BlueMage.UltravibrationMinTargets, 1, 8,
                "Enemies within the 6y freeze radius before the combo starts.", save,
                v => config.BlueMage.UltravibrationMinTargets = v);

            ConfigUIHelpers.Toggle(
                "Bad Breath",
                () => config.BlueMage.EnableBadBreath,
                v => config.BlueMage.EnableBadBreath = v,
                "Once per pack: 8y cone with Slow, Blind, Paralysis, Poison and damage-down on everything it hits.", save);

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
