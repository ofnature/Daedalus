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
/// <param name="Side">0 = the only space on that move, 1 = left fork, 2 = right fork.</param>
/// <param name="Battle">The XBMBattleDetail row for an enemy, elite or boss space; 0 otherwise.</param>
/// <param name="Options">What a random space can be; empty for the others.</param>
/// <param name="At">Where the space is on the board, from the board's layout (<see cref="CrucibleBoardLayout"/>); null when unknown.</param>
public sealed record CrucibleStage(
    int Move, int Side, CrucibleStageType Type, int Battle, IReadOnlyList<CrucibleStageOption> Options,
    System.Numerics.Vector3? At = null)
{
    public bool IsBattle => Type is CrucibleStageType.Enemy or CrucibleStageType.Elite or CrucibleStageType.Boss;

    public string SideLabel => Side switch { 1 => "L", 2 => "R", _ => "" };
}

/// <summary>A row of XBMContentStageEvent, as read: (move, type, index, id).</summary>
public readonly record struct CrucibleStageRow(int Move, byte Type, int Index);

/// <summary>
/// Turns a board's raw stage rows into its route. Pure.
/// <para>
/// Read off the client and checked against two published layouts (consolegameswiki, boards 1 and 2), space for space:
/// column 0 is the move, column 1 the type (1 start, 2 enemy, 3 elite, 4 boss, 5 shop, 6 campsite, 7 treasure,
/// 8 random), column 2 an index — for a battle, the subrow of the board's XBMContentBattle (so 0 is the final boss);
/// for a random space, the subrow of the board's XBMContentRandomStageEvent. Two rows on one move are a fork, left first.
/// </para>
/// </summary>
public static class CrucibleRouteBuilder
{
    /// <param name="battles">The board's XBMContentBattle values, by subrow (subrow 0 = the final boss).</param>
    /// <param name="randomPools">The board's random spaces, by subrow: what each can turn out to be.</param>
    public static IReadOnlyList<CrucibleStage> Build(
        IEnumerable<CrucibleStageRow> rows, IReadOnlyList<int> battles,
        IReadOnlyList<IReadOnlyList<(byte Type, int Index)>> randomPools)
    {
        int BattleAt(int index) => index >= 0 && index < battles.Count ? battles[index] : 0;

        var stages = new List<CrucibleStage>();
        foreach (var group in rows.Where(r => r.Move >= 1 && r.Type != (byte)CrucibleStageType.Start).GroupBy(r => r.Move).OrderBy(g => g.Key))
        {
            var spaces = group.ToList();
            for (var i = 0; i < spaces.Count; i++)
            {
                var row = spaces[i];
                var type = (CrucibleStageType)row.Type;
                var side = spaces.Count == 1 ? 0 : i + 1;
                var battle = type is CrucibleStageType.Enemy or CrucibleStageType.Elite or CrucibleStageType.Boss ? BattleAt(row.Index) : 0;
                var options = type == CrucibleStageType.Random && row.Index >= 0 && row.Index < randomPools.Count
                    ? randomPools[row.Index]
                        .Select(o => new CrucibleStageOption((CrucibleStageType)o.Type,
                            o.Type is 2 or 3 ? BattleAt(o.Index) : 0))
                        .ToList()
                    : new List<CrucibleStageOption>();
                stages.Add(new CrucibleStage(row.Move, side, type, battle, options));
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
    /// Nameday Attire; a shop is left without buying).
    /// </para>
    /// </summary>
    public static int SuggestedSide(
        CrucibleIndex index, CrucibleBoard board, IReadOnlyList<CrucibleStage> fork, Func<int, bool> isCaptured,
        bool legendary = false)
    {
        if (fork.Count < 2)
            return 0;

        int Rank(CrucibleStage s) => s.Type switch
        {
            CrucibleStageType.Campsite => legendary ? -10 : 100,
            CrucibleStageType.Treasure => legendary ? 35 : 60,
            CrucibleStageType.Enemy => 50 + Coverage(index, s.Battle, TeamFor(index, board, s.Battle, isCaptured)),
            CrucibleStageType.Random => legendary ? 10 : 40,
            CrucibleStageType.Shop => legendary ? 40 : 30,
            CrucibleStageType.Elite => 20 + Coverage(index, s.Battle, TeamFor(index, board, s.Battle, isCaptured)),
            _ => 0,
        };

        return fork.OrderByDescending(Rank).ThenBy(s => s.Side).First().Side;
    }
}
