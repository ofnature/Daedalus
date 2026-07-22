using Daedalus.Models.Gear;

namespace Daedalus.Data;

/// <summary>
/// Per-job substat priority order + guidance note for the Meld Optimizer banner and (phase 5)
/// the sweep's stat weighting. Entered from The Balance's level-100 guidance and cross-checked
/// against the rank-1 parse audit of 2026-07-22 (meld choices visible in top logs).
/// // verified 2026-07-22 — re-verify each patch (game-data rule: live data outranks snapshots).
/// </summary>
public static class BalancePriorities
{
    public sealed record JobPriority(
        /// <summary>Substats in meld-priority order (highest first).</summary>
        uint[] Order,
        /// <summary>The job's speed stat, shown dimmed when not part of Order.</summary>
        uint SpeedStat,
        /// <summary>One-line guidance note for the banner.</summary>
        string Note);

    private static readonly uint[] CritDetDh =
        { GearStatIds.CriticalHit, GearStatIds.Determination, GearStatIds.DirectHit };

    private static readonly uint[] CritDhDet =
        { GearStatIds.CriticalHit, GearStatIds.DirectHit, GearStatIds.Determination };

    public static JobPriority For(uint jobId) => jobId switch
    {
        // ── tanks: Crit > Det > DH; SkS to comfort only; Tenacity low value ──
        JobRegistry.Paladin or JobRegistry.Gladiator or
        JobRegistry.Warrior or JobRegistry.Marauder or
        JobRegistry.DarkKnight or JobRegistry.Gunbreaker =>
            new(CritDetDh, GearStatIds.SkillSpeed,
                "Tank: SkS only to a comfort tier; Tenacity is the last resort meld."),

        // ── healers: Crit > Det > DH for damage; SpS comfort; Piety as needed ──
        JobRegistry.WhiteMage or JobRegistry.Conjurer or
        JobRegistry.Scholar or JobRegistry.Arcanist or
        JobRegistry.Astrologian or JobRegistry.Sage =>
            new(CritDetDh, GearStatIds.SpellSpeed,
                "Healer: SpS to comfort tier, Piety only as much as the fight demands."),

        // ── melee ──
        JobRegistry.Samurai =>
            new(CritDetDh, GearStatIds.SkillSpeed,
                "SAM: hold SkS at base tier unless running a dedicated speed set."),
        JobRegistry.Ninja or JobRegistry.Rogue =>
            new(CritDetDh, GearStatIds.SkillSpeed,
                "NIN: no SkS melds — Huton covers speed; Crit first."),
        JobRegistry.Monk or JobRegistry.Pugilist =>
            new(CritDetDh, GearStatIds.SkillSpeed,
                "MNK: pick a GCD tier first (SkS to that tier), then Crit > Det > DH."),
        JobRegistry.Dragoon or JobRegistry.Lancer or
        JobRegistry.Reaper or JobRegistry.Viper =>
            new(CritDetDh, GearStatIds.SkillSpeed,
                "Crit > Det > DH; skill speed stays at base."),

        // ── physical ranged ──
        JobRegistry.Bard =>
            new(CritDetDh, GearStatIds.SkillSpeed,
                "BRD: Crit first; SkS only in dedicated speed sets."),
        JobRegistry.Machinist or JobRegistry.Dancer =>
            new(CritDetDh, GearStatIds.SkillSpeed,
                "Crit > Det > DH; no speed melds."),

        // ── casters ──
        JobRegistry.BlackMage or JobRegistry.Thaumaturge =>
            new(new[] { GearStatIds.SpellSpeed, GearStatIds.CriticalHit, GearStatIds.Determination, GearStatIds.DirectHit },
                GearStatIds.SpellSpeed,
                "BLM: SpS to your preferred tier FIRST, then Crit > Det > DH."),
        JobRegistry.Summoner or JobRegistry.RedMage or JobRegistry.Pictomancer =>
            new(CritDhDet, GearStatIds.SpellSpeed,
                "Caster: Crit > DH ≈ Det; hold SpS at base tier."),

        _ => new(CritDetDh, GearStatIds.SkillSpeed, "Generic: Crit > Det > DH."),
    };
}
