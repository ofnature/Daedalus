using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Daedalus.Data;
using Xunit;
using static Daedalus.Tests.Data.CrucibleBoardData;

namespace Daedalus.Tests.Data;

/// <summary>Walking a board along its own map (XBMContentStageEventMap), from the five boards' client data.</summary>
public class CrucibleBoardMapTests
{
    private static IReadOnlyList<CrucibleStage> Mapped(
        CrucibleStageRow[] rows, int[] battles, IReadOnlyList<IReadOnlyList<(byte, int)>> random, CrucibleMapRow[] map, Vector3[] points)
        => CrucibleBoardLayout.Place(CrucibleRouteBuilder.Build(rows, battles, random, map), points, map);

    private static IReadOnlyList<CrucibleStage> Strip(
        CrucibleStageRow[] rows, int[] battles, IReadOnlyList<IReadOnlyList<(byte, int)>> random, Vector3[] points)
        => CrucibleBoardLayout.Place(CrucibleRouteBuilder.Build(rows, battles, random), points);

    public static IEnumerable<object[]> Boards1To4() =>
    [
        [Rows1, Battles1, Random1, Map1, Points1],
        [Rows2, Battles2, Random2, Map2, Points2],
        [Rows3, Battles3, Random3, Map3, Points3],
        [Rows4, Battles4, Random4, Map4, Points4],
    ];

    private static IReadOnlyList<CrucibleStage> Board4() => Mapped(Rows4, Battles4, Random4, Map4, Points4);

    private static IReadOnlyList<CrucibleStage> Board5() => Mapped(Rows5, Battles5, Random5, Map5, Points5);

    private static Vector3 P(float x, float z) => new(x, 0, z);

    private static CrucibleStage Space(IReadOnlyList<CrucibleStage> route, int move, int side)
        => route.Single(s => s.Move == move && s.Side == side);

    /// <summary>The map puts every space of boards 1-4 exactly where the strip layout (field-walked) does.</summary>
    [Theory]
    [MemberData(nameof(Boards1To4))]
    public void TheMapAgreesWithTheStripOnBoards1To4(
        CrucibleStageRow[] rows, int[] battles, IReadOnlyList<IReadOnlyList<(byte, int)>> random, CrucibleMapRow[] map, Vector3[] points)
    {
        var mapped = Mapped(rows, battles, random, map, points);
        Assert.All(mapped, s => Assert.NotNull(s.At));
        Assert.Equal(Strip(rows, battles, random, points).Select(s => s.At), mapped.Select(s => s.At));
    }

    /// <summary>The strip layout cannot place the Second Master's Board (field: "0 placed"); the map places all 25.</summary>
    [Fact]
    public void TheSecondMastersBoardIsPlacedFromItsMap()
    {
        Assert.All(Strip(Rows5, Battles5, Random5, Points5), s => Assert.Null(s.At));

        var route = Board5();
        Assert.Equal(25, route.Count(s => s.At != null));
        Assert.Equal(P(-700, -9), Space(route, 1, 0).At);
        Assert.Equal(P(-709, -9), Space(route, 2, 1).At); // the Mindflayer, beside move 1
        Assert.Equal(P(-691, -9), Space(route, 2, 2).At); // the shop
        Assert.Equal(P(-718, -54), Space(route, 9, 1).At); // the Chimera, out on the west edge
        Assert.Equal(P(-700, -81), route.Single(s => s.Type == CrucibleStageType.Boss).At);
    }

    [Fact]
    public void ThreeSpaceMovesReadLeftMiddleRight()
        => Assert.Equal(["3L", "3M", "3R"], Board5().Where(s => s.Move == 3).Select(s => $"{s.Move}{s.SideLabel}"));

    /// <summary>Each lane reaches two of move 3's three spaces: from the Mindflayer, the campsite and the treasure.</summary>
    [Fact]
    public void FromTheMindflayerOnlyItsLaneIsOffered()
    {
        var route = Board5();
        var offered = new List<string>();
        CrucibleWalkPlan.NextOnMap(route, Space(route, 2, 1), fork =>
        {
            offered.AddRange(fork.Select(s => $"{s.Move}{s.SideLabel}"));
            return 1;
        });
        Assert.Equal(["3L", "3M"], offered);
    }

    /// <summary>The middle of move 12 (a campsite) goes straight to the final boss, skipping move 13.</summary>
    [Fact]
    public void TheMiddleLaneSkipsMove13()
    {
        var route = Board5();
        var next = CrucibleWalkPlan.NextOnMap(route, Space(route, 12, 2), _ => 1);
        Assert.Equal(CrucibleStageType.Boss, next!.Type);
    }

    [Fact]
    public void FromTheStartTheFirstMoveIsNext()
        => Assert.Equal(1, CrucibleWalkPlan.NextOnMap(Board5(), null, _ => 1)!.Move);

    [Fact]
    public void TheBossLeadsNowhere()
    {
        var route = Board5();
        Assert.Null(CrucibleWalkPlan.NextOnMap(route, route.Single(s => s.Type == CrucibleStageType.Boss), _ => 1));
    }

    /// <summary>Move 2 is beside move 1, not north of it: standing on the shop is move 2 (by rows it read as 1).</summary>
    [Fact]
    public void StandingOnASidewaysSpaceIsItsMove()
    {
        var route = Board5();
        Assert.Equal(Space(route, 2, 2), CrucibleWalkPlan.CurrentSpace(route, P(-691.5f, -9.4f), null));
        Assert.Equal(2, CrucibleWalkPlan.CurrentMove(route, P(-691, -9)));
    }

    [Fact]
    public void BetweenSpacesTheLastOneReachedHolds()
    {
        var route = Board5();
        var shop = Space(route, 2, 2);
        Assert.Equal(shop, CrucibleWalkPlan.CurrentSpace(route, P(-691, -13), shop));
    }

    [Fact]
    public void SouthOfTheBoardIsTheStart()
    {
        Assert.Null(CrucibleWalkPlan.CurrentSpace(Board5(), P(-700, 0), null));
        Assert.Equal(0, CrucibleWalkPlan.CurrentMove(Board5(), P(-700, 0)));
    }

    /// <summary>A reload mid-board (nothing reached yet, not at the start): the nearest space.</summary>
    [Fact]
    public void AfterAReloadTheNearestSpaceIsWhereYouAre()
        => Assert.Equal(5, CrucibleWalkPlan.CurrentSpace(Board5(), P(-700, -38), null)!.Move); // the Durga piece at z -36

    [Fact]
    public void WithoutAMapNothingIsReachable()
        => Assert.Empty(CrucibleWalkPlan.Reachable(Strip(Rows1, Battles1, Random1, Points1), Strip(Rows1, Battles1, Random1, Points1)[0]));

    // --- Legendary looks down the lane for its elites ---

    private static CrucibleEnemy Enemy(uint nameId, int battle) => new(nameId, $"#{nameId}", battle, false, CrucibleElement.None, [1, 1, 1, 1, 1], []);

    private static CrucibleIndex Index(CrucibleBoard board) => new(
        [board],
        [Enemy(14633, 33), Enemy(14663, 39), Enemy(14665, 40), Enemy(14688, 44), Enemy(14617, 28), Enemy(14618, 29)]);

    private static CrucibleBoard Board(int index, IReadOnlyList<CrucibleStage> route, int[] battles)
        => new(index, 0, 0, $"Board {index}", 50, battles.Where(b => b != 0).ToList(), null, route);

    private static IReadOnlyList<CrucibleStage> Fork(IReadOnlyList<CrucibleStage> route, CrucibleStage from)
        => route.Where(s => from.Next!.Contains(s.Node)).ToList();

    /// <summary>
    /// Move 7: a random space (left) against a shop (right). Legendary ranks the shop higher, but only the left lane
    /// reaches the Chimera at 9L; with the map it goes left.
    /// </summary>
    [Fact]
    public void LegendaryTakesTheLaneToTheChimera()
    {
        var route = Board5();
        var board = Board(5, route, Battles5);
        var fork = Fork(route, Space(route, 6, 0));
        Assert.Equal(2, CrucibleRoutePlan.SuggestedSide(Index(board), board, fork, _ => true, legendary: true));
        Assert.Equal(1, CrucibleRoutePlan.SuggestedSide(Index(board), board, fork, _ => true, legendary: true, route));
    }

    /// <summary>The look-ahead is Legendary's only: an easy clear keeps its own ranking.</summary>
    [Fact]
    public void ClearDoesNotLookDownTheLane()
    {
        var route = Board5();
        var board = Board(5, route, Battles5);
        var fork = Fork(route, Space(route, 6, 0));
        Assert.Equal(
            CrucibleRoutePlan.SuggestedSide(Index(board), board, fork, _ => true),
            CrucibleRoutePlan.SuggestedSide(Index(board), board, fork, _ => true, route: route));
    }

    /// <summary>The Mindflayer is on the fork itself (2L, over the shop).</summary>
    [Fact]
    public void LegendaryTakesTheMindflayer()
    {
        var route = Board5();
        var board = Board(5, route, Battles5);
        Assert.Equal(1, CrucibleRoutePlan.SuggestedSide(Index(board), board, Fork(route, Space(route, 1, 0)), _ => true, legendary: true, route));
    }

    /// <summary>First Master's Board: whichever way Legendary goes on the way, it ends up at the Golem (7L).</summary>
    [Fact]
    public void LegendaryWalksTheFirstMastersBoardToTheGolem()
    {
        var route = Board4();
        var board = Board(4, route, Battles4);
        var index = Index(board);
        CrucibleStage? at = null;
        var walked = new List<CrucibleStage>();
        while (CrucibleWalkPlan.NextOnMap(route, at, fork =>
                   CrucibleRoutePlan.SuggestedSide(index, board, fork, _ => true, legendary: true, route)) is { } next)
        {
            walked.Add(next);
            at = next;
        }

        Assert.Contains(walked, s => s.Battle == 28);
        Assert.Equal(CrucibleStageType.Boss, walked[^1].Type);
    }

    /// <summary>Second Master's Board end to end: Mindflayer, Chimera and King Ahriman all on the walk, and the boss last.</summary>
    [Fact]
    public void LegendaryWalksTheSecondMastersBoardPastAllThreeElites()
    {
        var route = Board5();
        var board = Board(5, route, Battles5);
        var index = Index(board);
        CrucibleStage? at = null;
        var walked = new List<CrucibleStage>();
        while (CrucibleWalkPlan.NextOnMap(route, at, fork =>
                   CrucibleRoutePlan.SuggestedSide(index, board, fork, _ => true, legendary: true, route)) is { } next)
        {
            walked.Add(next);
            at = next;
        }

        Assert.Contains(walked, s => s.Battle == 33);
        Assert.Contains(walked, s => s.Battle == 39);
        Assert.Contains(walked, s => s.Battle == 44);
        Assert.Equal(CrucibleStageType.Boss, walked[^1].Type);
    }
}
