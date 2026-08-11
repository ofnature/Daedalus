using System;
using System.Collections.Generic;
using System.Linq;

namespace Daedalus.Data;

/// <summary>
/// One usable ether variant — a grade in a specific quality. NQ and HQ are separate entries
/// because they restore different amounts and are counted separately in the bags.
/// </summary>
/// <param name="ItemId">NQ item row id. The HQ dispatch id is this + <see cref="ConsumableIds.HqOffset"/>.</param>
/// <param name="Hq">Whether this entry is the high-quality variant.</param>
/// <param name="Percent">Fraction of max MP restored (sheet ItemAction Data[0] / DataHQ[0], as a percent).</param>
/// <param name="Cap">Absolute MP ceiling for the restore (Data[1] / DataHQ[1]).</param>
public readonly record struct EtherVariant(uint ItemId, bool Hq, int Percent, int Cap, string Name)
{
    /// <summary>What this actually returns at the given max MP — the sheet applies both terms.</summary>
    public int RestoreAt(uint maxMp) => Math.Min((int)(maxMp * Percent / 100f), Cap);

    /// <summary>Id to hand <c>IActionService.ExecuteItem</c> / <c>IInventoryProbe.GetItemCount</c>.</summary>
    public uint InventoryId => Hq ? ItemId + ConsumableIds.HqOffset : ItemId;

    public string DisplayName => Hq ? Name + " (HQ)" : Name;
}

/// <summary>
/// The MP-restore ether ladder, values read off the Item sheet (verified 2026-08-10 against
/// XIVAPI rows 4555/4556/4557/4558/13638/23168 — ItemAction Data and DataHQ).
/// <para>
/// The caps are tuned to a 10,000 MP pool, so at level cap every grade restores exactly its
/// cap; <see cref="EtherVariant.RestoreAt"/> still applies the percentage term so a lower-level
/// or lower-MP character is estimated correctly rather than over-promised.
/// </para>
/// <para>
/// NOTE the ladder is NOT grade order: <b>Max-Ether HQ (1,500) beats Super-Ether NQ (1,400)</b>,
/// and X-Ether HQ ties Max-Ether NQ. Cascading purely by grade would reach for a weaker item
/// while a stronger one sat in the bag, which is why <see cref="BestFirst"/> is sorted by what
/// each variant actually returns.
/// </para>
/// </summary>
public static class EtherItems
{
    public const uint Ether = 4555;
    public const uint HiEther = 4556;
    public const uint MegaEther = 4557;
    public const uint XEther = 4558;
    public const uint MaxEther = 13638;
    public const uint SuperEther = 23168;

    /// <summary>Every grade, NQ and HQ, with its sheet numbers.</summary>
    public static readonly IReadOnlyList<EtherVariant> All =
    [
        new(SuperEther, Hq: true,  Percent: 18, Cap: 1800, "Super-Ether"),
        new(SuperEther, Hq: false, Percent: 14, Cap: 1400, "Super-Ether"),
        new(MaxEther,   Hq: true,  Percent: 15, Cap: 1500, "Max-Ether"),
        new(MaxEther,   Hq: false, Percent: 12, Cap: 1200, "Max-Ether"),
        new(XEther,     Hq: true,  Percent: 12, Cap: 1200, "X-Ether"),
        new(XEther,     Hq: false, Percent: 10, Cap: 1000, "X-Ether"),
        new(MegaEther,  Hq: true,  Percent: 10, Cap: 1000, "Mega-Ether"),
        new(MegaEther,  Hq: false, Percent: 8,  Cap: 800,  "Mega-Ether"),
        new(HiEther,    Hq: true,  Percent: 9,  Cap: 900,  "Hi-Ether"),
        new(HiEther,    Hq: false, Percent: 7,  Cap: 700,  "Hi-Ether"),
        new(Ether,      Hq: true,  Percent: 8,  Cap: 800,  "Ether"),
        new(Ether,      Hq: false, Percent: 6,  Cap: 600,  "Ether"),
    ];

    /// <summary>
    /// The cascade order: strongest restore first. Ties break toward NQ, so an equal-value
    /// normal-quality item is burned before the high-quality one it matches (HQ is the scarcer
    /// stock and is worth keeping for when the ladder has thinned out).
    /// </summary>
    public static readonly IReadOnlyList<EtherVariant> BestFirst = All
        .OrderByDescending(v => v.Cap)
        .ThenBy(v => v.Hq)
        .ToList();
}
