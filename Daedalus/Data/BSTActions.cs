using System.Collections.Generic;
using Daedalus.Models.Action;

namespace Daedalus.Data;

/// <summary>
/// Instinctual affinity. Each instinctual weaponskill has one and grants the matching
/// "&lt;affinity&gt; Heart" buff for 7s; chaining into the successor completes an intentional combo.
/// <para>
/// The cycle is <b>Volant → Rampant → Durant → Eldritch → Volant</b>, read directly off the
/// action tooltips in this client's sheets (2026-09-08). It is a four-cycle, but the affinities
/// are NAMED, not colour-coded — the scaffold's original yellow/green/blue/red model was wrong.
/// </para>
/// </summary>
public enum InstinctAffinity : byte
{
    None = 0,

    // ── tier 1: the four Hearts ───────────────────────────────────────────────────────────
    Volant = 1,
    Rampant = 2,
    Durant = 3,
    Eldritch = 4,

    // ── tier 2: the upgraded skills ───────────────────────────────────────────────────────
    // Sunstrider and Moonstalker are BOTH the names of the tier-1 combos AND affinities in their
    // own right, carried by the upgraded instinctual skills (Brutal Rage, Hawkish Talons, Risen
    // Fall, Calamity). Chaining one into the other completes the infinitive combo Universality.
    // The naming collision is the game's, not ours — see IntentionalCombo.
    Sunstrider = 5,
    Moonstalker = 6,
}

/// <summary>Which rung of the instinct ladder an affinity sits on.</summary>
public enum InstinctTier : byte
{
    None = 0,

    /// <summary>Volant / Rampant / Durant / Eldritch — the four Hearts.</summary>
    Hearts = 1,

    /// <summary>Sunstrider / Moonstalker — carried by the upgraded instinctual skills.</summary>
    Upgraded = 2,
}

/// <summary>
/// The two intentional combos. Which one a chain completes depends on the transition, and they
/// alternate around the cycle: Volant→Rampant and Durant→Eldritch are Sunstrider;
/// Rampant→Durant and Eldritch→Volant are Moonstalker.
/// </summary>
public enum IntentionalCombo : byte
{
    None = 0,

    /// <summary>Tier 1: Volant→Rampant, Durant→Eldritch.</summary>
    Sunstrider = 1,

    /// <summary>Tier 1: Rampant→Durant, Eldritch→Volant.</summary>
    Moonstalker = 2,

    /// <summary>
    /// Tier 2, and the game calls it an <i>infinitive</i> combo rather than an intentional one:
    /// chaining Sunstrider into Moonstalker or back again, with the upgraded skills. "Dealing
    /// greater additional damage" — the top of the ladder.
    /// </summary>
    Universality = 3,
}

/// <summary>
/// Familiar classification. <c>Borrow</c> converts the active familiar's classification into the
/// matching Kinship, which in turn changes what <c>Beast Mode</c> does. Verified against the
/// Borrow and Beast Mode tooltips.
/// </summary>
public enum BeastClassification : byte
{
    Unknown = 0,
    Beastkin = 1,
    Wavekin = 2,
    Vilekin = 3,
    Scalekin = 4,
    Cloudkin = 5,
    Soulkin = 6,
    Seedkin = 7,
    Ashkin = 8,
}

/// <summary>What kind of thing a Beastmaster action is.</summary>
public enum BstArchetype : byte
{
    Unknown = 0,

    /// <summary>The Smash Axe → Axeblade Bite → Shieldsplitter combo chain.</summary>
    ComboWeaponskill = 1,

    /// <summary>Instinctual weaponskill — carries an <see cref="InstinctAffinity"/>, spends TP.</summary>
    Instinctual = 2,

    /// <summary>Summons a familiar. Three separate actions, one per Battlehorn slot.</summary>
    Battlehorn = 3,

    /// <summary>Orders the familiar's Aetheric Burst; the familiar then retreats.</summary>
    PartingBlow = 4,

    /// <summary>Orders the familiar's instinctual skill; grants one of the four Hearts.</summary>
    Trick = 5,

    /// <summary>Familiar-unique, requires One with Nature.</summary>
    TemperedRelease = 6,

    /// <summary>Grants Kinship from the familiar's classification; requires One with Nature.</summary>
    Borrow = 7,

    /// <summary>Changes according to the Kinship granted by Borrow.</summary>
    BeastMode = 8,

    /// <summary>Out-of-combat capture flow: Gauge checks odds, Capture applies Interest Captured.</summary>
    Capture = 9,

    /// <summary>Spends Instinct stacks to refill a TP gauge.</summary>
    Rally = 10,

    /// <summary>
    /// Shield Charge — a 20y forward rush with a 6y burst. A DASH, so it must go through the same
    /// leap safety as every other gap closer (pit floors, hazard destinations) rather than being
    /// treated as a plain attack.
    /// </summary>
    GapCloser = 11,

    /// <summary>Everything else.</summary>
    Other = 12,
}

/// <summary>
/// Beastmaster action data, harvested from <b>this client's own Excel sheets</b> on 2026-09-08 via
/// <c>/dae dumpjob 43</c> — XIVAPI still has nothing. 20 actions, ClassJob 43.
///
/// <para>
/// Potencies below are transcribed from ActionTransient verbatim. Two rendered <b>blank in the
/// dump</b> — Smash Axe as "a potency of ." and Shield Charge as "a potency of ". <b>Both have
/// since been read off the in-game tooltips</b> (100 and 220 respectively), so no potency in this
/// catalog is a guess.
/// </para>
///
/// <para>
/// <b>Known incomplete.</b> The dump covers every action keyed to ClassJob 43 and tops out at
/// Lv40 (Rallying Cheer), while the job caps at 50. Note Trick is id 47093 against everything
/// else's 448xx block, so BST actions are not contiguous — any Lv41-50 actions are likely keyed
/// by ClassJobCategory rather than ClassJob and will need a second sweep. The familiar's own
/// skills (what Trick and Parting Blow order) are not here either; they belong to the pet.
/// </para>
/// </summary>
public static class BSTActions
{
    /// <param name="ActionId">Row id.</param>
    /// <param name="Name">Display name.</param>
    /// <param name="MinLevel">Unlock level.</param>
    /// <param name="Archetype">Which kind of action this is.</param>
    /// <param name="Potency">Base potency; 0 where the sheet does not state one.</param>
    /// <param name="ComboPotency">Potency when the combo condition is met; 0 if not a combo action.</param>
    /// <param name="Affinity">Instinct affinity for <see cref="BstArchetype.Instinctual"/>.</param>
    /// <param name="RecastSeconds">Recast from the sheet.</param>
    /// <param name="ComboTpGain">
    /// TP the player's gauge gains when the combo condition is met; 0 if it grants none. This is
    /// the job's TP generator — instinctual skills need <see cref="InstinctualMinTpCost"/> before
    /// they can be pressed at all — so the rotation counts combos to reach that floor.
    /// </param>
    /// <param name="RangeYalms">Range from the tooltip. The melee chain is 3y; the pet orders are not.</param>
    /// <param name="EffectRadiusYalms">AoE radius around the target; 0 for single target.</param>
    /// <param name="IndependentRecast">
    /// <b>The action's recast is its own — pressing it does NOT consume the GCD.</b> Stated verbatim
    /// on every instinctual skill: "Instinctual skills do not share a recast timer with any other
    /// actions." They are Weaponskills with a 5s recast that runs in parallel to the 2.5s combo
    /// chain, so the rotation must dispatch them like oGCDs. Reading them as GCDs would halve the
    /// job's throughput.
    /// </param>
    public readonly record struct BstActionDef(
        uint ActionId,
        string Name,
        byte MinLevel,
        BstArchetype Archetype,
        int Potency = 0,
        int ComboPotency = 0,
        InstinctAffinity Affinity = InstinctAffinity.None,
        float RecastSeconds = 0f,
        int ComboTpGain = 0,
        float RangeYalms = 0f,
        float EffectRadiusYalms = 0f,
        bool IndependentRecast = false);

    /// <summary>The job's level cap.</summary>
    public const byte LevelCap = 50;

    /// <summary>
    /// Instinctual skills spend the whole TP gauge and scale with it: 400 at the 100 minimum,
    /// rising to 1,000 as TP nears maximum. Stated identically on all four.
    /// </summary>
    public const int InstinctualMinPotency = 400;

    /// <summary>Potency at a full TP gauge.</summary>
    public const int InstinctualMaxPotency = 1000;

    /// <summary>
    /// TP the UPGRADED instinctual skills cost (Brutal Rage / Hawkish Talons / Risen Fall /
    /// Calamity) — stated as "TP Gauge Cost: 250", a flat cost rather than the base skills'
    /// "minimum". It also bounds the gauge: the maximum is at least 250, and the JobHudXBM0
    /// readout is three digits, so the maximum lies between 250 and 999.
    /// </summary>
    public const int UpgradedInstinctualTpCost = 250;

    /// <summary>Potency of every upgraded instinctual skill — all four state 1,200 in a 6y burst.</summary>
    public const int UpgradedInstinctualPotency = 1200;

    /// <summary>
    /// Minimum TP the instinctual skills and Trick require. Stated as "Minimum TP Gauge Cost: 100"
    /// on both; the instinctual skills spend the PLAYER's gauge, Trick the FAMILIAR's.
    /// </summary>
    public const int InstinctualMinTpCost = 100;

    /// <summary>
    /// Familiar TP that Parting Blow grants ("Increases Familiar TP Gauge by 24"). Distinct from
    /// the combo chain's player-side <c>ComboTpGain</c> — two different gauges, two different
    /// numbers, and conflating them would mis-time Trick.
    /// </summary>
    public const int PartingBlowFamiliarTpGain = 24;

    /// <summary>Rally's base player-TP refill, before Mastered Instinct stacks.</summary>
    public const int RallyBaseTpGain = 40;

    /// <summary>Rallying Cheer's base familiar-TP refill, before Natural Instinct stacks.</summary>
    public const int RallyingCheerBaseTpGain = 30;

    /// <summary>
    /// Each Mastered Instinct (Rally) or Natural Instinct (Rallying Cheer) stack is worth this
    /// much TP — the same 70 on both, even though their base refills differ (40 vs 30).
    /// </summary>
    public const int InstinctStackTpGain = 70;

    /// <summary>
    /// Borrow's Kinship lasts 90s, but "expiration timer is halted while your familiar is
    /// summoned" — so it only burns down once the familiar is gone.
    /// </summary>
    public const float KinshipDurationSeconds = 90f;

    /// <summary>
    /// The Kinship that Borrow grants for a familiar of this classification. One-to-one, verified
    /// against the Borrow tooltip: Beastkin→Beast, Vilekin→Vile, Cloudkin→Cloud, Seedkin→Seed,
    /// Wavekin→Wave, Scalekin→Scale, Soulkin→Soul, Ashkin→Ash. Beast Mode then becomes an action
    /// determined by that Kinship, so this is what tells the rotation what Beast Mode will DO.
    /// </summary>
    public static string KinshipEffectFor(BeastClassification classification) => classification switch
    {
        BeastClassification.Beastkin => "reduces physical vulnerability",
        BeastClassification.Vilekin => "increases block rate",
        BeastClassification.Cloudkin => "bolsters movement",
        BeastClassification.Seedkin => "inflicts poison and weakening effects",
        BeastClassification.Wavekin => "deals ranged magic damage to a single target",
        BeastClassification.Scalekin => "absorbs magic damage",
        BeastClassification.Soulkin => "interrupts a single target",
        BeastClassification.Ashkin => "removes a detrimental effect",
        _ => "unknown",
    };

    /// <summary>Every Heart lasts 7 seconds — stated on all four tooltips, so this is measured, not assumed.</summary>
    public const float HeartDurationSeconds = 7f;

    public static readonly IReadOnlyList<BstActionDef> All =
    [
        // ── the basic combo chain ─────────────────────────────────────────────────────────
        // Potency 100 read off the in-game tooltip 2026-09-08; the sheet dump rendered it blank
        // ("a potency of ."), so this is the tooltip's number, not the dump's.
        new(44879, "Smash Axe", 1, BstArchetype.ComboWeaponskill, Potency: 100, RecastSeconds: 2.5f, RangeYalms: 3f),
        new(44883, "Axeblade Bite", 2, BstArchetype.ComboWeaponskill, Potency: 260, ComboPotency: 500, RecastSeconds: 2.5f, ComboTpGain: 13, RangeYalms: 3f),
        new(44885, "Shieldsplitter", 12, BstArchetype.ComboWeaponskill, Potency: 280, ComboPotency: 600, RecastSeconds: 2.5f, ComboTpGain: 15, RangeYalms: 3f),

        // ── instinctual skills ────────────────────────────────────────────────────────────
        // Weaponskills, but they do NOT consume the GCD — see IndependentRecast. Verified on the
        // Avalanche Axe and Mistral Axe tooltips 2026-09-08.
        new(44884, "Avalanche Axe", 4, BstArchetype.Instinctual, Potency: InstinctualMinPotency, Affinity: InstinctAffinity.Rampant, RecastSeconds: 5f, RangeYalms: 3f, IndependentRecast: true),
        new(44887, "Mistral Axe", 8, BstArchetype.Instinctual, Potency: InstinctualMinPotency, Affinity: InstinctAffinity.Durant, RecastSeconds: 5f, RangeYalms: 3f, IndependentRecast: true),
        new(44888, "Spinning Axe", 14, BstArchetype.Instinctual, Potency: InstinctualMinPotency, Affinity: InstinctAffinity.Eldritch, RecastSeconds: 5f, RangeYalms: 3f, IndependentRecast: true),
        new(44889, "Gale Axe", 16, BstArchetype.Instinctual, Potency: InstinctualMinPotency, Affinity: InstinctAffinity.Volant, RecastSeconds: 5f, RangeYalms: 3f, IndependentRecast: true),

        // ── familiars ────────────────────────────────────────────────────────────────────
        new(44881, "First Battlehorn", 1, BstArchetype.Battlehorn, RecastSeconds: 2f),
        new(44892, "Second Battlehorn", 10, BstArchetype.Battlehorn, RecastSeconds: 2f),
        new(44894, "Third Battlehorn", 20, BstArchetype.Battlehorn, RecastSeconds: 2f),
        // Trick orders the FAMILIAR's instinctual skill and grants one of the four Hearts — which
        // one depends on the familiar, so it is not fixed to an affinity here.
        new(47093, "Trick", 8, BstArchetype.Trick, RecastSeconds: 3f, RangeYalms: 30f),
        // Parting Blow is an 8y AoE around the target, combat-only, and the familiar retreats after.
        new(44891, "Parting Blow", 6, BstArchetype.PartingBlow, Potency: 1000, RecastSeconds: 10f, RangeYalms: 25f, EffectRadiusYalms: 8f),
        new(44890, "Tempered Release", 18, BstArchetype.TemperedRelease, RecastSeconds: 30f),
        new(44895, "Borrow", 22, BstArchetype.Borrow, RecastSeconds: 30f),
        new(44886, "Beast Mode", 22, BstArchetype.BeastMode, RecastSeconds: 2.5f),

        // ── capture ──────────────────────────────────────────────────────────────────────
        new(44882, "Gauge", 1, BstArchetype.Capture, RecastSeconds: 3f),
        new(44880, "Capture", 1, BstArchetype.Capture, Potency: 100, RecastSeconds: 2.5f),

        // ── utility ──────────────────────────────────────────────────────────────────────
        // Gap closer: "Rushes forward and delivers an attack to target and all enemies nearby it."
        // Cannot be executed while bound.
        new(44893, "Shield Charge", 24, BstArchetype.GapCloser, Potency: 220, RecastSeconds: 60f, RangeYalms: 20f, EffectRadiusYalms: 6f),
        new(44905, "Rally", 28, BstArchetype.Rally, RecastSeconds: 120f),
        new(44904, "Rallying Cheer", 40, BstArchetype.Rally, RecastSeconds: 120f),
    ];

    /// <summary>
    /// The affinity that completes an intentional combo after <paramref name="previous"/>.
    /// Cycle: Volant → Rampant → Durant → Eldritch → Volant.
    /// </summary>
    public static InstinctAffinity NextInCycle(InstinctAffinity previous) => previous switch
    {
        InstinctAffinity.Volant => InstinctAffinity.Rampant,
        InstinctAffinity.Rampant => InstinctAffinity.Durant,
        InstinctAffinity.Durant => InstinctAffinity.Eldritch,
        InstinctAffinity.Eldritch => InstinctAffinity.Volant,

        // Tier 2 is a two-cycle, not a four-cycle: the two upgraded affinities simply alternate.
        InstinctAffinity.Sunstrider => InstinctAffinity.Moonstalker,
        InstinctAffinity.Moonstalker => InstinctAffinity.Sunstrider,

        _ => InstinctAffinity.None,
    };

    /// <summary>Which rung of the ladder an affinity sits on.</summary>
    public static InstinctTier TierOf(InstinctAffinity affinity) => affinity switch
    {
        InstinctAffinity.Volant or InstinctAffinity.Rampant
            or InstinctAffinity.Durant or InstinctAffinity.Eldritch => InstinctTier.Hearts,
        InstinctAffinity.Sunstrider or InstinctAffinity.Moonstalker => InstinctTier.Upgraded,
        _ => InstinctTier.None,
    };

    /// <summary>
    /// Which intentional combo the transition scores, or None when it is not a valid one.
    /// The two alternate around the cycle.
    /// </summary>
    public static IntentionalCombo ComboFor(InstinctAffinity previous, InstinctAffinity next)
    {
        if (next == InstinctAffinity.None || NextInCycle(previous) != next)
            return IntentionalCombo.None;

        return previous switch
        {
            InstinctAffinity.Volant => IntentionalCombo.Sunstrider,     // → Rampant
            InstinctAffinity.Durant => IntentionalCombo.Sunstrider,     // → Eldritch
            InstinctAffinity.Rampant => IntentionalCombo.Moonstalker,   // → Durant
            InstinctAffinity.Eldritch => IntentionalCombo.Moonstalker,  // → Volant

            // Either direction across the tier-2 pair completes Universality.
            InstinctAffinity.Sunstrider or InstinctAffinity.Moonstalker => IntentionalCombo.Universality,

            _ => IntentionalCombo.None,
        };
    }

    #region ActionDefinitions the rotation fires

    // The records above are the HARVEST — the transcribed facts, and the test surface. These are
    // the same actions in the shape the scheduler consumes. ArtemisCatalogTests asserts the two
    // agree on id, level, potency, recast and range, so they cannot drift apart.
    //
    // Battlehorns are deliberately absent: which familiar to summon is the player's choice, made by
    // assigning slots, and the rotation has no business overriding it.

    /// <summary>Smash Axe — combo starter (Lv1).</summary>
    public static readonly ActionDefinition SmashAxe = new()
    {
        ActionId = 44879, Name = "Smash Axe", MinLevel = 1,
        Category = ActionCategory.GCD, TargetType = ActionTargetType.SingleEnemy,
        EffectTypes = ActionEffectType.Damage,
        RecastTime = 2.5f, Range = 3f, DamagePotency = 100,
    };

    /// <summary>Axeblade Bite — combo from Smash Axe; +13 player TP (Lv2).</summary>
    public static readonly ActionDefinition AxebladeBite = new()
    {
        ActionId = 44883, Name = "Axeblade Bite", MinLevel = 2,
        Category = ActionCategory.GCD, TargetType = ActionTargetType.SingleEnemy,
        EffectTypes = ActionEffectType.Damage,
        RecastTime = 2.5f, Range = 3f, DamagePotency = 260,
    };

    /// <summary>Shieldsplitter — combo from Axeblade Bite; +15 player TP (Lv12).</summary>
    public static readonly ActionDefinition Shieldsplitter = new()
    {
        ActionId = 44885, Name = "Shieldsplitter", MinLevel = 12,
        Category = ActionCategory.GCD, TargetType = ActionTargetType.SingleEnemy,
        EffectTypes = ActionEffectType.Damage,
        RecastTime = 2.5f, Range = 3f, DamagePotency = 280,
    };

    // The four instinctual skills. Weaponskills in game, but Category is oGCD ON PURPOSE — their
    // recast is independent of the global, so the scheduler must weave them rather than spend a
    // GCD slot on them. See BstActionDef.IndependentRecast.

    /// <summary>Avalanche Axe — Rampant (Lv4).</summary>
    public static readonly ActionDefinition AvalancheAxe = new()
    {
        ActionId = 44884, Name = "Avalanche Axe", MinLevel = 4,
        Category = ActionCategory.oGCD, TargetType = ActionTargetType.SingleEnemy,
        EffectTypes = ActionEffectType.Damage,
        RecastTime = 5f, Range = 3f, DamagePotency = InstinctualMinPotency,
    };

    /// <summary>Mistral Axe — Durant (Lv8).</summary>
    public static readonly ActionDefinition MistralAxe = new()
    {
        ActionId = 44887, Name = "Mistral Axe", MinLevel = 8,
        Category = ActionCategory.oGCD, TargetType = ActionTargetType.SingleEnemy,
        EffectTypes = ActionEffectType.Damage,
        RecastTime = 5f, Range = 3f, DamagePotency = InstinctualMinPotency,
    };

    /// <summary>Spinning Axe — Eldritch (Lv14).</summary>
    public static readonly ActionDefinition SpinningAxe = new()
    {
        ActionId = 44888, Name = "Spinning Axe", MinLevel = 14,
        Category = ActionCategory.oGCD, TargetType = ActionTargetType.SingleEnemy,
        EffectTypes = ActionEffectType.Damage,
        RecastTime = 5f, Range = 3f, DamagePotency = InstinctualMinPotency,
    };

    /// <summary>Gale Axe — Volant (Lv16).</summary>
    public static readonly ActionDefinition GaleAxe = new()
    {
        ActionId = 44889, Name = "Gale Axe", MinLevel = 16,
        Category = ActionCategory.oGCD, TargetType = ActionTargetType.SingleEnemy,
        EffectTypes = ActionEffectType.Damage,
        RecastTime = 5f, Range = 3f, DamagePotency = InstinctualMinPotency,
    };

    /// <summary>Trick — orders the familiar's instinctual skill; 30y, spends FAMILIAR TP (Lv8).</summary>
    public static readonly ActionDefinition Trick = new()
    {
        ActionId = 47093, Name = "Trick", MinLevel = 8,
        Category = ActionCategory.oGCD, TargetType = ActionTargetType.SingleEnemy,
        EffectTypes = ActionEffectType.Damage,
        RecastTime = 3f, Range = 30f,
    };

    /// <summary>Parting Blow — Aetheric Burst, 1,000 potency in 8y; the familiar retreats (Lv6).</summary>
    public static readonly ActionDefinition PartingBlow = new()
    {
        ActionId = 44891, Name = "Parting Blow", MinLevel = 6,
        Category = ActionCategory.oGCD, TargetType = ActionTargetType.SingleEnemy,
        EffectTypes = ActionEffectType.Damage,
        RecastTime = 10f, Range = 25f, Radius = 8f, DamagePotency = 1000,
    };

    /// <summary>Gauge — scans a beast's capture odds. Out of combat, 3s recast (Lv1).</summary>
    public static readonly ActionDefinition Gauge = new()
    {
        ActionId = 44882, Name = "Gauge", MinLevel = 1,
        Category = ActionCategory.oGCD, TargetType = ActionTargetType.SingleEnemy,
        RecastTime = 3f, Range = 25f,
    };

    /// <summary>
    /// Capture — applies Interest Captured for 120s; the capture succeeds if the beast is defeated
    /// while it is live (Lv1).
    /// </summary>
    public static readonly ActionDefinition Capture = new()
    {
        ActionId = 44880, Name = "Capture", MinLevel = 1,
        Category = ActionCategory.GCD, TargetType = ActionTargetType.SingleEnemy,
        EffectTypes = ActionEffectType.Damage,
        RecastTime = 2.5f, Range = 3f, DamagePotency = 100,
    };

    /// <summary>
    /// How long Interest Captured lasts. The kill must land inside this window, which is what the
    /// auto-capture timing is built around.
    /// </summary>
    public const float CaptureWindowSeconds = 120f;

    /// <summary>The instinctual skill carrying each affinity, or null for None.</summary>
    public static ActionDefinition? InstinctualFor(InstinctAffinity affinity) => affinity switch
    {
        InstinctAffinity.Volant => GaleAxe,
        InstinctAffinity.Rampant => AvalancheAxe,
        InstinctAffinity.Durant => MistralAxe,
        InstinctAffinity.Eldritch => SpinningAxe,
        _ => null,
    };

    /// <summary>The affinity an instinctual action id carries, or None.</summary>
    public static InstinctAffinity AffinityOf(uint actionId) => actionId switch
    {
        44889 => InstinctAffinity.Volant,
        44884 => InstinctAffinity.Rampant,
        44887 => InstinctAffinity.Durant,
        44888 => InstinctAffinity.Eldritch,
        _ => InstinctAffinity.None,
    };

    #endregion

    /// <summary>Find a definition by id, or null.</summary>
    public static BstActionDef? ById(uint actionId)
    {
        foreach (var a in All)
        {
            if (a.ActionId == actionId)
                return a;
        }

        return null;
    }
}
