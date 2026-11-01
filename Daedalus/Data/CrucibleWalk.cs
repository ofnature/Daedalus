using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Daedalus.Data;

/// <summary>
/// Puts each space of a board on the ground. Every board's layout (its territory's <c>planmap.lgb</c>, layer
/// <c>LVD_ROUTE_*</c>) holds one player pop point per space, and all five boards are laid out the same way: a strip
/// at x ≈ −700 walked north, move 1 at z = −9, each move one row further north, a fork's spaces side by side in its
/// row. That holds for boards 1-4; the Second Master's Board (25 points) is a grid whose moves step sideways, so the
/// board's own map (<see cref="CrucibleMapRow"/>) places the spaces when there is one, and the strip is the fallback.
/// Pure.
/// </summary>
public static class CrucibleBoardLayout
{
    /// <summary>
    /// The route with each space's position, from the board's map: its grid columns are the points' distinct x values
    /// west to east, its grid rows their distinct z values south to north. Falls back to <see cref="Place(IReadOnlyList{CrucibleStage}, IReadOnlyList{Vector3})"/>
    /// when there is no map or it does not land every space on a point.
    /// </summary>
    public static IReadOnlyList<CrucibleStage> Place(
        IReadOnlyList<CrucibleStage> route, IReadOnlyList<Vector3> points, IReadOnlyList<CrucibleMapRow>? map)
    {
        var cells = map?.Where(m => m.Kind == 1).GroupBy(m => m.Node).ToDictionary(g => g.Key, g => (g.First().X, g.First().Y));
        if (cells == null || route.Count == 0 || points.Count != route.Count || route.Any(s => !cells.ContainsKey(s.Node)))
            return Place(route, points);

        static List<float> Distinct(IEnumerable<float> values)
        {
            var list = new List<float>();
            foreach (var v in values.OrderBy(v => v))
                if (list.Count == 0 || v - list[^1] >= RowTolerance)
                    list.Add(v);
            return list;
        }

        var columns = route.Select(s => cells[s.Node].X).Distinct().OrderBy(x => x).ToList();
        var rows = route.Select(s => cells[s.Node].Y).Distinct().OrderByDescending(y => y).ToList();
        var xs = Distinct(points.Select(p => p.X));
        var zs = Distinct(points.Select(p => p.Z));
        zs.Reverse(); // south (largest z) first, like the grid rows
        if (columns.Count != xs.Count || rows.Count != zs.Count)
            return Place(route, points);

        var placed = new List<CrucibleStage>(route.Count);
        foreach (var stage in route)
        {
            var (x, y) = cells[stage.Node];
            var want = new Vector2(xs[columns.IndexOf(x)], zs[rows.IndexOf(y)]);
            var point = points.OrderBy(p => Vector2.Distance(new Vector2(p.X, p.Z), want)).First();
            if (Vector2.Distance(new Vector2(point.X, point.Z), want) >= RowTolerance)
                return Place(route, points);
            placed.Add(stage with { At = point });
        }

        return placed.Select(s => s.At).Distinct().Count() == placed.Count ? placed : Place(route, points);
    }

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
    /// <summary>Whether every space knows where it leads (the board's map was read).</summary>
    public static bool HasMap(IReadOnlyList<CrucibleStage> route) => route.Count > 0 && route.All(s => s.Next != null);

    /// <summary>Within this of a space's spot (across the ground) you are standing on it.</summary>
    public const float OnSpaceYalms = 1.5f;

    /// <summary>
    /// The space you are on, on a mapped board: the one within <see cref="OnSpaceYalms"/>; between spaces, the last one
    /// reached (<paramref name="lastReached"/>); with none reached yet (a reload mid-board), the nearest one — unless you
    /// are south of every space, at the start (null).
    /// </summary>
    public static CrucibleStage? CurrentSpace(IReadOnlyList<CrucibleStage> route, Vector3 player, CrucibleStage? lastReached)
    {
        var placed = route.Where(s => s.At != null).ToList();
        if (placed.Count == 0)
            return null;
        float Across(CrucibleStage s) => Vector2.Distance(new Vector2(s.At!.Value.X, s.At.Value.Z), new Vector2(player.X, player.Z));
        var nearest = placed.OrderBy(Across).First();
        if (Across(nearest) <= OnSpaceYalms)
            return nearest;
        if (lastReached != null)
            return lastReached;
        return player.Z > placed.Max(s => s.At!.Value.Z) + ReachedYalms ? null : nearest;
    }

    /// <summary>
    /// The space to walk to next on a mapped board: the only one <paramref name="from"/> leads to, or the side
    /// <paramref name="chooseSide"/> picks among them (1-based place on their move). From the start (null), the first move.
    /// Null when the board is done.
    /// </summary>
    public static CrucibleStage? NextOnMap(
        IReadOnlyList<CrucibleStage> route, CrucibleStage? from, Func<IReadOnlyList<CrucibleStage>, int> chooseSide)
    {
        var options = from == null
            ? route.Where(s => s.Move == route.Min(r => r.Move)).ToList()
            : route.Where(s => from.Next?.Contains(s.Node) == true).ToList();
        options = options.Where(s => s.At != null).ToList();
        if (options.Count <= 1)
            return options.FirstOrDefault();

        var side = chooseSide(options);
        return options.FirstOrDefault(s => s.Side == side) ?? options[0];
    }

    /// <summary>Every space that can still be walked from <paramref name="from"/> on, itself included (empty without a map).</summary>
    public static IReadOnlyList<CrucibleStage> Reachable(IReadOnlyList<CrucibleStage> route, CrucibleStage from)
    {
        if (from.Next == null)
            return [];
        var byNode = route.ToDictionary(s => s.Node);
        var seen = new HashSet<int> { from.Node };
        var queue = new Queue<CrucibleStage>([from]);
        var reached = new List<CrucibleStage>();
        while (queue.Count > 0)
        {
            var stage = queue.Dequeue();
            reached.Add(stage);
            foreach (var next in stage.Next ?? [])
                if (seen.Add(next) && byNode.TryGetValue(next, out var to))
                    queue.Enqueue(to);
        }

        return reached;
    }

    /// <summary>A space counts as reached once you are this close to its row (or past it).</summary>
    public const float ReachedYalms = 1f;

    /// <summary>
    /// The last move you have reached: the furthest row at or behind you (the walk runs north, towards −z). 0 at the
    /// start. Works from position alone, so it picks up after a battle, a reload or a hand-walked stretch.
    /// <para>On a mapped board, the move of <see cref="CurrentSpace"/>: there a row is not a move.</para>
    /// </summary>
    public static int CurrentMove(IReadOnlyList<CrucibleStage> route, Vector3 player) =>
        HasMap(route)
            ? CurrentSpace(route, player, null)?.Move ?? 0
            : route.Where(s => s.At is { } at && at.Z >= player.Z - ReachedYalms).Select(s => s.Move).DefaultIfEmpty(0).Max();

    /// <summary>
    /// The space to walk to next: the only one on the next move, or at a fork the side <paramref name="chooseSide"/>
    /// picks (1-based; anything else means the left). Null when the board is done or its spaces are not placed.
    /// Inside a branch there is no choice: taking one side of a fork closes the other on every board, so the next fork
    /// keeps to the lane you are on.
    /// </summary>
    /// <param name="spotOf">Where a space is actually walked to (a fork learned flipped walks to its sibling's spot);
    /// defaults to its placed position.</param>
    public static CrucibleStage? Next(IReadOnlyList<CrucibleStage> route, Vector3 player, Func<IReadOnlyList<CrucibleStage>, int> chooseSide,
        Func<CrucibleStage, Vector3>? spotOf = null)
    {
        spotOf ??= s => s.At!.Value;
        var next = CurrentMove(route, player) + 1;
        var options = route.Where(s => s.Move == next && s.At != null).ToList();
        if (options.Count == 0)
            return null;
        if (options.Count == 1)
            return options[0];

        // Inside a branch the lane is already chosen: a fork's lanes do not cross until they rejoin (board 2, 2026-10-30:
        // 3R taken, then 4L asked for — the walker stood still, and the left lane showed as closed).
        if (LaneSpace(route, player, next - 1) is { } here
            && options.OrderBy(s => MathF.Abs(spotOf(s).X - here.X)).First() is var sameLane
            && MathF.Abs(spotOf(sameLane).X - here.X) < LaneChangeYalms)
            return sameLane;

        var side = chooseSide(options);
        return options.FirstOrDefault(s => s.Side == side) ?? options[0];
    }

    /// <summary>
    /// Where the space you are on stands, when <paramref name="move"/> is a fork (so you are on one of its lanes): the
    /// one of its spaces nearest you across. Null for a move with a single space, or the start.
    /// </summary>
    private static Vector3? LaneSpace(IReadOnlyList<CrucibleStage> route, Vector3 player, int move)
    {
        var spaces = route.Where(s => s.Move == move && s.At != null).ToList();
        return spaces.Count < 2 ? null : spaces.OrderBy(s => MathF.Abs(s.At!.Value.X - player.X)).First().At;
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

    /// <summary>Whether a fork was learned in play to sit the other way round from the game data's left/right.</summary>
    public static bool IsForkFlipped(string key, ICollection<string>? learned) => learned?.Contains(key) == true;

    /// <summary>Lanes are this far apart (x −705 / −695 on every board); a target this far across is on the other lane.</summary>
    public const float LaneChangeYalms = 3f;

    /// <summary>
    /// Where to walk on the way to <paramref name="to"/>: when it is on the other lane, first across to that lane halfway
    /// down. Only from a single (middle) space into a fork — a fork's lanes never cross (Board 1 move 3, 2026-10-27: from
    /// 2L the walker aimed for the treasure at 3R and entered the campsite at 3L, because the right lane was closed;
    /// <see cref="Next"/> now keeps to the lane inside a branch).
    /// </summary>
    public static Vector3 LaneWaypoint(Vector3 from, Vector3 to)
        => MathF.Abs(to.X - from.X) >= LaneChangeYalms
            ? new Vector3(to.X, to.Y, from.Z + ((to.Z - from.Z) / 2f))
            : to;

    /// <summary>
    /// A wrong space only says the fork is the other way round when it opened where we were aiming — nearer the spot we
    /// walked to than its sibling's. Opening on the way there is crossing the sibling's space, not a flipped fork.
    /// </summary>
    public static bool OpenedAtTarget(Vector3 player, Vector3 targetSpot, Vector3 siblingSpot)
        => Vector2.Distance(new Vector2(player.X, player.Z), new Vector2(targetSpot.X, targetSpot.Z))
           < Vector2.Distance(new Vector2(player.X, player.Z), new Vector2(siblingSpot.X, siblingSpot.Z));

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
