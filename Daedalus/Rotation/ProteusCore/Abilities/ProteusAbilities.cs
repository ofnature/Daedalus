using Daedalus.Data;
using Daedalus.Rotation.Common.Scheduling;

namespace Daedalus.Rotation.ProteusCore.Abilities;

/// <summary>
/// Declarative <see cref="AbilityBehavior"/> for the Blue Mage kit. BLU spells that aren't SLOTTED
/// in the active set are rejected by the scheduler's dispatch-time status gate, so a push is only a
/// candidate — unslotted spells fall through to the next priority instead of stalling.
/// </summary>
public static class ProteusAbilities
{
    // --- Fillers ---
    public static readonly AbilityBehavior SonicBoom = new() { Action = BLUActions.SonicBoom };
    public static readonly AbilityBehavior WaterCannon = new() { Action = BLUActions.WaterCannon };
    public static readonly AbilityBehavior GoblinPunch = new() { Action = BLUActions.GoblinPunch };
    public static readonly AbilityBehavior Plaincracker = new() { Action = BLUActions.Plaincracker, Toggle = cfg => cfg.BlueMage.EnableAoERotation };

    // --- Nukes / DoT ---
    public static readonly AbilityBehavior TheRoseOfDestruction = new() { Action = BLUActions.TheRoseOfDestruction, Toggle = cfg => cfg.BlueMage.EnableRoseOfDestruction };
    public static readonly AbilityBehavior SongOfTorment = new() { Action = BLUActions.SongOfTorment, Toggle = cfg => cfg.BlueMage.EnableSongOfTorment };

    // --- Role / utility ---
    public static readonly AbilityBehavior AethericMimicry = new() { Action = BLUActions.AethericMimicry, Toggle = cfg => cfg.BlueMage.EnableMimicry };
    /// <summary>
    /// Manual/fleet mimicry requests (window buttons) — NO toggle: the Toggle gate re-fires at
    /// DISPATCH, so routing a manual request through the auto behavior with Auto Mimicry off
    /// pushed a candidate the dispatcher then silently rejected — the scan "saw" the target,
    /// nothing cast, the 4s grace blacklisted it (field report 2026-07-17, BLU→BLU DPS mimic).
    /// </summary>
    public static readonly AbilityBehavior AethericMimicryManual = new() { Action = BLUActions.AethericMimicry };
    public static readonly AbilityBehavior MightyGuard = new() { Action = BLUActions.MightyGuard, Toggle = cfg => cfg.BlueMage.EnableMightyGuard };
    public static readonly AbilityBehavior Diamondback = new() { Action = BLUActions.Diamondback, Toggle = cfg => cfg.BlueMage.EnableDiamondback };
    public static readonly AbilityBehavior WhiteWind = new() { Action = BLUActions.WhiteWind, Toggle = cfg => cfg.BlueMage.EnableWhiteWind };

    // --- Burst ---
    public static readonly AbilityBehavior MoonFlute = new() { Action = BLUActions.MoonFlute, Toggle = cfg => cfg.BlueMage.EnableMoonFlute };

    // --- DoTs / snapshot (loadout wave 2) ---
    public static readonly AbilityBehavior Bristle = new() { Action = BLUActions.Bristle, Toggle = cfg => cfg.BlueMage.EnableBristle };
    public static readonly AbilityBehavior BreathOfMagic = new() { Action = BLUActions.BreathOfMagic, Toggle = cfg => cfg.BlueMage.EnableBreathOfMagic };
    public static readonly AbilityBehavior MortalFlame = new() { Action = BLUActions.MortalFlame, Toggle = cfg => cfg.BlueMage.EnableMortalFlame };

    // --- Nukes / procs ---
    public static readonly AbilityBehavior MatraMagic = new() { Action = BLUActions.MatraMagic, Toggle = cfg => cfg.BlueMage.EnableMatraMagic };
    public static readonly AbilityBehavior ColdFog = new() { Action = BLUActions.ColdFog, Toggle = cfg => cfg.BlueMage.EnableColdFog };
    public static readonly AbilityBehavior WhiteDeath = new() { Action = BLUActions.WhiteDeath, Toggle = cfg => cfg.BlueMage.EnableColdFog, ProcBuff = BLUActions.StatusIds.TouchOfFrost };
    public static readonly AbilityBehavior BadBreath = new() { Action = BLUActions.BadBreath, Toggle = cfg => cfg.BlueMage.EnableBadBreath };

    // --- Freeze→shatter ---
    public static readonly AbilityBehavior TheRamsVoice = new() { Action = BLUActions.TheRamsVoice, Toggle = cfg => cfg.BlueMage.EnableFreezeShatter };
    public static readonly AbilityBehavior Ultravibration = new() { Action = BLUActions.Ultravibration, Toggle = cfg => cfg.BlueMage.EnableFreezeShatter };

    // --- oGCD weaves ---
    public static readonly AbilityBehavior FeatherRain = new() { Action = BLUActions.FeatherRain, Toggle = cfg => cfg.BlueMage.EnableOffensiveOgcds };
    public static readonly AbilityBehavior GlassDance = new() { Action = BLUActions.GlassDance, Toggle = cfg => cfg.BlueMage.EnableOffensiveOgcds };
    public static readonly AbilityBehavior BothEnds = new() { Action = BLUActions.BothEnds, Toggle = cfg => cfg.BlueMage.EnableOffensiveOgcds };
    public static readonly AbilityBehavior Surpanakha = new() { Action = BLUActions.Surpanakha, Toggle = cfg => cfg.BlueMage.EnableSurpanakha, ChargeSource = 18323 };

    // --- Cheese / execute ---
    public static readonly AbilityBehavior Missile = new() { Action = BLUActions.Missile, Toggle = cfg => cfg.BlueMage.EnableMissileCheese };

    // --- Execute ---
    public static readonly AbilityBehavior FinalSting = new() { Action = BLUActions.FinalSting, Toggle = cfg => cfg.BlueMage.EnableFinalSting };
    /// <summary>The same cast under the FLEET opt-in (v3.4 orders) — separate from the Solo execute toggle.</summary>
    public static readonly AbilityBehavior FleetFinalSting = new() { Action = BLUActions.FinalSting, Toggle = cfg => cfg.BlueMage.EnableFleetSting };

    // --- Self buffs ---
    public static readonly AbilityBehavior BasicInstinct = new() { Action = BLUActions.BasicInstinct, Toggle = cfg => cfg.BlueMage.EnableBasicInstinct };
    public static readonly AbilityBehavior ToadOil = new() { Action = BLUActions.ToadOil, Toggle = cfg => cfg.BlueMage.EnableToadOil };

    // --- v3 coordination ---
    public static readonly AbilityBehavior Cactguard = new() { Action = BLUActions.Cactguard, Toggle = cfg => cfg.BlueMage.EnableCactguard };

    // --- Healer kit ---
    public static readonly AbilityBehavior PomCure = new() { Action = BLUActions.PomCure, Toggle = cfg => cfg.BlueMage.EnablePomCure };
    /// <summary>The BLU raise (raid audit 2026-07-18): healer-role kit, global Resurrection toggle.</summary>
    public static readonly AbilityBehavior AngelWhisper = new() { Action = BLUActions.AngelWhisper, Toggle = cfg => cfg.Resurrection.EnableRaise };
    public static readonly AbilityBehavior BluSwiftcast = new() { Action = RoleActions.Swiftcast };
    public static readonly AbilityBehavior Gobskin = new() { Action = BLUActions.Gobskin, Toggle = cfg => cfg.BlueMage.EnableGobskin };
    public static readonly AbilityBehavior Exuviation = new() { Action = BLUActions.Exuviation, Toggle = cfg => cfg.BlueMage.EnableExuviation };
}
