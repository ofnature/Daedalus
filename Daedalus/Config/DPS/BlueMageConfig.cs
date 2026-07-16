using System;

namespace Daedalus.Config.DPS;

/// <summary>
/// The role Proteus plays as Blue Mage. BLU has no in-game role — this dropdown IS the role:
/// it selects which modules run AND which archetype Aetheric Mimicry copies.
/// </summary>
public enum BluRole
{
    /// <summary>DPS loadout: Sonic Boom filler, mimic a DPS (+20% crit/DH rate).</summary>
    Dps,

    /// <summary>Tank loadout: Mighty Guard stance, Diamondback, Goblin Punch filler, mimic a tank.</summary>
    Tank,

    /// <summary>Healer loadout: White Wind thresholds, mimic a healer (+20% healing potency).</summary>
    Healer,

    /// <summary>
    /// Solo farm/overworld mode: Basic Instinct FIRST (+100% damage while partyless), then Mighty
    /// Guard on top (BI cancels its damage penalty — free tank stance), DPS mimicry, White Wind +
    /// Diamondback self-sustain, optional Final Sting execute. Appended value — never reorder.
    /// </summary>
    Solo,
}

/// <summary>
/// Blue Mage (Proteus) configuration. BLU availability is learned+slotted (not level), so there
/// are no level-gated toggles here — per-spell enables plus the role selector.
/// </summary>
[Serializable]
public sealed class BlueMageConfig
{
    /// <summary>The role dropdown — drives module selection and Aetheric Mimicry archetype.</summary>
    public BluRole Role { get; set; } = BluRole.Dps;

    /// <summary>Auto-apply Aetheric Mimicry matching <see cref="Role"/> (Mimicry Helper parity).
    /// Turn OFF to drive mimicry entirely from the BLU Mimicry window's role buttons.</summary>
    public bool EnableMimicry { get; set; } = true;

    /// <summary>Pop the BLU Mimicry window (role buttons + remove) when switching to BLU.</summary>
    public bool ShowMimicryWindowOnBlu { get; set; } = true;

    /// <summary>
    /// When the Role dropdown changes (out of combat, outside duties), load the Blue Academy
    /// reference loadout for that role — learned spells only, unlearned slots left empty.
    /// Default OFF: it REPLACES the active 24-slot set, clobbering any hand-built loadout.
    /// </summary>
    public bool AutoApplyRoleLoadout { get; set; } = false;

    /// <summary>Maintain Mighty Guard while Role = Tank (dropped when leaving tank role).</summary>
    public bool EnableMightyGuard { get; set; } = true;

    /// <summary>Diamondback below the HP threshold while Role = Tank.</summary>
    public bool EnableDiamondback { get; set; } = true;

    private int _diamondbackHpPercent = 50;
    /// <summary>HP% at/below which Diamondback fires (tank role).</summary>
    public int DiamondbackHpPercent
    {
        get => _diamondbackHpPercent;
        set => _diamondbackHpPercent = Math.Clamp(value, 10, 90);
    }

    /// <summary>White Wind when injured allies are in its 15y radius (healer role; self-save any role).</summary>
    public bool EnableWhiteWind { get; set; } = true;

    private int _whiteWindHpPercent = 60;
    /// <summary>HP% at/below which White Wind is considered.</summary>
    public int WhiteWindHpPercent
    {
        get => _whiteWindHpPercent;
        set => _whiteWindHpPercent = Math.Clamp(value, 20, 90);
    }

    /// <summary>Song of Torment DoT maintenance (30s Bleeding).</summary>
    public bool EnableSongOfTorment { get; set; } = true;

    /// <summary>The Rose of Destruction on cooldown (30s ST nuke).</summary>
    public bool EnableRoseOfDestruction { get; set; } = true;

    /// <summary>Plaincracker AoE rotation (6y self AoE — count is player-anchored).</summary>
    public bool EnableAoERotation { get; set; } = true;

    private int _aoeMinTargets = 3;
    /// <summary>Enemies within Plaincracker's 6y self-radius before AoE replaces the ST filler.</summary>
    public int AoEMinTargets
    {
        get => _aoeMinTargets;
        set => _aoeMinTargets = Math.Clamp(value, 2, 8);
    }

    // ── Loadout wave 2 (2026-07-11) ────────────────────────────────────────

    /// <summary>Breath of Magic DoT upkeep (120p/60s, own status).</summary>
    public bool EnableBreathOfMagic { get; set; } = true;

    /// <summary>Mortal Flame — infinite DoT, cast ONCE per target.</summary>
    public bool EnableMortalFlame { get; set; } = true;

    /// <summary>Bristle before Breath of Magic / Mortal Flame (snapshots +50% into the DoT).</summary>
    public bool EnableBristle { get; set; } = true;

    /// <summary>Matra Magic on cooldown (120s ST nuke).</summary>
    public bool EnableMatraMagic { get; set; } = true;

    /// <summary>Feather Rain / Glass Dance / Both Ends off-cooldown oGCD weaves.</summary>
    public bool EnableOffensiveOgcds { get; set; } = true;

    /// <summary>Surpanakha 4-charge dump (all four back-to-back — anything else drops the stack).</summary>
    public bool EnableSurpanakha { get; set; } = true;

    /// <summary>Cold Fog when getting hit in a pack; White Death spam while Touch of Frost is up.</summary>
    public bool EnableColdFog { get; set; } = true;

    /// <summary>Bad Breath once per pack (AoE debuff spread: Slow/Blind/Paralysis/Poison + damage-down).</summary>
    public bool EnableBadBreath { get; set; } = true;

    /// <summary>Basic Instinct while solo (+100% damage, permanent until a party member appears).</summary>
    public bool EnableBasicInstinct { get; set; } = true;

    /// <summary>Toad Oil upkeep (+20% evasion, 180s) in tank role or while solo.</summary>
    public bool EnableToadOil { get; set; } = true;

    /// <summary>Pom Cure single-target heal (healer role; requires healer mimicry — 100p without it).</summary>
    public bool EnablePomCure { get; set; } = true;

    private int _pomCureHpPercent = 60;
    /// <summary>HP% at/below which Pom Cure is cast on the most injured ally.</summary>
    public int PomCureHpPercent
    {
        get => _pomCureHpPercent;
        set => _pomCureHpPercent = Math.Clamp(value, 20, 90);
    }

    /// <summary>Gobskin barrier upkeep (healer role, refresh when the shield is gone).</summary>
    public bool EnableGobskin { get; set; } = true;

    /// <summary>Exuviation cleanse (healer role — removes one debuff from nearby party).</summary>
    public bool EnableExuviation { get; set; } = true;

    /// <summary>
    /// Moon Flute burst window (kit-driven): fires when every slotted big piece is off cooldown
    /// and the pack will outlive the window. Default OFF until validated in-game — Waning
    /// Nocturne locks ALL actions for 15s after the buff.
    /// </summary>
    public bool EnableMoonFlute { get; set; } = false;

    private int _moonFluteMinTtkSeconds = 30;
    /// <summary>Hold Moon Flute when the pack's estimated time-to-kill is below this (0 = off).</summary>
    public int MoonFluteMinTtkSeconds
    {
        get => _moonFluteMinTtkSeconds;
        set => _moonFluteMinTtkSeconds = Math.Clamp(value, 0, 120);
    }

    /// <summary>The Ram's Voice → Ultravibration freeze→shatter combo (instantly kills trash packs).</summary>
    public bool EnableFreezeShatter { get; set; } = true;

    private int _ultravibrationMinTargets = 2;
    /// <summary>Enemies within the 6y freeze radius before the freeze→shatter combo starts.</summary>
    public int UltravibrationMinTargets
    {
        get => _ultravibrationMinTargets;
        set => _ultravibrationMinTargets = Math.Clamp(value, 1, 8);
    }

    /// <summary>
    /// Final Sting execute (Solo role only): ~2000p that KILLS THE CASTER and locks itself out
    /// for 10 minutes (Brush with Death). Default OFF — for finishing tough solo targets, never
    /// for farm loops.
    /// </summary>
    public bool EnableFinalSting { get; set; } = false;

    private int _finalStingTargetHpPercent = 30;
    /// <summary>Target HP% at/below which Final Sting fires (Solo role, last engaged enemy only).</summary>
    public int FinalStingTargetHpPercent
    {
        get => _finalStingTargetHpPercent;
        set => _finalStingTargetHpPercent = Math.Clamp(value, 5, 50);
    }

    /// <summary>
    /// Missile chain on death-vulnerable bosses (in duty): 50% of current HP per cast until the
    /// HP floor. Unknown enemies get ONE probe cast — its outcome feeds the death-immunity
    /// ledger, which permanently remembers who's immune.
    /// </summary>
    public bool EnableMissileCheese { get; set; } = true;

    private int _missileHpFloorPercent = 30;
    /// <summary>Stop missiling below this target HP% (normal rotation finishes faster there).</summary>
    public int MissileHpFloorPercent
    {
        get => _missileHpFloorPercent;
        set => _missileHpFloorPercent = Math.Clamp(value, 10, 60);
    }

    private int _missileMinTargetMaxHp = 40000;
    /// <summary>Only consider targets at/above this max HP (bosses, not trash).</summary>
    public int MissileMinTargetMaxHp
    {
        get => _missileMinTargetMaxHp;
        set => _missileMinTargetMaxHp = Math.Clamp(value, 10_000, 1_000_000);
    }

    // ── v3 multi-BLU coordination (only acts when ≥2 BLU toons share the LAN bus) ──

    /// <summary>
    /// Synchronize Moon Flute windows across the fleet: when every BLU's pieces are ready the
    /// coordinated burst signal starts everyone's window on the same tick (T13 splits the party
    /// into 30s-staggered groups). Only applies with ≥2 BLU on the bus — solo Flute timing is
    /// unchanged. Requires <see cref="EnableMoonFlute"/>.
    /// </summary>
    public bool SyncMoonFluteWithParty { get; set; } = true;

    /// <summary>
    /// Cactguard the tank when BossMod forecasts a tankbuster (one designated non-tank caster
    /// per fleet; solo-BLU parties self-designate). Needs Cactguard slotted + a tank in party.
    /// </summary>
    public bool EnableCactguard { get; set; } = true;

    /// <summary>Calculator calibration: an observed NON-CRIT, unbuffed damage number (0 = unset).
    /// Persisted — it's also the seed for the future fleet-sting count math.</summary>
    public int FinalStingBaselineDamage { get; set; }

    /// <summary>The calibration hit's potency (210 = Sonic Boom, 2000 = a real Final Sting test).</summary>
    public float FinalStingBaselinePotency { get; set; } = 210f;
}
