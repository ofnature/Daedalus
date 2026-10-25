using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Daedalus.Data;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>Placing a board's spaces from its layout, and where to walk next.</summary>
public class CrucibleWalkTests
{
    // Board 1's XBMContentStageEvent rows and its LVD_ROUTE_02 pop points (f1x1/level/planmap.lgb), in file order.
    private static readonly CrucibleStageRow[] Board1Rows =
    [
        new(0, 1, -1), new(1, 2, 1), new(2, 2, 2), new(2, 2, 3), new(3, 6, 0), new(3, 7, 0), new(4, 2, 4),
        new(5, 7, 1), new(6, 3, 5), new(7, 7, 2), new(7, 6, 1), new(8, 5, 0), new(9, 4, 0),
    ];

    private static readonly Vector3[] Board1Points =
    [
        P(-700, -75), P(-700, -66), P(-695, -58.5), P(-705, -58.5), P(-700, -51), P(-700, -42), P(-700, -33),
        P(-695, -25.5), P(-705, -25.5), P(-695, -16.5), P(-705, -16.5), P(-700, -9),
    ];

    // Board 2 (f1x2), file order — shuffled, which the placement must not care about.
    private static readonly CrucibleStageRow[] Board2Rows =
    [
        new(0, 1, -1), new(1, 2, 1), new(2, 5, 0), new(3, 3, 2), new(3, 2, 3), new(4, 7, 0), new(4, 6, 0),
        new(5, 2, 4), new(6, 7, 1), new(7, 3, 5), new(8, 8, 0), new(8, 7, 2), new(9, 5, 1), new(10, 4, 0),
    ];

    private static readonly Vector3[] Board2Points =
    [
        P(-700, -9), P(-695, -25.5), P(-700, -42), P(-695, -34.5), P(-705, -34.5), P(-700, -51), P(-705, -25.5),
        P(-695, -67.5), P(-700, -18), P(-700, -84), P(-700, -60), P(-705, -67.5), P(-700, -75),
    ];

    private static Vector3 P(double x, double z) => new((float)x, 0, (float)z);

    private static IReadOnlyList<CrucibleStage> Board1() =>
        CrucibleBoardLayout.Place(CrucibleRouteBuilder.Build(Board1Rows, [6, 1, 2, 3, 4, 5], []), Board1Points);

    private static IReadOnlyList<CrucibleStage> Board2() =>
        CrucibleBoardLayout.Place(CrucibleRouteBuilder.Build(Board2Rows, [13, 7, 8, 9, 10, 11, 12], [new List<(byte, int)> { (2, 6), (6, 1) }]), Board2Points);

    /// <summary>Move 1 is the southern end; the boss the northern; a fork's left space is the western one.</summary>
    [Fact]
    public void Board1IsPlacedSouthToNorthLeftToRight()
    {
        var route = Board1();
        Assert.All(route, s => Assert.NotNull(s.At));
        Assert.Equal(P(-700, -9), route.Single(s => s.Move == 1).At);
        Assert.Equal(P(-700, -75), route.Single(s => s.Type == CrucibleStageType.Boss).At);
        Assert.Equal(P(-705, -16.5), route.Single(s => s.Move == 2 && s.Side == 1).At);
        Assert.Equal(P(-695, -16.5), route.Single(s => s.Move == 2 && s.Side == 2).At);
    }

    /// <summary>Board 2's points come out of the file in no order; the forks still land on moves 3, 4 and 8.</summary>
    [Fact]
    public void Board2IsPlacedFromShuffledPoints()
    {
        var route = Board2();
        Assert.All(route, s => Assert.NotNull(s.At));
        Assert.Equal(P(-700, -18), route.Single(s => s.Type == CrucibleStageType.Shop && s.Move == 2).At);
        Assert.Equal(-67.5f, route.Single(s => s.Move == 8 && s.Side == 1).At!.Value.Z);
        Assert.Equal(P(-700, -84), route.Single(s => s.Type == CrucibleStageType.Boss).At);
    }

    /// <summary>A layout that does not match the route row for row leaves the spaces unplaced — no walking.</summary>
    [Fact]
    public void AMismatchedLayoutPlacesNothing()
    {
        var route = CrucibleBoardLayout.Place(CrucibleRouteBuilder.Build(Board1Rows, [6, 1, 2, 3, 4, 5], []), Board1Points.Skip(1).Append(P(-690, -9)).ToArray());
        Assert.All(route, s => Assert.Null(s.At));
    }

    [Theory]
    [InlineData(0f, 0)]       // the start, south of move 1
    [InlineData(-9f, 1)]      // standing on move 1
    [InlineData(-12.5f, 1)]   // between 1 and 2
    [InlineData(-16.2f, 2)]   // on the fork at move 2
    [InlineData(-75.3f, 9)]   // on the boss
    public void TheCurrentMoveComesFromWhereYouStand(float z, int move)
        => Assert.Equal(move, CrucibleWalkPlan.CurrentMove(Board1(), P(-700, z)));

    [Fact]
    public void FromTheStartTheNextSpaceIsMove1()
        => Assert.Equal(1, CrucibleWalkPlan.Next(Board1(), P(-700, 0), _ => 1)!.Move);

    /// <summary>At a fork the chosen side's space, by its side number.</summary>
    [Fact]
    public void AtAForkTheChosenSideIsWalkedTo()
    {
        var right = CrucibleWalkPlan.Next(Board1(), P(-700, -9), _ => 2)!;
        Assert.Equal((2, 2), (right.Move, right.Side));
        Assert.Equal(P(-695, -16.5), right.At);
    }

    [Fact]
    public void OnTheBossThereIsNothingLeftToWalkTo()
        => Assert.Null(CrucibleWalkPlan.Next(Board1(), P(-700, -75), _ => 1));

    /// <summary>The battle arenas sit hundreds of yalms from the strip: never walk from there.</summary>
    [Fact]
    public void TheArenaIsNotTheStrip()
    {
        Assert.True(CrucibleWalkPlan.OnStrip(Board1(), P(-700, 0)));
        Assert.False(CrucibleWalkPlan.OnStrip(Board1(), P(120, -404)));
    }

    [Fact]
    public void AForkSpacesSiblingIsTheOtherSide()
    {
        var route = Board1();
        var left = route.Single(s => s.Move == 2 && s.Side == 1);
        Assert.Equal(2, CrucibleWalkPlan.Sibling(route, left)!.Side);
        Assert.Null(CrucibleWalkPlan.Sibling(route, route.Single(s => s.Move == 1)));
    }
}
