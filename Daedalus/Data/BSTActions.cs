using System.Collections.Generic;

namespace Daedalus.Data;

/// <summary>
/// The four-colour Instinct cycle. Chaining clockwise — yellow, green, blue, red, yellow —
/// within the combo window scores the higher-damage "Intentional Combo"; an off-order chain
/// still combos, for less.
/// </summary>
public enum InstinctColor : byte
{
    None = 0,
    Yellow = 1,
    Green = 2,
    Blue = 3,
    Red = 4,
}

/// <summary>
/// Beast classifications. A pact's classification decides which Borrow action the beast can lend
/// the player, so this is the key the Borrow table will be built on once the data lands.
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

/// <summary>
/// What kind of thing a Beastmaster action is. The archetypes are known from the job's design
/// even though the individual actions are not, and the rotation's structure keys off these
/// rather than off names.
/// </summary>
public enum BstArchetype : byte
{
    Unknown = 0,

    /// <summary>Player-side instinctual action — carries an <see cref="InstinctColor"/>.</summary>
    Instinctual = 1,

    /// <summary>AoE that also retreats the beast and starts its Battlehorn cooldown.</summary>
    PartingBlow = 2,

    /// <summary>The active beast's instinctual skill, commanded by the player.</summary>
    Trick = 3,

    /// <summary>Beast-unique, once per summon.</summary>
    TemperedRelease = 4,

    /// <summary>Classification-based action lent to the player, once per summon.</summary>
    Borrow = 5,

    /// <summary>Out-of-combat capture flow: Gauge (check difficulty), Capture (apply tame).</summary>
    Capture = 6,
}

/// <summary>
/// Beastmaster action data — <b>deliberately empty</b>.
///
/// <para>
/// Verified 2026-09-08 against the game's own sheets: <c>ClassJob</c> row 43 exists and is named
/// beastmaster (BST), but <b>no BST actions are published</b> — searches for Parting Blow,
/// Tempered Release, Capture and Battlehorn all return nothing. This machine's client is on
/// 7.55; the job arrives in 7.56.
/// </para>
///
/// <para>
/// So this file carries the SHAPE and no invented numbers. Every id, potency, level and recast in
/// this job is currently unknown, and the one failure this scaffold must not permit is a
/// plausible-looking guess reaching a hotbar. <see cref="All"/> being empty is what keeps the
/// rotation inert: the learned/level gate has nothing to pass, so nothing is ever pushed.
/// </para>
///
/// <para>
/// <b>To populate when 7.56 lands</b>, same process as prior jobs: pull the Action sheet filtered
/// to ClassJob 43, take id / Name / ClassJobLevel / Recast100ms / Cast100ms / Range / EffectRange
/// from the sheet and the tooltip text from ActionTransient, then fill <see cref="All"/> with one
/// <see cref="BstActionDef"/> per action. Do not transcribe potencies from a wiki — read them off
/// ActionTransient, the way the phantom catalog was built.
/// </para>
/// </summary>
public static class BSTActions
{
    /// <summary>
    /// One Beastmaster action. Mirrors the phantom catalog's shape rather than
    /// <see cref="ActionDefinition"/>: the discriminating facts for this job are the archetype and
    /// the instinct colour, neither of which any other job has.
    /// </summary>
    /// <param name="ActionId">Real action id. Never 0 in a populated entry.</param>
    /// <param name="Name">Display name, for logs and the Missing window.</param>
    /// <param name="MinLevel">Level the action unlocks at (BST caps at 50).</param>
    /// <param name="Archetype">Which of the job's action kinds this is.</param>
    /// <param name="Color">Instinct colour for <see cref="BstArchetype.Instinctual"/>; None otherwise.</param>
    /// <param name="Classification">Owning classification for <see cref="BstArchetype.Borrow"/>.</param>
    public readonly record struct BstActionDef(
        uint ActionId,
        string Name,
        byte MinLevel,
        BstArchetype Archetype,
        InstinctColor Color = InstinctColor.None,
        BeastClassification Classification = BeastClassification.Unknown);

    /// <summary>
    /// EMPTY until 7.56 publishes the action sheet. See the class remarks — an empty table is the
    /// scaffold's safety property, not an oversight.
    /// </summary>
    public static readonly IReadOnlyList<BstActionDef> All = [];

    /// <summary>The job's level cap. Limited jobs cap below the game's cap; BST caps at 50.</summary>
    public const byte LevelCap = 50;

    /// <summary>
    /// Clockwise successor in the instinct cycle — the order an Intentional Combo has to follow.
    /// Pure and knowable without the action data, so it is real rather than stubbed.
    /// </summary>
    public static InstinctColor NextClockwise(InstinctColor color) => color switch
    {
        InstinctColor.Yellow => InstinctColor.Green,
        InstinctColor.Green => InstinctColor.Blue,
        InstinctColor.Blue => InstinctColor.Red,
        InstinctColor.Red => InstinctColor.Yellow,
        _ => InstinctColor.None,
    };
}
