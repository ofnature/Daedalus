using System;
using System.Collections.Generic;
using Daedalus.Data;

namespace Daedalus.Services.Consumables;

/// <summary>Everything the ether decision needs, gathered live. Pure data so the policy is testable.</summary>
/// <param name="Stock">Inventory count per variant, keyed by <see cref="EtherVariant.InventoryId"/>.</param>
/// <param name="SecondsSinceOwnUse">Since the last successful ether — items share a short recast.</param>
/// <param name="SecondsSinceRefusal">Since the game last refused one (blocked duty, unseen recast).</param>
public readonly record struct EtherSituation(
    bool Enabled,
    bool Alive,
    bool UsesMp,
    uint CurrentMp,
    uint MaxMp,
    float MpThreshold,
    IReadOnlyDictionary<uint, uint> Stock,
    double SecondsSinceOwnUse,
    double SecondsSinceRefusal,
    bool IsCasting);

/// <summary>What the policy decided this tick.</summary>
/// <param name="Fire">Whether to use <paramref name="Choice"/> now.</param>
/// <param name="Choice">The variant to use — meaningless unless <paramref name="Fire"/>.</param>
public readonly record struct EtherDecision(bool Fire, EtherVariant Choice, string Reason);

/// <summary>
/// Cascading ether use: strongest restore first, falling down the ladder as stock runs out,
/// with a running-low warning before the bag is actually empty.
/// <para>
/// Motivated by the field problem this was built for — a healer in the Occult Crescent raising
/// repeatedly bottoms out near 200 MP, because a raise costs roughly 2,400 MP and Lucid Dreaming
/// only returns 3,850 per minute. Note the Occult phantom kit cannot cover this: Occult Ether is
/// a CHEMIST action, so a Phantom White Mage (the job carrying the instant Occult Raise) has no
/// phantom MP tool at all. Real ethers are the cross-job answer.
/// </para>
/// </summary>
public static class EtherPolicy
{
    /// <summary>Items share a recast the plugin cannot read directly — pace attempts instead.</summary>
    public const float RecastSeconds = 5f;

    /// <summary>After the game refuses a use (item-blocked duty), wait before trying again.</summary>
    public const float RefusalBackoffSeconds = 30f;

    /// <summary>Total ethers at or below this count raises the running-low warning.</summary>
    public const int RunningLowCount = 5;

    /// <summary>
    /// Picks the strongest ether actually held. Returns false when the bag is empty of every
    /// grade. Selection is by real restore value, not grade name — see <see cref="EtherItems.BestFirst"/>.
    /// </summary>
    public static bool TrySelect(IReadOnlyDictionary<uint, uint> stock, out EtherVariant choice)
    {
        foreach (var variant in EtherItems.BestFirst)
        {
            if (stock.TryGetValue(variant.InventoryId, out var count) && count > 0)
            {
                choice = variant;
                return true;
            }
        }

        choice = default;
        return false;
    }

    /// <summary>Total ethers held across every grade and quality.</summary>
    public static int TotalHeld(IReadOnlyDictionary<uint, uint> stock)
    {
        var total = 0;
        foreach (var variant in EtherItems.All)
        {
            if (stock.TryGetValue(variant.InventoryId, out var count))
                total += (int)count;
        }

        return total;
    }

    /// <summary>
    /// Whether the stock has thinned enough to tell the player. Deliberately fires while some
    /// ethers remain — a warning at zero is a status report, not a warning.
    /// </summary>
    public static bool IsRunningLow(IReadOnlyDictionary<uint, uint> stock)
    {
        var total = TotalHeld(stock);
        return total > 0 && total <= RunningLowCount;
    }

    /// <summary>Ordered so the reason names the FIRST thing that would have to change.</summary>
    public static EtherDecision Decide(in EtherSituation s)
    {
        if (!s.Enabled)
            return new(false, default, "disabled in settings");
        if (!s.Alive)
            return new(false, default, "dead");
        if (!s.UsesMp || s.MaxMp == 0)
            return new(false, default, "job has no MP pool");

        var mpPct = (float)s.CurrentMp / s.MaxMp;
        if (mpPct >= s.MpThreshold)
            return new(false, default, $"MP {mpPct:P0} — above {s.MpThreshold:P0}");

        if (s.SecondsSinceOwnUse < RecastSeconds)
            return new(false, default, "item recast rolling");
        if (s.SecondsSinceRefusal < RefusalBackoffSeconds)
            return new(false, default, "backing off — the game refused an ether here");
        if (s.IsCasting)
            return new(false, default, "already casting");

        if (!TrySelect(s.Stock, out var choice))
            return new(false, default, "no ethers left");

        return new(true, choice, $"{choice.DisplayName} (+{choice.RestoreAt(s.MaxMp):N0} MP)");
    }
}
