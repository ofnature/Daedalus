using System;
using System.Collections.Generic;
using System.Linq;

namespace Daedalus.Data;

/// <summary>What a space on a Crucible board is (XBMContentStageEvent column 1).</summary>
public enum CrucibleStageType : byte
{
    Start = 1,
    Enemy = 2,
    Elite = 3,
    Boss = 4,
    Shop = 5,
    Campsite = 6,
    Treasure = 7,
    Random = 8,
}

/// <summary>One thing a random space can turn out to be (XBMRandomStageEvent subrow).</summary>
/// <param name="Battle">The XBMBattleDetail row, for an enemy; 0 otherwise.</param>
public sealed record CrucibleStageOption(CrucibleStageType Type, int Battle);

/// <summary>One space on a board.</summary>
/// <param name="Move">The move it is on (1 = first after the start).</param>
/// <param name="Side">0 = the only space on that move, else its place on the move from the left (1 = left).</param>
/// <param name="Battle">The XBMBattleDetail row for an enemy, elite or boss space; 0 otherwise.</param>
/// <param name="Options">What a random space can be; empty for the others.</param>
/// <param name="At">Where the space is on the board, from the board's layout (<see cref="CrucibleBoardLayout"/>); null when unknown.</param>
/// <param name="Node">Its subrow in the board's XBMContentStageEvent: the node id the board's map uses (0 = the start).</param>
/// <param name="Next">The nodes it leads to, from the board's map (XBMContentStageEventMap); null when there is no map.</param>
/// <param name="Spaces">How many spaces its move has (3 on some moves of the Second Master's Board).</param>
public sealed record CrucibleStage(
    int Move, int Side, CrucibleStageType Type, int Battle, IReadOnlyList<CrucibleStageOption> Options,
    System.Numerics.Vector3? At = null, int Node = 0, IReadOnlyList<int>? Next = null, int Spaces = 1)
{
    public bool IsBattle => Type is CrucibleStageType.Enemy or CrucibleStageType.Elite or CrucibleStageType.Boss;

    public string SideLabel => (Side, Spaces) switch
    {
        (0, _) => "",
        (1, _) => "L",
        (2, 3) => "M",
        (2, _) or (3, 3) => "R",
        _ => Side.ToString(),
    };
}

/// <summary>A row of XBMContentStageEvent, as read: (move, type, index, id).</summary>
public readonly record struct CrucibleStageRow(int Move, byte Type, int Index);

/// <summary>
/// A row of XBMContentStageEventMap, the board as the game draws it: a grid cell (<paramref name="X"/> grows east,
/// <paramref name="Y"/> grows south), what is drawn there (<paramref name="Kind"/> 1 = the space <paramref name="Node"/>
/// itself; 4-10 = a path from <paramref name="Node"/> to <paramref name="Next"/>; 0 = padding) and the two nodes. Nodes
/// are the board's XBMContentStageEvent subrows, 0 = the start. Read off the client 2026-10-31: on boards 1-4 it puts
/// every space where the strip layout does; the Second Master's Board is a grid whose moves step sideways.
/// </summary>
public readonly record struct CrucibleMapRow(int X, int Y, int Kind, int Node, int Next);

/// <summary>
/// Turns a board's raw stage rows into its route. Pure.
/// <para>
/// Read off the client and checked against two published layouts (consolegameswiki, boards 1 and 2), space for space:
/// column 0 is the move, column 1 the type (1 start, 2 enemy, 3 elite, 4 boss, 5 shop, 6 campsite, 7 treasure,
/// 8 random), column 2 an index — for a battle, the subrow of the board's XBMContentBattle (so 0 is the final boss);
/// for a random space, the subrow of the board's XBMContentRandomStageEvent. Two rows on one move are a fork, left first
/// (three on some moves of the Second Master's Board). With the board's map, each space also gets where it leads.
/// </para>
/// </summary>
public static class CrucibleRouteBuilder
{
    /// <param name="battles">The board's XBMContentBattle values, by subrow (subrow 0 = the final boss).</param>
    /// <param name="randomPools">The board's random spaces, by subrow: what each can turn out to be.</param>
    /// <param name="map">The board's XBMContentStageEventMap rows; without them (or empty) no space knows where it leads.</param>
    public static IReadOnlyList<CrucibleStage> Build(
        IEnumerable<CrucibleStageRow> rows, IReadOnlyList<int> battles,
        IReadOnlyList<IReadOnlyList<(byte Type, int Index)>> randomPools, IReadOnlyList<CrucibleMapRow>? map = null)
    {
        int BattleAt(int index) => index >= 0 && index < battles.Count ? battles[index] : 0;
        var links = map is { Count: > 0 }
            ? map.Where(m => m.Kind > 1).GroupBy(m => m.Node).ToDictionary(g => g.Key, g => g.Select(m => m.Next).Distinct().ToList())
            : null;

        var stages = new List<CrucibleStage>();
        foreach (var group in rows.Select((Row, Node) => (Row, Node))
                     .Where(r => r.Row.Move >= 1 && r.Row.Type != (byte)CrucibleStageType.Start).GroupBy(r => r.Row.Move).OrderBy(g => g.Key))
        {
            var spaces = group.ToList();
            for (var i = 0; i < spaces.Count; i++)
            {
                var (row, node) = spaces[i];
                var type = (CrucibleStageType)row.Type;
                var side = spaces.Count == 1 ? 0 : i + 1;
                var battle = type is CrucibleStageType.Enemy or CrucibleStageType.Elite or CrucibleStageType.Boss ? BattleAt(row.Index) : 0;
                var options = type == CrucibleStageType.Random && row.Index >= 0 && row.Index < randomPools.Count
                    ? randomPools[row.Index]
                        .Select(o => new CrucibleStageOption((CrucibleStageType)o.Type,
                            o.Type is 2 or 3 ? BattleAt(o.Index) : 0))
                        .ToList()
                    : new List<CrucibleStageOption>();
                stages.Add(new CrucibleStage(row.Move, side, type, battle, options, Node: node,
                    Next: links == null ? null : links.GetValueOrDefault(node) ?? [], Spaces: spaces.Count));
            }
        }

        return stages;
    }
}

/// <summary>Which beasts to bring to each battle, and which way to go at a fork. Pure.</summary>
public static class CrucibleRoutePlan
{
    /// <summary>
    /// Three captured familiars for one battle: the leader's weakness first, then the rest of the lineup's, then the
    /// sturdier (later-caught) beast. The final boss gets the community team when it fits (<see cref="CrucibleRunPlan"/>).
    /// </summary>
    public static IReadOnlyList<BstFamiliar> TeamFor(
        CrucibleIndex index, CrucibleBoard board, int battle, Func<int, bool> isCaptured)
    {
        if (battle == board.FinalBattle && CrucibleRunPlan.RecommendTeam(index, board, isCaptured) is { Source: not "matched to weaknesses" } curated)
            return curated.Beasts;

        var lineup = index.EnemiesInBattle(battle);
        var leader = lineup.FirstOrDefault()?.Weakness ?? CrucibleElement.None;
        var others = lineup.Skip(1).Select(e => e.Weakness).Where(w => w != CrucibleElement.None).ToList();

        return BstFamiliars.All
            .Where(b => isCaptured(b.BestiaryNo))
            .Select(b => (Beast: b, Attack: BstFamiliarAttacks.Of(b.BestiaryNo)))
            .Where(x => x.Attack != CrucibleElement.None && (x.Attack == leader || others.Contains(x.Attack)))
            .OrderByDescending(x => x.Attack == leader)
            .ThenByDescending(x => others.Count(w => w == x.Attack))
            .ThenByDescending(x => x.Beast.CaptureLevel)
            .Select(x => x.Beast)
            .Take(3)
            .ToList();
    }

    /// <summary>How many of a battle's enemies one of these beasts hits on its weakness.</summary>
    public static int Coverage(CrucibleIndex index, int battle, IReadOnlyList<BstFamiliar> team)
    {
        var attacks = team.Select(b => BstFamiliarAttacks.Of(b.BestiaryNo)).ToHashSet();
        return index.EnemiesInBattle(battle).Count(e => e.Weakness != CrucibleElement.None && attacks.Contains(e.Weakness));
    }

    /// <summary>
    /// The side to take at a fork, for an easy clear: a campsite over anything (HP never refills otherwise), a normal
    /// enemy over an elite, between two battles the one your beasts cover better, a treasure over a random. 0 when the
    /// move is not a fork.
    /// <para>
    /// For Legendary (<paramref name="legendary"/>) a campsite is last (resting costs Sleepless Survival, 1,000), then a
    /// random space (it can turn out to be a campsite), and a shop beats a treasure (a coffer of only beast gear costs
    /// Nameday Attire; a shop is left without buying). An elite in <see cref="LegendaryElites"/> beats everything: its
    /// 800 points are part of that board's Legendary. With the board's map (<paramref name="route"/>) that holds for
    /// one further on too: a side gains as much for each such elite only its lane still reaches (Second Master's Board:
    /// the Chimera at 9L is behind a random space at 7L, which ranks under the shop at 7R; the lanes rejoin before King
    /// Ahriman, so he counts for neither).
    /// </para>
    /// </summary>
    public static int SuggestedSide(
        CrucibleIndex index, CrucibleBoard board, IReadOnlyList<CrucibleStage> fork, Func<int, bool> isCaptured,
        bool legendary = false, IReadOnlyList<CrucibleStage>? route = null)
    {
        if (fork.Count < 2)
            return 0;

        var elitesAhead = legendary && route != null && CrucibleWalkPlan.HasMap(route)
            ? fork.ToDictionary(s => s, s => CrucibleWalkPlan.Reachable(route, s)
                .Where(r => r.IsBattle && IsLegendaryElite(index, r.Battle)).Select(r => r.Node).ToHashSet())
            : null;
        int OnlyThisLane(CrucibleStage s) => elitesAhead == null
            ? 0
            : elitesAhead[s].Count(node => fork.Any(other => other != s && !elitesAhead[other].Contains(node)));

        int Rank(CrucibleStage s) => s.Type switch
        {
            CrucibleStageType.Campsite => legendary ? -10 : 100,
            CrucibleStageType.Treasure => legendary ? 35 : 60,
            CrucibleStageType.Enemy => 50 + Coverage(index, s.Battle, TeamFor(index, board, s.Battle, isCaptured)),
            CrucibleStageType.Random => legendary ? 10 : 40,
            CrucibleStageType.Shop => legendary ? 40 : 30,
            CrucibleStageType.Elite => (legendary && IsLegendaryElite(index, s.Battle) ? 200 : 20)
                                       + Coverage(index, s.Battle, TeamFor(index, board, s.Battle, isCaptured)),
            _ => 0,
        };

        return fork.OrderByDescending(s => Rank(s) + (200 * OnlyThisLane(s))).ThenBy(s => s.Side).First().Side;
    }

    /// <summary>
    /// Elites Legendary goes out of its way for, by BNpcName. Zu piece (Third Board, move 4L): the community route takes it
    /// over the Catoblepas for its 800 points (Ponzi's Third Board guide, 2026-09-22). The Master's Boards' elites from the
    /// 2026-10-31 research (docs/crucible-legendary-strats.md §9.2): Golem on the First, Mindflayer, Chimera and King
    /// Ahriman on the Second. Board 2's elite stays skipped — its Legendary came by the Wyvern, with +1,300 to spare.
    /// </summary>
    public static readonly IReadOnlySet<uint> LegendaryElites = new HashSet<uint>
    {
        14572, // zu piece (Third Board 4L)
        14617, // golem piece (First Master's 7L): without it the board's margin is +50, not +600
        14633, // mindflayer piece (Second Master's 2L, over the shop)
        14663, // chimera piece (Second Master's 9L, over the sphinx)
        14688, // king ahriman piece (Second Master's 13R)
    };

    private static bool IsLegendaryElite(CrucibleIndex index, int battle)
        => index.EnemiesInBattle(battle).Any(e => LegendaryElites.Contains(e.NameId));
}
