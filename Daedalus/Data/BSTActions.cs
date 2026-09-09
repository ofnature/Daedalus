using System.Collections.Generic;

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
    Volant = 1,
    Rampant = 2,
    Durant = 3,
    Eldritch = 4,
}

/// <summary>
/// The two intentional combos. Which one a chain completes depends on the transition, and they
/// alternate around the cycle: Volant→Rampant and Durant→Eldritch are Sunstrider;
/// Rampant→Durant and Eldritch→Volant are Moonstalker.
/// </summary>
public enum IntentionalCombo : byte
{
    None = 0,
    Sunstrider = 1,
    Moonstalker = 2,
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

    /// <summary>Everything else — gap closer, utility.</summary>
    Other = 11,
}

/// <summary>
/// Beastmaster action data, harvested from <b>this client's own Excel sheets</b> on 2026-09-08 via
/// <c>/dae dumpjob 43</c> — XIVAPI still has nothing. 20 actions, ClassJob 43.
///
/// <para>
/// Potencies below are transcribed from ActionTransient verbatim. Two are <b>blank in the sheet
/// itself</b> — Smash Axe reads "a potency of ." and Shield Charge "a potency of " — so they are
/// recorded as 0 with a note rather than guessed.
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
    public readonly record struct BstActionDef(
        uint ActionId,
        string Name,
        byte MinLevel,
        BstArchetype Archetype,
        int Potency = 0,
        int ComboPotency = 0,
        InstinctAffinity Affinity = InstinctAffinity.None,
        float RecastSeconds = 0f);

    /// <summary>The job's level cap.</summary>
    public const byte LevelCap = 50;

    /// <summary>
    /// Instinctual skills spend the whole TP gauge and scale with it: 400 at the 100 minimum,
    /// rising to 1,000 as TP nears maximum. Stated identically on all four.
    /// </summary>
    public const int InstinctualMinPotency = 400;

    /// <summary>Potency at a full TP gauge.</summary>
    public const int InstinctualMaxPotency = 1000;

    /// <summary>Minimum TP the instinctual skills and Trick require.</summary>
    public const int InstinctualMinTpCost = 100;

    /// <summary>Every Heart lasts 7 seconds — stated on all four tooltips, so this is measured, not assumed.</summary>
    public const float HeartDurationSeconds = 7f;

    public static readonly IReadOnlyList<BstActionDef> All =
    [
        // ── the basic combo chain ─────────────────────────────────────────────────────────
        // Smash Axe's potency is BLANK in the sheet ("a potency of ."), not zero. Do not
        // invent one — re-read the tooltip after the next patch.
        new(44879, "Smash Axe", 1, BstArchetype.ComboWeaponskill, Potency: 0, RecastSeconds: 2.5f),
        new(44883, "Axeblade Bite", 2, BstArchetype.ComboWeaponskill, Potency: 260, ComboPotency: 500, RecastSeconds: 2.5f),
        // Combo bonus: +15 TP gauge.
        new(44885, "Shieldsplitter", 12, BstArchetype.ComboWeaponskill, Potency: 280, ComboPotency: 600, RecastSeconds: 2.5f),

        // ── instinctual skills — the four affinities, 5s recast, off the GCD ──────────────
        new(44884, "Avalanche Axe", 4, BstArchetype.Instinctual, Potency: InstinctualMinPotency, Affinity: InstinctAffinity.Rampant, RecastSeconds: 5f),
        new(44887, "Mistral Axe", 8, BstArchetype.Instinctual, Potency: InstinctualMinPotency, Affinity: InstinctAffinity.Durant, RecastSeconds: 5f),
        new(44888, "Spinning Axe", 14, BstArchetype.Instinctual, Potency: InstinctualMinPotency, Affinity: InstinctAffinity.Eldritch, RecastSeconds: 5f),
        new(44889, "Gale Axe", 16, BstArchetype.Instinctual, Potency: InstinctualMinPotency, Affinity: InstinctAffinity.Volant, RecastSeconds: 5f),

        // ── familiars ────────────────────────────────────────────────────────────────────
        new(44881, "First Battlehorn", 1, BstArchetype.Battlehorn, RecastSeconds: 2f),
        new(44892, "Second Battlehorn", 10, BstArchetype.Battlehorn, RecastSeconds: 2f),
        new(44894, "Third Battlehorn", 20, BstArchetype.Battlehorn, RecastSeconds: 2f),
        new(47093, "Trick", 8, BstArchetype.Trick, RecastSeconds: 3f),
        new(44891, "Parting Blow", 6, BstArchetype.PartingBlow, Potency: 1000, RecastSeconds: 10f),
        new(44890, "Tempered Release", 18, BstArchetype.TemperedRelease, RecastSeconds: 30f),
        new(44895, "Borrow", 22, BstArchetype.Borrow, RecastSeconds: 30f),
        new(44886, "Beast Mode", 22, BstArchetype.BeastMode, RecastSeconds: 2.5f),

        // ── capture ──────────────────────────────────────────────────────────────────────
        new(44882, "Gauge", 1, BstArchetype.Capture, RecastSeconds: 3f),
        new(44880, "Capture", 1, BstArchetype.Capture, Potency: 100, RecastSeconds: 2.5f),

        // ── utility ──────────────────────────────────────────────────────────────────────
        // Shield Charge's potency is BLANK in the sheet, same as Smash Axe.
        new(44893, "Shield Charge", 24, BstArchetype.Other, Potency: 0, RecastSeconds: 60f),
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
        _ => InstinctAffinity.None,
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
            _ => IntentionalCombo.None,
        };
    }

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
