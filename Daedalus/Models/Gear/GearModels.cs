using System;
using System.Collections.Generic;

namespace Daedalus.Models.Gear;

/// <summary>
/// Equipment slot in the game's EquippedItems container order (belt slot 5 removed from the
/// game — deliberately absent). Values ARE the container slot indices.
/// </summary>
public enum GearSlotId
{
    MainHand = 0,
    OffHand = 1,
    Head = 2,
    Body = 3,
    Hands = 4,
    Legs = 6,
    Feet = 7,
    Ears = 8,
    Neck = 9,
    Wrists = 10,
    RingR = 11,
    RingL = 12,
}

/// <summary>
/// BaseParam sheet row ids for the stats the optimizer cares about. Kept as constants (not an
/// enum) because they index directly into game attribute arrays and Lumina rows.
/// </summary>
public static class GearStatIds
{
    public const uint Strength = 1;
    public const uint Dexterity = 2;
    public const uint Vitality = 3;
    public const uint Intelligence = 4;
    public const uint Mind = 5;
    public const uint Piety = 6;
    public const uint Tenacity = 19;
    public const uint DirectHit = 22;
    public const uint CriticalHit = 27;
    public const uint Determination = 44;
    public const uint SkillSpeed = 45;
    public const uint SpellSpeed = 46;

    /// <summary>Substats a meld can carry (the sweep candidates, before job filtering).</summary>
    public static readonly uint[] MeldableSubstats =
        { CriticalHit, Determination, DirectHit, SkillSpeed, SpellSpeed, Tenacity, Piety };

    public static string Name(uint statId) => statId switch
    {
        Strength => "STR",
        Dexterity => "DEX",
        Vitality => "VIT",
        Intelligence => "INT",
        Mind => "MND",
        Piety => "Piety",
        Tenacity => "Ten",
        DirectHit => "DH",
        CriticalHit => "Crit",
        Determination => "Det",
        SkillSpeed => "SkS",
        SpellSpeed => "SpS",
        _ => $"BaseParam{statId}",
    };
}

/// <summary>One melded materia on a piece.</summary>
public sealed record MateriaMeld(
    uint StatId,
    int Value,
    int Grade,
    /// <summary>Grade-XI overmeld socket (4/5 on pentameld pieces) — fixed floor, never swept.</summary>
    bool IsFixedOvermeld);

/// <summary>One equipped piece with everything the window and optimizer need.</summary>
public sealed record GearPiece(
    GearSlotId Slot,
    uint ItemId,
    string Name,
    int Ilvl,
    /// <summary>Base (unmelded) stats from the Item sheet, BaseParam id → value.</summary>
    IReadOnlyDictionary<uint, int> BaseStats,
    /// <summary>Current melds in socket order.</summary>
    IReadOnlyList<MateriaMeld> Melds,
    /// <summary>Guaranteed materia sockets on the item (Item.MateriaSlotCount).</summary>
    int GuaranteedSockets,
    /// <summary>Pentameldable (Item.IsAdvancedMeldingPermitted).</summary>
    bool AdvancedMeldingPermitted,
    /// <summary>Per-stat cap for this piece (BaseParam id → cap). Total base+melds may not exceed.</summary>
    IReadOnlyDictionary<uint, int> Caps)
{
    /// <summary>
    /// Sockets the optimizer may sweep: every socket that can hold a grade-XII materia.
    /// Current BiS model (verified in-game 2026-07-22): guaranteed sockets + the FIRST overmeld
    /// take XII (+54); overmeld sockets 4/5 only take XI (+18) and stay fixed.
    /// </summary>
    public int SweepableSockets => AdvancedMeldingPermitted ? GuaranteedSockets + 1 : GuaranteedSockets;

    /// <summary>Wasted points for a stat: how far base + melds exceed this piece's cap (0 if under).</summary>
    public int OvercapWaste(uint statId)
    {
        var total = BaseStats.TryGetValue(statId, out var b) ? b : 0;
        foreach (var meld in Melds)
        {
            if (meld.StatId == statId)
                total += meld.Value;
        }

        return Caps.TryGetValue(statId, out var cap) && total > cap ? total - cap : 0;
    }
}

/// <summary>Immutable capture of the equipped set — the only thing UI/optimizer ever read.</summary>
public sealed record GearSnapshot(
    IReadOnlyList<GearPiece> Pieces,
    byte GenderId,
    uint JobId,
    DateTime CapturedUtc,
    /// <summary>Player level — selects the StatConversions level modifiers. Defaults to cap.</summary>
    int Level = 100,
    /// <summary>
    /// LIVE character attribute totals (PlayerState) — includes food and every other buff the
    /// gear model can't see. Preferred for display and GCD tiers; the optimizer still models
    /// gear + naked floor (its deltas are relative, so flat food bonuses wash out).
    /// </summary>
    IReadOnlyDictionary<uint, int>? LiveStats = null)
{
    public static readonly GearSnapshot Empty =
        new(Array.Empty<GearPiece>(), 0, 0, DateTime.MinValue);
}
