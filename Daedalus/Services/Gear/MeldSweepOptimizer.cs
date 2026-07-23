using System;
using System.Collections.Generic;
using System.Linq;
using Daedalus.Data;
using Daedalus.Models.Gear;

namespace Daedalus.Services.Gear;

/// <summary>
/// The meld sweep (plan phase 5, pure — runs on a background Task against an immutable
/// snapshot). Reassigns every sweepable (grade-XII) socket across the job's candidate substats,
/// respecting per-piece caps, and ranks whole-set distributions with <see cref="MeldDpsModel"/> —
/// the SAME model as the breakpoint verdict.
///
/// Search shape: a global distribution is just "how many sockets per stat" (compositions of
/// N sockets into ≤4 stats — a few thousand). For each composition, sockets are placed onto
/// pieces by a marginal-value greedy (full 54s first, then partial cap remainders, then zero) —
/// optimal here because per-piece per-stat value is concave in socket count (54, 54, …,
/// remainder, 0). Fixed XI overmelds are a stat floor and are never reassigned.
/// </summary>
public static class MeldSweepOptimizer
{
    private const int MateriaXiiValue = 54;

    public sealed record SocketChange(GearSlotId Slot, int SocketIndex, uint FromStat, uint ToStat);

    public sealed record MeldPlan(
        IReadOnlyDictionary<GearSlotId, uint[]> SocketAssignments,
        IReadOnlyDictionary<uint, int> Totals,
        double DeltaPercent,
        IReadOnlyList<SocketChange> Changes,
        string Summary);

    public static IReadOnlyList<MeldPlan> Sweep(GearSnapshot snapshot, int topN = 3)
    {
        var priority = BalancePriorities.For(snapshot.JobId);
        var candidates = priority.Order.Concat(new[] { priority.SpeedStat }).Distinct().ToArray();
        var speedStat = priority.SpeedStat;
        var speedValued = priority.Order.Contains(speedStat);
        var level = snapshot.Level;

        // Per-piece sweep inputs: socket count + per-candidate headroom above base+fixed melds.
        var pieces = snapshot.Pieces
            .Where(p => p.SweepableSockets > 0)
            .Select(p => new PieceState(p, candidates))
            .ToArray();
        var totalSockets = pieces.Sum(p => p.Sockets);
        if (totalSockets == 0)
            return Array.Empty<MeldPlan>();

        // Baseline: totals with ONLY base stats + fixed overmelds (what the sweep builds on),
        // and the actual current totals (what deltas are measured against).
        var baseTotals = TotalsWithMelds(snapshot, meld => meld.IsFixedOvermeld);
        var currentTotals = TotalsWithMelds(snapshot, _ => true);
        var currentMult = MeldDpsModel.Multiplier(currentTotals, level, speedStat, speedValued);

        // Enumerate global socket-count compositions and score each via the greedy placement.
        var scored = new List<(double Mult, int[] Counts)>();
        foreach (var counts in Compositions(totalSockets, candidates.Length))
        {
            var effective = PlaceGreedy(pieces, candidates, counts, materialize: false, out _);
            var totals = MergeTotals(baseTotals, candidates, effective);
            scored.Add((MeldDpsModel.Multiplier(totals, level, speedStat, speedValued), (int[])counts.Clone()));
        }

        var plans = new List<MeldPlan>();
        foreach (var (_, counts) in scored.OrderByDescending(s => s.Mult).Take(topN * 3))
        {
            var effective = PlaceGreedy(pieces, candidates, counts, materialize: true, out var assignments);
            var totals = MergeTotals(baseTotals, candidates, effective);
            var delta = (MeldDpsModel.Multiplier(totals, level, speedStat, speedValued) / currentMult - 1.0) * 100.0;
            var plan = BuildPlan(snapshot, assignments, totals, delta);

            // Distinct by the actual change set — different compositions can collapse to the
            // same placement once caps clip them.
            if (plans.All(existing => existing.Summary != plan.Summary))
                plans.Add(plan);
            if (plans.Count >= topN)
                break;
        }

        return plans;
    }

    // ── internals ───────────────────────────────────────────────────────────

    private sealed class PieceState
    {
        public readonly GearPiece Piece;
        public readonly int Sockets;
        public readonly int MeldValue; // best grade this piece's ilvl can hold (+54 XII, +18 XI…)
        public readonly int[] Headroom; // per candidate index, above base+fixed

        public PieceState(GearPiece piece, uint[] candidates)
        {
            Piece = piece;
            Sockets = piece.SweepableSockets;
            MeldValue = Math.Max(1, piece.SweepMeldValue);
            Headroom = new int[candidates.Length];
            for (var i = 0; i < candidates.Length; i++)
            {
                var stat = candidates[i];
                var baseValue = piece.BaseStats.TryGetValue(stat, out var b) ? b : 0;
                foreach (var meld in piece.Melds)
                {
                    if (meld.IsFixedOvermeld && meld.StatId == stat)
                        baseValue += meld.Value;
                }

                // Fail-open when the cap sheet was unavailable: assume plenty of headroom.
                Headroom[i] = piece.Caps.TryGetValue(stat, out var cap)
                    ? Math.Max(0, cap - baseValue)
                    : int.MaxValue / 4;
            }
        }
    }

    /// <summary>All ways to split <paramref name="total"/> sockets into <paramref name="parts"/> stats.</summary>
    internal static IEnumerable<int[]> Compositions(int total, int parts)
    {
        var current = new int[parts];
        return Recurse(0, total);

        IEnumerable<int[]> Recurse(int index, int remaining)
        {
            if (index == parts - 1)
            {
                current[index] = remaining;
                yield return current;
                yield break;
            }

            for (var take = 0; take <= remaining; take++)
            {
                current[index] = take;
                foreach (var result in Recurse(index + 1, remaining - take))
                    yield return result;
            }
        }
    }

    /// <summary>
    /// Places the demanded socket counts onto pieces, maximizing effective (cap-clipped) stat
    /// gain. Returns effective contribution per candidate; optionally materializes the concrete
    /// per-piece socket assignment.
    /// </summary>
    private static int[] PlaceGreedy(
        PieceState[] pieces, uint[] candidates, int[] counts, bool materialize,
        out Dictionary<GearSlotId, uint[]> assignments)
    {
        var socketsFree = pieces.Select(p => p.Sockets).ToArray();
        var headroom = pieces.Select(p => (int[])p.Headroom.Clone()).ToArray();
        var demand = (int[])counts.Clone();
        var effective = new int[candidates.Length];
        var placed = materialize
            ? pieces.Select(p => new List<uint>(p.Sockets)).ToArray()
            : null;

        // Best possible marginal anywhere = the largest per-piece meld value (usually 54).
        var maxMeldValue = pieces.Length > 0 ? pieces.Max(p => p.MeldValue) : MateriaXiiValue;

        var remaining = demand.Sum();
        while (remaining > 0)
        {
            // Best (stat, piece) by marginal value: full piece-grade value > partial remainder > 0.
            // Per-piece values differ now (ilvl-gated materia grades), so high-grade pieces soak
            // demand before low-ilvl pieces automatically.
            var bestStat = -1; var bestPiece = -1; var bestValue = -1;
            for (var s = 0; s < candidates.Length; s++)
            {
                if (demand[s] == 0)
                    continue;
                for (var p = 0; p < pieces.Length; p++)
                {
                    if (socketsFree[p] == 0)
                        continue;
                    var value = Math.Min(pieces[p].MeldValue, Math.Max(0, headroom[p][s]));
                    if (value > bestValue)
                    {
                        bestValue = value; bestStat = s; bestPiece = p;
                        if (value == maxMeldValue)
                            goto place; // can't do better than a full top-grade meld
                    }
                }
            }

        place:
            socketsFree[bestPiece]--;
            headroom[bestPiece][bestStat] -= pieces[bestPiece].MeldValue;
            demand[bestStat]--;
            effective[bestStat] += bestValue;
            placed?[bestPiece].Add(candidates[bestStat]);
            remaining--;
        }

        assignments = new Dictionary<GearSlotId, uint[]>();
        if (materialize && placed != null)
        {
            for (var p = 0; p < pieces.Length; p++)
                assignments[pieces[p].Piece.Slot] = placed[p].ToArray();
        }

        return effective;
    }

    private static Dictionary<uint, int> MergeTotals(
        IReadOnlyDictionary<uint, int> baseTotals, uint[] candidates, int[] effective)
    {
        var totals = new Dictionary<uint, int>(baseTotals);
        for (var i = 0; i < candidates.Length; i++)
        {
            totals.TryGetValue(candidates[i], out var value);
            totals[candidates[i]] = value + effective[i];
        }

        return totals;
    }

    /// <summary>Totals from base stats plus the melds matching <paramref name="meldFilter"/> (cap-clipped per piece).</summary>
    private static Dictionary<uint, int> TotalsWithMelds(GearSnapshot snapshot, Func<MateriaMeld, bool> meldFilter)
    {
        var filtered = snapshot.Pieces
            .Select(p => p with { Melds = p.Melds.Where(meldFilter).ToList() })
            .ToList();
        var aggregate = GearStatAggregator.Aggregate(snapshot with { Pieces = filtered });
        return new Dictionary<uint, int>(aggregate.Totals);
    }

    private static MeldPlan BuildPlan(
        GearSnapshot snapshot,
        Dictionary<GearSlotId, uint[]> assignments,
        Dictionary<uint, int> totals,
        double delta)
    {
        // Diff plan vs current XII melds per piece: pair off matching stats, report the rest.
        var changes = new List<SocketChange>();
        foreach (var piece in snapshot.Pieces)
        {
            if (!assignments.TryGetValue(piece.Slot, out var planned))
                continue;

            var current = piece.Melds.Where(m => !m.IsFixedOvermeld).Select(m => m.StatId).ToList();
            var plannedList = planned.ToList();

            // Remove exact matches (those sockets stay untouched).
            foreach (var stat in current.ToList())
            {
                if (plannedList.Remove(stat))
                    current.Remove(stat);
            }

            // Whatever remains pairs up as replacements (or fresh melds into empty sockets).
            for (var i = 0; i < plannedList.Count; i++)
            {
                var from = i < current.Count ? current[i] : 0u;
                changes.Add(new SocketChange(piece.Slot, i, from, plannedList[i]));
            }
        }

        var summary = changes.Count == 0
            ? "current melds are already optimal"
            : string.Join(" · ", changes
                .GroupBy(c => (c.Slot, c.FromStat, c.ToStat))
                .Select(g =>
                {
                    var from = g.Key.FromStat == 0 ? "empty" : GearStatIds.Name(g.Key.FromStat);
                    var count = g.Count() > 1 ? $" ×{g.Count()}" : "";
                    return $"{g.Key.Slot}: {from}→{GearStatIds.Name(g.Key.ToStat)}{count}";
                }));

        return new MeldPlan(assignments, totals, delta, changes, summary);
    }
}
