using System;
using System.Collections.Generic;
using Daedalus.Models.Gear;
using Daedalus.Services.Gear;
using Xunit;

namespace Daedalus.Tests.Services.Gear;

/// <summary>
/// Meld optimizer phase 1 (2026-07-22): the pure math under the gear pipeline. The Lumina/
/// ClientStructs plumbing stays deliberately thin and is field-validated via /daedalus dumpgear;
/// everything with logic in it is covered here.
/// </summary>
public class StatCapMathTests
{
    [Fact]
    public void Cap_ScalesByPerMilleSlotPercent()
    {
        // Synthetic: ilvl base 500 at a 70% slot → 350.
        Assert.Equal(350, StatCapMath.Cap(500, 700));
    }

    [Fact]
    public void Cap_RoundsHalfUp()
    {
        // 405 × 350 / 1000 = 141.75 → 142; 401 × 350 / 1000 = 140.35 → 140.
        Assert.Equal(142, StatCapMath.Cap(405, 350));
        Assert.Equal(140, StatCapMath.Cap(401, 350));
        // Exact half rounds UP (community-verified in-game behavior, not banker's).
        Assert.Equal(3, StatCapMath.Cap(5, 500));
    }

    [Fact]
    public void Cap_ZeroInputs_ZeroCap()
    {
        Assert.Equal(0, StatCapMath.Cap(0, 700));
        Assert.Equal(0, StatCapMath.Cap(500, 0));
    }
}

public class GearPieceModelTests
{
    private static GearPiece Piece(
        int guaranteed = 2, bool advanced = false,
        Dictionary<uint, int>? baseStats = null,
        List<MateriaMeld>? melds = null,
        Dictionary<uint, int>? caps = null)
        => new(
            GearSlotId.Body, 12345, "Test Togi", 760,
            baseStats ?? new Dictionary<uint, int>(),
            melds ?? new List<MateriaMeld>(),
            guaranteed, advanced,
            caps ?? new Dictionary<uint, int>());

    [Fact]
    public void SweepableSockets_PentameldModel_GuaranteedPlusFirstOvermeld()
    {
        // Current BiS: pentameld = 3× XII + 2× XI → sweepable is guaranteed + 1.
        Assert.Equal(2, Piece(guaranteed: 2, advanced: false).SweepableSockets);
        Assert.Equal(3, Piece(guaranteed: 2, advanced: true).SweepableSockets);
    }

    [Fact]
    public void OvercapWaste_BasePlusMeldsAboveCap()
    {
        var piece = Piece(
            baseStats: new Dictionary<uint, int> { [GearStatIds.DirectHit] = 283 },
            melds: new List<MateriaMeld>
            {
                new(GearStatIds.DirectHit, 54, 12, false),
                new(GearStatIds.DirectHit, 54, 12, false),
            },
            caps: new Dictionary<uint, int> { [GearStatIds.DirectHit] = 379 });

        // 283 + 108 = 391 vs cap 379 → 12 wasted.
        Assert.Equal(12, piece.OvercapWaste(GearStatIds.DirectHit));
        Assert.Equal(0, piece.OvercapWaste(GearStatIds.CriticalHit));
    }
}

public class GearStatAggregatorTests
{
    private static GearSnapshot Snapshot(params GearPiece[] pieces)
        => new(pieces, 0, 34, DateTime.UtcNow);

    private static GearPiece Piece(GearSlotId slot, Dictionary<uint, int> baseStats,
        List<MateriaMeld> melds, Dictionary<uint, int> caps)
        => new(slot, 1, "P", 760, baseStats, melds, 2, false, caps);

    [Fact]
    public void Aggregate_SumsBaseAndMeldsAcrossPieces()
    {
        var result = GearStatAggregator.Aggregate(Snapshot(
            Piece(GearSlotId.Head,
                new() { [GearStatIds.CriticalHit] = 254 },
                new() { new(GearStatIds.Determination, 54, 12, false) },
                new() { [GearStatIds.CriticalHit] = 254, [GearStatIds.Determination] = 254 }),
            Piece(GearSlotId.Feet,
                new() { [GearStatIds.CriticalHit] = 100 },
                new() { new(GearStatIds.CriticalHit, 54, 12, false) },
                new() { [GearStatIds.CriticalHit] = 254 })));

        Assert.Equal(254 + 100 + 54, result.Totals[GearStatIds.CriticalHit]);
        Assert.Equal(54, result.Totals[GearStatIds.Determination]);
        Assert.Empty(result.Overcaps);
    }

    [Fact]
    public void Aggregate_ClipsPieceContributionAtCap_AndReportsWaste()
    {
        var result = GearStatAggregator.Aggregate(Snapshot(
            Piece(GearSlotId.Body,
                new() { [GearStatIds.DirectHit] = 283 },
                new()
                {
                    new(GearStatIds.DirectHit, 54, 12, false),
                    new(GearStatIds.DirectHit, 54, 12, false),
                },
                new() { [GearStatIds.DirectHit] = 379 })));

        // The game clips the piece at its cap: total is 379, not 391 — and the waste is reported.
        Assert.Equal(379, result.Totals[GearStatIds.DirectHit]);
        var overcap = Assert.Single(result.Overcaps);
        Assert.Equal(GearSlotId.Body, overcap.Slot);
        Assert.Equal(GearStatIds.DirectHit, overcap.StatId);
        Assert.Equal(12, overcap.WastedPoints);
    }

    [Fact]
    public void Aggregate_FixedXiOvermelds_CountTowardTotals()
    {
        // XI overmelds (+18) are fixed but still real stats — they must be in the totals.
        var result = GearStatAggregator.Aggregate(Snapshot(
            Piece(GearSlotId.Legs,
                new() { [GearStatIds.CriticalHit] = 100 },
                new()
                {
                    new(GearStatIds.CriticalHit, 54, 12, false),
                    new(GearStatIds.CriticalHit, 18, 11, true),
                    new(GearStatIds.CriticalHit, 18, 11, true),
                },
                new() { [GearStatIds.CriticalHit] = 404 })));

        Assert.Equal(100 + 54 + 18 + 18, result.Totals[GearStatIds.CriticalHit]);
    }
}
