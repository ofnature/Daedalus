using System.Collections.Generic;
using Daedalus.Models.Gear;

namespace Daedalus.Services.Gear;

/// <summary>
/// Pure aggregation over a <see cref="GearSnapshot"/>: total per stat across all pieces with
/// per-piece cap clipping (a piece never contributes more than its cap for a stat — the game
/// discards the overflow, so the aggregate must too), plus the overcap report the UI colors red.
/// </summary>
public static class GearStatAggregator
{
    public sealed record OvercapEntry(GearSlotId Slot, uint StatId, int WastedPoints);

    public sealed record AggregateResult(
        IReadOnlyDictionary<uint, int> Totals,
        IReadOnlyList<OvercapEntry> Overcaps);

    public static AggregateResult Aggregate(GearSnapshot snapshot)
    {
        var totals = new Dictionary<uint, int>();
        var overcaps = new List<OvercapEntry>();

        foreach (var piece in snapshot.Pieces)
        {
            // Per-piece per-stat sum first, then clip at the piece cap, then add to the total.
            var pieceStats = new Dictionary<uint, int>(piece.BaseStats);
            foreach (var meld in piece.Melds)
            {
                pieceStats.TryGetValue(meld.StatId, out var current);
                pieceStats[meld.StatId] = current + meld.Value;
            }

            foreach (var (statId, value) in pieceStats)
            {
                var contribution = value;
                if (piece.Caps.TryGetValue(statId, out var cap) && value > cap)
                {
                    contribution = cap;
                    overcaps.Add(new OvercapEntry(piece.Slot, statId, value - cap));
                }

                totals.TryGetValue(statId, out var total);
                totals[statId] = total + contribution;
            }
        }

        return new AggregateResult(totals, overcaps);
    }
}
