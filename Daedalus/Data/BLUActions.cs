using Daedalus.Models.Action;

namespace Daedalus.Data;

/// <summary>
/// Blue Mage action definitions — the core kit Proteus rotates with. Every id, cast time, range,
/// radius, MP cost and status id here was pulled from the game sheets via XIVAPI (2026-07-02);
/// none are guessed. BLU availability is NOT level-based: a spell is usable iff it is learned
/// (spellbook) AND slotted in the active set — the scheduler's dispatch-time status gate handles
/// the slotted check, modules gate pushes on <c>IsActionLearned</c>. The full 124-spell table with
/// learn sources lives in <see cref="BLUSpellbook"/>.
/// </summary>
public static class BLUActions
{
    // BLU GCD recast is a fixed 2.5s (no skill/spell speed scaling).
    private const float BluRecast = 2.5f;

    /// <summary>#1 — starter spell, granted at job unlock. Hardcast ST fallback filler.</summary>
    public static readonly ActionDefinition WaterCannon = new()
    {
        ActionId = 11385,
        Name = "Water Cannon",
        MinLevel = 1,
        Category = ActionCategory.GCD,
        TargetType = ActionTargetType.SingleEnemy,
        EffectTypes = ActionEffectType.Damage,
        CastTime = 2.0f,
        RecastTime = BluRecast,
        Range = 25f,
        MpCost = 100,
        DamagePotency = 200
    };

    /// <summary>#63 — primary ST filler: 1.0s cast, 25y, the BLU "Glare".</summary>
    public static readonly ActionDefinition SonicBoom = new()
    {
        ActionId = 18308,
        Name = "Sonic Boom",
        MinLevel = 1,
        Category = ActionCategory.GCD,
        TargetType = ActionTargetType.SingleEnemy,
        EffectTypes = ActionEffectType.Damage,
        CastTime = 1.0f,
        RecastTime = BluRecast,
        Range = 25f,
        MpCost = 200,
        DamagePotency = 210
    };

    /// <summary>#105 — tank-role filler: INSTANT, 3y melee. Low dropoff under Mighty Guard.</summary>
    public static readonly ActionDefinition GoblinPunch = new()
    {
        ActionId = 34563,
        Name = "Goblin Punch",
        MinLevel = 1,
        Category = ActionCategory.GCD,
        TargetType = ActionTargetType.SingleEnemy,
        EffectTypes = ActionEffectType.Damage,
        CastTime = 0f,
        RecastTime = BluRecast,
        Range = 3f,
        MpCost = 200,
        DamagePotency = 220 // 400 when a tank-mimicry stance is active
    };

    /// <summary>#11 — AoE filler: 6y point-blank SELF AoE (count MUST be player-anchored).</summary>
    public static readonly ActionDefinition Plaincracker = new()
    {
        ActionId = 11391,
        Name = "Plaincracker",
        MinLevel = 1,
        Category = ActionCategory.GCD,
        TargetType = ActionTargetType.Self,
        EffectTypes = ActionEffectType.Damage,
        CastTime = 2.0f,
        RecastTime = BluRecast,
        Range = 0f,
        Radius = 6f,
        MpCost = 200,
        DamagePotency = 220
    };

    /// <summary>#9 — ST DoT: applies Bleeding (status 1714, 30s). FindEnemyNeedingDot pattern.</summary>
    public static readonly ActionDefinition SongOfTorment = new()
    {
        ActionId = 11386,
        Name = "Song of Torment",
        MinLevel = 1,
        Category = ActionCategory.GCD,
        TargetType = ActionTargetType.SingleEnemy,
        EffectTypes = ActionEffectType.Damage | ActionEffectType.DoT,
        CastTime = 2.0f,
        RecastTime = BluRecast,
        Range = 25f,
        MpCost = 400,
        DamagePotency = 50 // + Bleeding 50p/tick 30s
    };

    /// <summary>#90 — 30s-cooldown ST nuke (own recast group, safe to IsActionReady-check).</summary>
    public static readonly ActionDefinition TheRoseOfDestruction = new()
    {
        ActionId = 23275,
        Name = "The Rose of Destruction",
        MinLevel = 1,
        Category = ActionCategory.GCD,
        TargetType = ActionTargetType.SingleEnemy,
        EffectTypes = ActionEffectType.Damage,
        CastTime = 2.0f,
        RecastTime = 30f,
        Range = 25f,
        MpCost = 300,
        DamagePotency = 400
    };

    /// <summary>#30 — tank stance: -40% damage dealt, +tank defense/enmity. Status 1719. GCD cast.</summary>
    public static readonly ActionDefinition MightyGuard = new()
    {
        ActionId = 11417,
        Name = "Mighty Guard",
        MinLevel = 1,
        Category = ActionCategory.GCD,
        TargetType = ActionTargetType.Self,
        EffectTypes = ActionEffectType.Buff,
        CastTime = 2.0f,
        RecastTime = BluRecast,
        MpCost = 700
    };

    /// <summary>#29 — tankbuster mitigation: ~90% reduction for 10s, locks movement. Status 1722.</summary>
    public static readonly ActionDefinition Diamondback = new()
    {
        ActionId = 11424,
        Name = "Diamondback",
        MinLevel = 1,
        Category = ActionCategory.GCD,
        TargetType = ActionTargetType.Self,
        EffectTypes = ActionEffectType.Buff | ActionEffectType.Shield,
        CastTime = 2.0f,
        RecastTime = BluRecast,
        MpCost = 3000
    };

    /// <summary>#13 — party heal: 15y self-centered AoE, heals ~= caster current HP. Expensive.</summary>
    public static readonly ActionDefinition WhiteWind = new()
    {
        ActionId = 11406,
        Name = "White Wind",
        MinLevel = 1,
        Category = ActionCategory.GCD,
        TargetType = ActionTargetType.Self,
        EffectTypes = ActionEffectType.Heal,
        CastTime = 2.0f,
        RecastTime = BluRecast,
        Radius = 15f,
        MpCost = 1500
    };

    /// <summary>
    /// #77 — copies the targeted player's ROLE: Tank (status 2124) / DPS (2125) / Healer (2126).
    /// Ally-targeted, 25y. PERMANENT until recast — survives death AND zoning, so it is grabbed
    /// once in town and holds for the whole session. The Proteus role dropdown decides which
    /// archetype to scan for (Mimicry Helper parity).
    /// </summary>
    public static readonly ActionDefinition AethericMimicry = new()
    {
        ActionId = 18322,
        Name = "Aetheric Mimicry",
        MinLevel = 1,
        Category = ActionCategory.GCD,
        TargetType = ActionTargetType.SingleAlly,
        EffectTypes = ActionEffectType.Buff,
        CastTime = 1.0f,
        RecastTime = BluRecast,
        Range = 25f,
        MpCost = 300
    };

    /// <summary>
    /// #39 — Moon Flute: +50% damage for 15s, then a 15s full action lockout. NOT used by the v1
    /// rotation (the burst planner is a later milestone — see burn-reference/proteus-plan.md);
    /// defined so the checklist and future planner have the verified id.
    /// </summary>
    public static readonly ActionDefinition MoonFlute = new()
    {
        ActionId = 11415,
        Name = "Moon Flute",
        MinLevel = 1,
        Category = ActionCategory.GCD,
        TargetType = ActionTargetType.Self,
        EffectTypes = ActionEffectType.Buff,
        CastTime = 2.0f,
        RecastTime = BluRecast,
        MpCost = 500
    };

    // ── Loadout wave 2 (2026-07-11) — the user's 24-slot farm/support kit. Every value below
    // re-verified against the Action sheet via XIVAPI on this date; statuses in StatusIds. ──

    /// <summary>#12 — +50% potency on the NEXT offensive magic spell (Boost 1716). DoT snapshotter.</summary>
    public static readonly ActionDefinition Bristle = new()
    {
        ActionId = 11393,
        Name = "Bristle",
        MinLevel = 1,
        Category = ActionCategory.GCD,
        TargetType = ActionTargetType.Self,
        EffectTypes = ActionEffectType.Buff,
        CastTime = 1.0f,
        RecastTime = BluRecast,
        MpCost = 200
    };

    /// <summary>#109 — 120p/tick 60s DoT (own status 3712), 10y self-anchored cone.</summary>
    public static readonly ActionDefinition BreathOfMagic = new()
    {
        ActionId = 34567,
        Name = "Breath of Magic",
        MinLevel = 1,
        Category = ActionCategory.GCD,
        TargetType = ActionTargetType.Self,
        EffectTypes = ActionEffectType.Damage | ActionEffectType.DoT,
        CastTime = 2.0f,
        RecastTime = BluRecast,
        Radius = 10f,
        MpCost = 300
    };

    /// <summary>
    /// #121 — INFINITE 40p fire DoT (status 3643), 25y ST. Cast ONCE per target — any recast
    /// replaces the snapshot (re-applying unbuffed destroys a buffed one).
    /// </summary>
    public static readonly ActionDefinition MortalFlame = new()
    {
        ActionId = 34579,
        Name = "Mortal Flame",
        MinLevel = 1,
        Category = ActionCategory.GCD,
        TargetType = ActionTargetType.SingleEnemy,
        EffectTypes = ActionEffectType.Damage | ActionEffectType.DoT,
        CastTime = 2.0f,
        RecastTime = BluRecast,
        Range = 25f,
        MpCost = 500
    };

    /// <summary>#100 — 120s-recast ST nuke (50p ×8). Shares its recast group with Dragon Force /
    /// Angel's Snack — gate via GetCooldownRemaining, never IsActionReady (MCH FMF lesson).</summary>
    public static readonly ActionDefinition MatraMagic = new()
    {
        ActionId = 23285,
        Name = "Matra Magic",
        MinLevel = 1,
        Category = ActionCategory.GCD,
        TargetType = ActionTargetType.SingleEnemy,
        EffectTypes = ActionEffectType.Damage,
        CastTime = 2.0f,
        RecastTime = 120f,
        Range = 25f,
        MpCost = 400,
        DamagePotency = 400
    };

    /// <summary>#44 — instant 30s-recast oGCD, GROUND-PLACED at the target (30y reach, 5y splash).</summary>
    public static readonly ActionDefinition FeatherRain = new()
    {
        ActionId = 11426,
        Name = "Feather Rain",
        MinLevel = 1,
        Category = ActionCategory.oGCD,
        TargetType = ActionTargetType.Self,
        EffectTypes = ActionEffectType.Damage | ActionEffectType.DoT,
        CastTime = 0f,
        RecastTime = 30f,
        Range = 30f,
        Radius = 5f,
        MpCost = 300,
        DamagePotency = 220
    };

    /// <summary>#48 — instant 90s-recast oGCD, 350p self-anchored 12y ice AoE.</summary>
    public static readonly ActionDefinition GlassDance = new()
    {
        ActionId = 11430,
        Name = "Glass Dance",
        MinLevel = 1,
        Category = ActionCategory.oGCD,
        TargetType = ActionTargetType.Self,
        EffectTypes = ActionEffectType.Damage,
        CastTime = 0f,
        RecastTime = 90f,
        Radius = 12f,
        MpCost = 500,
        DamagePotency = 350
    };

    /// <summary>#102 — instant 120s-recast oGCD, 600p self-anchored 20y line AoE.</summary>
    public static readonly ActionDefinition BothEnds = new()
    {
        ActionId = 23287,
        Name = "Both Ends",
        MinLevel = 1,
        Category = ActionCategory.oGCD,
        TargetType = ActionTargetType.Self,
        EffectTypes = ActionEffectType.Damage,
        CastTime = 0f,
        RecastTime = 120f,
        Radius = 20f,
        MpCost = 300,
        DamagePotency = 600
    };

    /// <summary>
    /// #78 — instant oGCD, 4 CHARGES (30s each), 16y self-anchored cone. Each press buffs the next
    /// (Surpanakha's Fury 2130); ANY other action drops the stack — dump all 4 back-to-back only.
    /// </summary>
    public static readonly ActionDefinition Surpanakha = new()
    {
        ActionId = 18323,
        Name = "Surpanakha",
        MinLevel = 1,
        Category = ActionCategory.oGCD,
        TargetType = ActionTargetType.Self,
        EffectTypes = ActionEffectType.Damage,
        CastTime = 0f,
        RecastTime = 30f,
        Radius = 16f,
        MpCost = 200,
        DamagePotency = 200
    };

    /// <summary>#84 — 90s-recast self buff (Cold Fog 2493, 5s): take a hit within the window to gain
    /// Touch of Frost (2494, 15s), which unlocks White Death spam.</summary>
    public static readonly ActionDefinition ColdFog = new()
    {
        ActionId = 23267,
        Name = "Cold Fog",
        MinLevel = 1,
        Category = ActionCategory.GCD,
        TargetType = ActionTargetType.Self,
        EffectTypes = ActionEffectType.Buff,
        CastTime = 2.0f,
        RecastTime = 90f,
        MpCost = 300
    };

    /// <summary>#84b — INSTANT 400p ST filler while Touch of Frost is up (Cold Fog transform).</summary>
    public static readonly ActionDefinition WhiteDeath = new()
    {
        ActionId = 23268,
        Name = "White Death",
        MinLevel = 1,
        Category = ActionCategory.GCD,
        TargetType = ActionTargetType.SingleEnemy,
        EffectTypes = ActionEffectType.Damage,
        CastTime = 0f,
        RecastTime = BluRecast,
        Range = 25f,
        MpCost = 200,
        DamagePotency = 400
    };

    /// <summary>#28 — 220p 8y self-anchored cone + Slow/Blind/Paralysis/Poison/Malodorous (1715).</summary>
    public static readonly ActionDefinition BadBreath = new()
    {
        ActionId = 11388,
        Name = "Bad Breath",
        MinLevel = 1,
        Category = ActionCategory.GCD,
        TargetType = ActionTargetType.Self,
        EffectTypes = ActionEffectType.Damage | ActionEffectType.Debuff,
        CastTime = 2.0f,
        RecastTime = BluRecast,
        Radius = 8f,
        MpCost = 500,
        DamagePotency = 220
    };

    /// <summary>#91 — solo-only permanent buff (2498): +100% damage while no party members present.</summary>
    public static readonly ActionDefinition BasicInstinct = new()
    {
        ActionId = 23276,
        Name = "Basic Instinct",
        MinLevel = 1,
        Category = ActionCategory.GCD,
        TargetType = ActionTargetType.Self,
        EffectTypes = ActionEffectType.Buff,
        CastTime = 2.0f,
        RecastTime = BluRecast,
        MpCost = 500
    };

    /// <summary>#32 — +20% evasion 180s self buff (1737). Tank/solo maintenance.</summary>
    public static readonly ActionDefinition ToadOil = new()
    {
        ActionId = 11410,
        Name = "Toad Oil",
        MinLevel = 1,
        Category = ActionCategory.GCD,
        TargetType = ActionTargetType.Self,
        EffectTypes = ActionEffectType.Buff,
        CastTime = 2.0f,
        RecastTime = BluRecast,
        MpCost = 500
    };

    /// <summary>#58 — ST heal, 100p → 500p under Mimicry:Healer (worthless without it), 30y, 1.5s.</summary>
    public static readonly ActionDefinition PomCure = new()
    {
        ActionId = 18303,
        Name = "Pom Cure",
        MinLevel = 1,
        Category = ActionCategory.GCD,
        TargetType = ActionTargetType.SingleAlly,
        EffectTypes = ActionEffectType.Heal,
        CastTime = 1.5f,
        RecastTime = BluRecast,
        Range = 30f,
        MpCost = 200
    };

    /// <summary>#59 — party barrier 20y (Gobskin 2114), 100p → 250p under Mimicry:Healer. Does NOT
    /// stack with SCH/SGE shields.</summary>
    public static readonly ActionDefinition Gobskin = new()
    {
        ActionId = 18304,
        Name = "Gobskin",
        MinLevel = 1,
        Category = ActionCategory.GCD,
        TargetType = ActionTargetType.Self,
        EffectTypes = ActionEffectType.Shield,
        CastTime = 2.0f,
        RecastTime = BluRecast,
        Radius = 20f,
        MpCost = 200
    };

    /// <summary>#73 — 6y self-anchored AoE heal (50p → 300p under Mimicry:Healer) that also removes
    /// ONE detrimental effect from nearby party — the BLU esuna.</summary>
    public static readonly ActionDefinition Exuviation = new()
    {
        ActionId = 18318,
        Name = "Exuviation",
        MinLevel = 1,
        Category = ActionCategory.GCD,
        TargetType = ActionTargetType.Self,
        EffectTypes = ActionEffectType.Heal | ActionEffectType.Cleanse,
        CastTime = 2.0f,
        RecastTime = BluRecast,
        Radius = 6f,
        MpCost = 200
    };

    /// <summary>#33 — 220p 6y self AoE + Deep Freeze 12s (1731). The freeze half of freeze→shatter.</summary>
    public static readonly ActionDefinition TheRamsVoice = new()
    {
        ActionId = 11419,
        Name = "The Ram's Voice",
        MinLevel = 1,
        Category = ActionCategory.GCD,
        TargetType = ActionTargetType.Self,
        EffectTypes = ActionEffectType.Damage,
        CastTime = 2.0f,
        RecastTime = BluRecast,
        Radius = 6f,
        MpCost = 200,
        DamagePotency = 220
    };

    /// <summary>#92 — 120s-recast 6y self AoE: instantly KILLS frozen/petrified enemies. The shatter.
    /// Recast-group GCD — gate via GetCooldownRemaining, never IsActionReady.</summary>
    public static readonly ActionDefinition Ultravibration = new()
    {
        ActionId = 23277,
        Name = "Ultravibration",
        MinLevel = 1,
        Category = ActionCategory.GCD,
        TargetType = ActionTargetType.Self,
        EffectTypes = ActionEffectType.Damage,
        CastTime = 2.0f,
        RecastTime = 120f,
        Radius = 6f,
        MpCost = 500
    };

    /// <summary>
    /// #8 — ~2000p 3y MELEE execute that KO's the caster and applies Brush with Death (2127,
    /// 10min reuse lockout). Solo-role execute only, config-gated OFF by default.
    /// </summary>
    public static readonly ActionDefinition FinalSting = new()
    {
        ActionId = 11407,
        Name = "Final Sting",
        MinLevel = 1,
        Category = ActionCategory.GCD,
        TargetType = ActionTargetType.SingleEnemy,
        EffectTypes = ActionEffectType.Damage,
        CastTime = 2.0f,
        RecastTime = BluRecast,
        Range = 3f,
        DamagePotency = 2000
    };

    /// <summary>Status ids — verified against the Status sheet (2026-07-02; wave 2 re-verified 2026-07-11).</summary>
    public static class StatusIds
    {
        public const uint MightyGuard = 1719;
        public const uint Diamondback = 1722;
        /// <summary>Song of Torment's DoT — SHARED with Nightbloom/Aetherial Spark.</summary>
        public const uint Bleeding = 1714;
        public const uint AethericMimicryTank = 2124;
        public const uint AethericMimicryDps = 2125;
        public const uint AethericMimicryHealer = 2126;

        /// <summary>Bristle: next offensive spell +50%.</summary>
        public const uint Boost = 1716;
        /// <summary>Moon Flute +50% window (15s).</summary>
        public const uint WaxingNocturne = 1718;
        /// <summary>Moon Flute lockout (15s) — no actions at all.</summary>
        public const uint WaningNocturne = 1727;
        /// <summary>Ram's Voice freeze (12s, breaks on damage) — Ultravibration's trigger.</summary>
        public const uint DeepFreeze = 1731;
        public const uint ToadOil = 1737;
        public const uint Gobskin = 2114;
        /// <summary>Surpanakha stacking buff — any other action drops it.</summary>
        public const uint SurpanakhasFury = 2130;
        public const uint BasicInstinct = 2498;
        /// <summary>Cold Fog armed window (5s) — take a hit to convert.</summary>
        public const uint ColdFog = 2493;
        /// <summary>White Death enabled (15s).</summary>
        public const uint TouchOfFrost = 2494;
        /// <summary>Bad Breath's damage-down debuff — the once-per-pack marker.</summary>
        public const uint Malodorous = 1715;
        /// <summary>Breath of Magic DoT (60s, own status).</summary>
        public const uint BreathOfMagic = 3712;
        /// <summary>Mortal Flame infinite DoT. Sheet row 3643 confirmed via XIVAPI (name+description);
        /// the module keeps a per-target once-latch anyway so a wrong id can't chain-cast.
        /// FIELD-CONFIRMED 2026-07-12 (first run: one cast, stuck, never re-pushed).</summary>
        public const uint MortalFlame = 3643;
        /// <summary>Final Sting / Self-destruct aftermath — 10min reuse lockout (verified 2127).</summary>
        public const uint BrushWithDeath = 2127;
    }
}
