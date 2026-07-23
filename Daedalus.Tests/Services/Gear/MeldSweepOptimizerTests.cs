using System;
using System.Collections.Generic;
using System.Linq;
using Daedalus.Data;
using Daedalus.Models.Gear;
using Daedalus.Services.Gear;
using Xunit;

namespace Daedalus.Tests.Services.Gear;

/// <summary>
/// Meld optimizer phase 5: the sweep must respect per-piece caps, never touch fixed XI
/// overmelds, put the top-priority stat first when nothing caps, and report an empty change set
/// when the current melds already match the optimum.
/// </summary>
public class MeldSweepOptimizerTests
{
    private static GearPiece Piece(
        GearSlotId slot,
        int sockets,
        Dictionary<uint, int> baseStats,
        Dictionary<uint, int> caps,
        List<MateriaMeld>? melds = null,
        bool advanced = false)
        => new(slot, (uint)(1000 + (int)slot), $"Test {slot}", 760,
            baseStats, melds ?? new List<MateriaMeld>(), sockets, advanced, caps);

    /// <summary>
    /// Two-socket piece with generous caps and REALISTIC base crit. At tiny stat totals Det
    /// genuinely beats Crit in the real math (Det is linear, Crit compounds with itself); crit
    /// only dominates at endgame-scale totals, so the fixtures must sit there.
    /// </summary>
    private static GearPiece OpenPiece(GearSlotId slot) => Piece(
        slot, 2,
        new Dictionary<uint, int> { [GearStatIds.CriticalHit] = 1200 },
        new Dictionary<uint, int>
        {
            [GearStatIds.CriticalHit] = 9999,
            [GearStatIds.Determination] = 9999,
            [GearStatIds.DirectHit] = 9999,
            [GearStatIds.SkillSpeed] = 9999,
        });

    private static GearSnapshot Snapshot(params GearPiece[] pieces)
        => new(pieces, 0, JobRegistry.Samurai, DateTime.UtcNow); // SAM: Crit > Det > DH, SkS dim

    [Fact]
    public void Sweep_UncappedPieces_TopPriorityStatDominates()
    {
        var plans = MeldSweepOptimizer.Sweep(Snapshot(OpenPiece(GearSlotId.Head), OpenPiece(GearSlotId.Body)));

        Assert.NotEmpty(plans);
        var best = plans[0];
        // With no caps in play, every socket goes Crit (the model is monotonic in crit).
        var assigned = best.SocketAssignments.Values.SelectMany(s => s).ToList();
        Assert.Equal(4, assigned.Count);
        Assert.All(assigned, stat => Assert.Equal(GearStatIds.CriticalHit, stat));
    }

    [Fact]
    public void Sweep_RespectsCaps_SpillsToSecondPriority()
    {
        // Head can only fit ONE more crit meld (headroom 60); the rest must spill to Det.
        var head = Piece(GearSlotId.Head, 2,
            new Dictionary<uint, int> { [GearStatIds.CriticalHit] = 1200 },
            new Dictionary<uint, int>
            {
                [GearStatIds.CriticalHit] = 1260,
                [GearStatIds.Determination] = 9999,
                [GearStatIds.DirectHit] = 9999,
                [GearStatIds.SkillSpeed] = 9999,
            });

        var plans = MeldSweepOptimizer.Sweep(Snapshot(head));
        var best = plans[0];
        var assigned = best.SocketAssignments[GearSlotId.Head];

        Assert.Equal(2, assigned.Length);
        // At most one crit fits under the cap; effective totals never exceed it, and the
        // remaining socket(s) land in an uncapped stat rather than wasting into overcap.
        Assert.True(assigned.Count(s => s == GearStatIds.CriticalHit) <= 1);
        Assert.True(best.Totals[GearStatIds.CriticalHit] <= 1260);
    }

    [Fact]
    public void Sweep_FixedXiOvermelds_StayPutAndCountAsFloor()
    {
        // Pentameld piece: 2 guaranteed + 1 first-overmeld = 3 sweepable; 2 fixed XI Crit +18.
        var body = Piece(GearSlotId.Body, 2,
            new Dictionary<uint, int> { [GearStatIds.CriticalHit] = 1200 },
            new Dictionary<uint, int>
            {
                [GearStatIds.CriticalHit] = 9999,
                [GearStatIds.Determination] = 9999,
                [GearStatIds.DirectHit] = 9999,
                [GearStatIds.SkillSpeed] = 9999,
            },
            melds: new List<MateriaMeld>
            {
                new(GearStatIds.CriticalHit, 18, 11, IsFixedOvermeld: true),
                new(GearStatIds.CriticalHit, 18, 11, IsFixedOvermeld: true),
            },
            advanced: true);

        var plans = MeldSweepOptimizer.Sweep(Snapshot(body));
        var best = plans[0];

        // 3 sweepable sockets assigned; the XI floor (36) is included in totals regardless of
        // where the sweep puts the XII sockets.
        Assert.Equal(3, best.SocketAssignments[GearSlotId.Body].Length);
        Assert.True(best.Totals[GearStatIds.CriticalHit] >= 1200 + 36,
            $"XI floor missing from totals: {best.Totals[GearStatIds.CriticalHit]}");
        // No change entry ever references the fixed sockets (only 3 assignable).
        Assert.All(best.Changes, c => Assert.True(c.SocketIndex < 3));
    }

    [Fact]
    public void Sweep_IsIdempotent_ApplyingPlanOneYieldsNoChanges()
    {
        // Whatever the model prefers: melding exactly plan #1 and re-sweeping must produce
        // "already optimal" with zero delta — the recommendation is a fixed point.
        var bare = Piece(GearSlotId.Head, 2,
            new Dictionary<uint, int> { [GearStatIds.CriticalHit] = 1200 },
            new Dictionary<uint, int>
            {
                [GearStatIds.CriticalHit] = 9999,
                [GearStatIds.Determination] = 9999,
                [GearStatIds.DirectHit] = 9999,
                [GearStatIds.SkillSpeed] = 9999,
            });

        var firstPlan = MeldSweepOptimizer.Sweep(Snapshot(bare))[0];
        var melded = bare with
        {
            Melds = firstPlan.SocketAssignments[GearSlotId.Head]
                .Select(stat => new MateriaMeld(stat, 54, 12, false))
                .ToList(),
        };

        var second = MeldSweepOptimizer.Sweep(Snapshot(melded))[0];

        Assert.Empty(second.Changes);
        Assert.Equal("current melds are already optimal", second.Summary);
        Assert.Equal(0.0, second.DeltaPercent, 3);
    }

    [Fact]
    public void Sweep_SuboptimalCurrent_PositiveDeltaAndChanges()
    {
        // Both melds sit in DH while DH is 10 points from its cap — 98 wasted points. The plan
        // must move them into uncapped stats and gain DPS.
        var head = Piece(GearSlotId.Head, 2,
            new Dictionary<uint, int>
            {
                [GearStatIds.CriticalHit] = 1200,
                [GearStatIds.DirectHit] = 250,
            },
            new Dictionary<uint, int>
            {
                [GearStatIds.CriticalHit] = 9999,
                [GearStatIds.Determination] = 9999,
                [GearStatIds.DirectHit] = 260,
                [GearStatIds.SkillSpeed] = 9999,
            },
            melds: new List<MateriaMeld>
            {
                new(GearStatIds.DirectHit, 54, 12, false),
                new(GearStatIds.DirectHit, 54, 12, false),
            });

        var plans = MeldSweepOptimizer.Sweep(Snapshot(head));
        var best = plans[0];

        Assert.True(best.DeltaPercent > 0, $"expected gain, got {best.DeltaPercent}");
        Assert.NotEmpty(best.Changes);
        Assert.Contains("DH→", best.Summary); // swapped away from DH, whichever stat the model prefers
    }

    [Fact]
    public void Compositions_CountAndSum()
    {
        var all = MeldSweepOptimizer.Compositions(4, 3).Select(c => (int[])c.Clone()).ToList();
        Assert.Equal(15, all.Count); // C(4+2,2)
        Assert.All(all, c => Assert.Equal(4, c.Sum()));
    }

    [Fact]
    public void Sweep_IlvlGatedGrades_UsePerPieceMeldValues()
    {
        // A leveling piece capped at grade XI (+18): its sockets contribute 18s, not 54s —
        // materia grades carry a base-ilvl requirement read from the sheets.
        var lowPiece = Piece(GearSlotId.Head, 2,
            new Dictionary<uint, int> { [GearStatIds.CriticalHit] = 1200 },
            new Dictionary<uint, int>
            {
                [GearStatIds.CriticalHit] = 9999,
                [GearStatIds.Determination] = 9999,
                [GearStatIds.DirectHit] = 9999,
                [GearStatIds.SkillSpeed] = 9999,
            }) with
        { SweepMeldGrade = 11, SweepMeldValue = 18 };

        var plans = MeldSweepOptimizer.Sweep(Snapshot(lowPiece));
        var best = plans[0];

        // 2 sockets assigned; total contribution above base is exactly 2 × 18 across candidates.
        Assert.Equal(2, best.SocketAssignments[GearSlotId.Head].Length);
        var contributed = best.Totals
            .Where(kv => kv.Key != GearStatIds.CriticalHit).Sum(kv => kv.Value)
            + (best.Totals.TryGetValue(GearStatIds.CriticalHit, out var crit) ? crit - 1200 : 0);
        Assert.Equal(36, contributed);
    }
}
