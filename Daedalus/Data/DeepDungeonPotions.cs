using System.Collections.Generic;

namespace Daedalus.Data;

/// <summary>One deep dungeon's own regen potion.</summary>
public readonly record struct DeepDungeonPotion(uint ItemId, string Name);

/// <summary>
/// The regen potion each deep dungeon hands out. Item sheet rows 20309/23163/38944/47102, read off XIVAPI 2026-11-24:
/// all four grant Rehabilitation for 30s on a 15s recast, have no high-quality version, and are "only available for
/// use in" their own dungeon — so the dungeon picks the potion, never the bag.
/// </summary>
public static class DeepDungeonPotions
{
    public const uint Sustaining = 20309;
    public const uint Empyrean = 23163;
    public const uint Orthos = 38944;
    public const uint Pilgrims = 47102;

    /// <summary>Floor effect: "Unable to use normal items or certain dungeon-specific items."</summary>
    public const uint ItemPenaltyStatusId = 1094;

    /// <summary>
    /// A regen already on the character: every Status row named Rehabilitation (which one a potion applies differs by
    /// dungeon — 648 in the Palace, 4191 in Pilgrim's Traverse), plus Anointed (4587), the Traverse's own.
    /// </summary>
    public static readonly IReadOnlySet<uint> RegenStatusIds = new HashSet<uint>
    {
        279, 479, 648, 989, 1014, 1263, 1687, 3367, 3492, 4191, 5394,
        4587,
    };

    /// <summary>The potion for InstanceContentDeepDungeon.DeepDungeonId; null outside a deep dungeon.</summary>
    public static DeepDungeonPotion? ForDungeon(byte deepDungeonId) => deepDungeonId switch
    {
        1 => new(Sustaining, "Sustaining Potion"), // Palace of the Dead
        2 => new(Empyrean, "Empyrean Potion"),     // Heaven-on-High
        3 => new(Orthos, "Orthos Potion"),         // Eureka Orthos
        4 => new(Pilgrims, "Pilgrim's Potion"),    // Pilgrim's Traverse
        _ => null,
    };
}
