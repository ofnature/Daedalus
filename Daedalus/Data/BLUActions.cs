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
    /// Ally-targeted, 25y, persists until death/zone. The Proteus role dropdown decides which
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

    /// <summary>Status ids — verified against the Status sheet (2026-07-02).</summary>
    public static class StatusIds
    {
        public const uint MightyGuard = 1719;
        public const uint Diamondback = 1722;
        /// <summary>Song of Torment's DoT.</summary>
        public const uint Bleeding = 1714;
        public const uint AethericMimicryTank = 2124;
        public const uint AethericMimicryDps = 2125;
        public const uint AethericMimicryHealer = 2126;
    }
}
