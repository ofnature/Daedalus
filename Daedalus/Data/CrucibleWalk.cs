using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Daedalus.Data;

/// <summary>
/// Puts each space of a board on the ground. Every board's layout (its territory's <c>planmap.lgb</c>, layer
/// <c>LVD_ROUTE_*</c>) holds one player pop point per space, and all five boards are laid out the same way: a strip
/// at x ≈ −700 walked north, move 1 at z = −9, each move one row further north, a fork's spaces side by side in its
/// row. Checked against the route for all five boards (12/13/16/14/25 points, forks where the stage rows put them).
/// Pure.
/// </summary>
public static class CrucibleBoardLayout
{
    /// <summary>Points closer than this along the walk belong to one row (one move).</summary>
    private const float RowTolerance = 1f;

    /// <summary>
    /// The route with each space's position, or the route unchanged when the points do not match it row for row
    /// (a layout we have not seen: better no walking than walking to the wrong space). Rows run from the start
    /// (largest z) north; within a row the spaces go west to east — left to right walking north, the order a fork's
    /// stage rows list them.
    /// </summary>
    public static IReadOnlyList<CrucibleStage> Place(IReadOnlyList<CrucibleStage> route, IReadOnlyList<Vector3> points)
    {
        if (route.Count == 0 || points.Count != route.Count)
            return route;

        var rows = new List<List<Vector3>>();
        foreach (var point in points.OrderByDescending(p => p.Z))
        {
            if (rows.Count > 0 && Math.Abs(rows[^1][0].Z - point.Z) < RowTolerance)
                rows[^1].Add(point);
            else
                rows.Add([point]);
        }

        var moves = route.GroupBy(s => s.Move).OrderBy(g => g.Key).Select(g => g.ToList()).ToList();
        if (rows.Count != moves.Count || rows.Zip(moves).Any(pair => pair.First.Count != pair.Second.Count))
            return route;

        var placed = new List<CrucibleStage>(route.Count);
        for (var m = 0; m < moves.Count; m++)
        {
            var row = rows[m].OrderBy(p => p.X).ToList();
            for (var i = 0; i < moves[m].Count; i++)
                placed.Add(moves[m][i] with { At = row[i] });
        }

        return placed;
    }
}

/// <summary>Where to walk next on a board. Pure.</summary>
public static class CrucibleWalkPlan
{
    /// <summary>A space counts as reached once you are this close to its row (or past it).</summary>
    public const float ReachedYalms = 1f;

    /// <summary>
    /// The last move you have reached: the furthest row at or behind you (the walk runs north, towards −z). 0 at the
    /// start. Works from position alone, so it picks up after a battle, a reload or a hand-walked stretch.
    /// </summary>
    public static int CurrentMove(IReadOnlyList<CrucibleStage> route, Vector3 player) =>
        route.Where(s => s.At is { } at && at.Z >= player.Z - ReachedYalms).Select(s => s.Move).DefaultIfEmpty(0).Max();

    /// <summary>
    /// The space to walk to next: the only one on the next move, or at a fork the side <paramref name="chooseSide"/>
    /// picks (1-based; anything else means the left). Null when the board is done or its spaces are not placed.
    /// </summary>
    public static CrucibleStage? Next(IReadOnlyList<CrucibleStage> route, Vector3 player, Func<IReadOnlyList<CrucibleStage>, int> chooseSide)
    {
        var next = CurrentMove(route, player) + 1;
        var options = route.Where(s => s.Move == next && s.At != null).ToList();
        if (options.Count == 0)
            return null;
        if (options.Count == 1)
            return options[0];

        var side = chooseSide(options);
        return options.FirstOrDefault(s => s.Side == side) ?? options[0];
    }

    /// <summary>You are on the board's strip, not in a battle arena (those sit hundreds of yalms away).</summary>
    public static bool OnStrip(IReadOnlyList<CrucibleStage> route, Vector3 player)
    {
        var placed = route.Where(s => s.At != null).Select(s => s.At!.Value).ToList();
        if (placed.Count == 0)
            return false;

        return player.X >= placed.Min(p => p.X) - 12f && player.X <= placed.Max(p => p.X) + 12f
               && player.Z >= placed.Min(p => p.Z) - 12f && player.Z <= placed.Max(p => p.Z) + 15f;
    }

    /// <summary>The key of one fork in <c>CrucibleFlippedForks</c>: "board:move".</summary>
    public static string ForkKey(int board, int move) => $"{board}:{move}";

    /// <summary>
    /// Forks known to sit the other way round from the game data's left/right. Board 1 move 3: the walker aimed for the
    /// treasure at x −695 and the campsite opened (Korha, 2026-10-27, fork log); its battle forks matched.
    /// </summary>
    public static readonly IReadOnlySet<string> KnownFlippedForks = new HashSet<string> { "1:3" };

    /// <summary>Whether a fork is the other way round: known, toggled by what was learned in play.</summary>
    public static bool IsForkFlipped(string key, ICollection<string>? learned) =>
        KnownFlippedForks.Contains(key) != (learned?.Contains(key) == true);

    /// <summary>
    /// Which space type a screen that just opened belongs to: a campsite (the team screen without the battle list), a
    /// treasure coffer or a shop. Null for anything else (battles are told apart by the battle the horn screen shows).
    /// </summary>
    public static CrucibleStageType? OpenedSpace(bool treasure, bool shop, bool teamScreen, bool battleList) =>
        treasure ? CrucibleStageType.Treasure
        : shop ? CrucibleStageType.Shop
        : teamScreen && !battleList ? CrucibleStageType.Campsite
        : null;

    /// <summary>
    /// Whether a fork is the other way round: we walked to <paramref name="walkedTo"/>'s spot and a space of its
    /// sibling's type opened instead (and the two types differ).
    /// </summary>
    public static bool ForkIsFlipped(IReadOnlyList<CrucibleStage> route, CrucibleStage walkedTo, CrucibleStageType opened) =>
        walkedTo.Type != opened && Sibling(route, walkedTo) is { } other && other.Type == opened;

    /// <summary>
    /// At a two-way fork, the other side's space. Used when the battle that opened is the other side's: the layout's
    /// left/right was read the wrong way round for this board.
    /// </summary>
    public static CrucibleStage? Sibling(IReadOnlyList<CrucibleStage> route, CrucibleStage stage)
    {
        var fork = route.Where(s => s.Move == stage.Move).ToList();
        return fork.Count == 2 ? fork.First(s => s != stage) : null;
    }
}
