using System;
using System.Collections.Generic;
using System.Linq;

namespace Daedalus.Data;

/// <summary>What a Crucible run is for, which decides the roster and the horns.</summary>
public enum CrucibleTeamGoal
{
    /// <summary>Beat the board: the core team plus the beasts that cover each battle's weakness; horns by weakness.</summary>
    Clear,

    /// <summary>
    /// Level beasts: the core team carries every battle on the horns, and the rest of the roster is the lowest-rank
    /// beasts below 25 — every beast that goes into a run banks Crucible EXP, horn or not.
    /// </summary>
    Level,

    /// <summary>
    /// Score for Legendary: three beasts of one kin (Beast Squad III 4,250 + a Kin Gambit 750) on the roster and the horns,
    /// and none of the things that cost a bonus: no beast gear, no rest, no shopping (but the last shop's items that make
    /// ten held for Habitual Hoarder, when that pays more than Coin Clutcher). Research 2026-10-27
    /// (docs/crucible-legendary.md): with no familiar falling that is ~17,300 + HP on Board 1 against a 17,500 line.
    /// </summary>
    Legendary,
}

/// <summary>The core team and the levelling roster. Pure.</summary>
public static class CrucibleLevelPlan
{
    /// <summary>The highest beast rank.</summary>
    public const int MaxRank = 25;

    /// <summary>
    /// The three beasts that carry: the player's choices (Bestiary numbers, 0 = pick for me) where captured, the rest
    /// filled with the highest-rank captured beasts.
    /// </summary>
    public static IReadOnlyList<int> Core(IReadOnlyList<int> chosen, IEnumerable<int> captured, Func<int, int> rankOf)
    {
        var owned = captured.ToList();
        var core = chosen.Where(no => no != 0 && owned.Contains(no)).Distinct().Take(3).ToList();
        core.AddRange(owned.Where(no => !core.Contains(no))
            .OrderByDescending(rankOf)
            // Equal ranks (board 1 shows everyone at 5 or under): the later-caught beast has the stronger base —
            // field 2026-10-25, Opo-opo / Wespe / Mantis took the core over Damselfly on a lowest-number tie-break.
            .ThenByDescending(no => BstFamiliars.ByBestiaryNo((uint)no)?.CaptureLevel ?? 0)
            .ThenBy(no => no)
            .Take(3 - core.Count));
        return core;
    }

    /// <summary>
    /// The levelling roster, up to the cap: the core first, then the captured beasts furthest from rank 25 (lowest
    /// rank first; a rank never seen counts as 1). Beasts already at 25 are left home.
    /// </summary>
    public static IReadOnlyList<int> LevelRoster(IReadOnlyList<int> core, IEnumerable<int> captured, Func<int, int> rankOf, int cap)
    {
        var roster = core.Take(cap).ToList();
        roster.AddRange(captured
            .Where(no => !roster.Contains(no) && rankOf(no) < MaxRank)
            .OrderBy(rankOf)
            .ThenBy(no => no)
            .Take(Math.Max(0, cap - roster.Count)));
        return roster;
    }

    /// <summary>The clearing roster: the core first, then <see cref="CrucibleHornPlan.Roster"/>'s coverage picks, up to the cap.</summary>
    public static IReadOnlyList<int> ClearRoster(
        CrucibleIndex index, CrucibleBoard board, IReadOnlyList<int> core, Func<int, bool> isCaptured)
    {
        var cap = board.RosterCap > 0 ? board.RosterCap : 10;
        return core.Concat(CrucibleHornPlan.Roster(index, board, isCaptured).Select(b => b.BestiaryNo))
            .Distinct()
            .Take(cap)
            .ToList();
    }

    /// <summary>
    /// The horn picks the setter works from for one battle: the player's picks for that battle when there are any;
    /// otherwise, when levelling, the core team; otherwise none (all Auto). For Legendary <paramref name="core"/> is the
    /// three-beast team, and a per-battle pick outside it is ignored (the roster holds only those three).
    /// </summary>
    /// <para>Legendary with no pick of the player's: the core in <see cref="LegendaryHornOrder"/> for the battle's lead
    /// enemy — poison-immune by its resist row (<paramref name="lead"/>), or by name when the row was not read.</para>
    public static IReadOnlyList<int>? HornChoice(
        CrucibleTeamGoal goal, IReadOnlyList<int>? perBattle, IReadOnlyList<int> core, CrucibleEnemy? lead = null)
    {
        var picked = perBattle != null && perBattle.Any(no => no != 0);
        if (goal == CrucibleTeamGoal.Legendary)
            return picked && perBattle!.Where(no => no != 0).All(core.Contains)
                ? perBattle
                : LegendaryHornOrder(core, IsPoisonImmune(lead));
        if (picked)
            return perBattle;
        return goal == CrucibleTeamGoal.Level ? core : null;
    }

    /// <summary>The Legendary roster size: Beast Squad III is a team of 1 to 3.</summary>
    public const int LegendaryTeamSize = 3;

    /// <summary>
    /// The "Bug team" (Bestiary numbers): Damselfly poisons, Mantis puts physical vulnerability on, Wespe's Final Sting
    /// (2,000-3,000 potency) finishes. Meoni runs it on every Legendary board ("very overpowered"); vilekin, so it also
    /// pays the Vilekin Gambit.
    /// </summary>
    public const int Damselfly = 44, Mantis = 16, Wespe = 10;

    private static readonly int[] BugTeam = [Damselfly, Mantis, Wespe];

    /// <summary>Bestiary numbers of the Beastkin team for the Third Board.</summary>
    public const int CuSith = 1, Coeurl = 33, Behemoth = 50;

    /// <summary>
    /// A board's own Legendary team, in horn order, when a guide names one; null otherwise. Third Board: Beastkin — Cu
    /// Sith starts, Coeurl's debuff buffs Behemoth's Trick, Behemoth is the damage (Ponzi, "How To Get Legendary On The
    /// Third Board", 2026-09). At the board's rank sync it ties the Bug team, which then won the tie (Korha 2026-10-31).
    /// </summary>
    public static IReadOnlyList<int>? BoardLegendaryTeam(int boardIndex) => boardIndex switch
    {
        3 => [CuSith, Coeurl, Behemoth],
        _ => null,
    };

    /// <summary>
    /// Lead enemies (BNpcName, lower case, matched as a substring) that poison does not land on, so the Bug team leads
    /// with Mantis. From Meoni's Legendary runs (2026-09): bone knight/bishop, banemite, Pas de Seul, Voidmancer, Golem,
    /// Borgny. Everything else is treated as poisonable (the wiki lists most bosses as vulnerable to Poison).
    /// </summary>
    public static readonly IReadOnlyList<string> PoisonImmuneLeads =
        ["bone knight", "bone bishop", "banemite", "pas de seul", "voidmancer", "golem", "borgny"];

    /// <summary>
    /// Whether poison does not land on this lead enemy: its resist row when read (the game's own data — the Taurus,
    /// Voidmancer, Gargoyle and Chimera are immune too, which the name list missed), else <see cref="PoisonImmuneLeads"/>.
    /// </summary>
    public static bool IsPoisonImmune(CrucibleEnemy? lead)
        => lead?.TakesStatus(CrucibleStatus.Poison) is { } takes ? !takes : IsPoisonImmune(lead?.Name);

    /// <summary>Whether poison does not land on this lead enemy (<see cref="PoisonImmuneLeads"/>).</summary>
    public static bool IsPoisonImmune(string? leadEnemyName)
        => !string.IsNullOrEmpty(leadEnemyName)
           && PoisonImmuneLeads.Any(n => leadEnemyName.Contains(n, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The horn order for one battle. The Bug team goes Damselfly → Mantis → Wespe against a poisonable boss (poison,
    /// vulnerability, then Final Sting into it) and Mantis → Wespe → Damselfly against a poison-immune one (Meoni);
    /// anything else keeps its order.
    /// </summary>
    public static IReadOnlyList<int> LegendaryHornOrder(IReadOnlyList<int> team, bool poisonImmune)
    {
        if (!BugTeam.All(team.Contains))
            return team;
        int[] order = poisonImmune ? [Mantis, Wespe, Damselfly] : [Damselfly, Mantis, Wespe];
        return order.Concat(team.Where(no => !BugTeam.Contains(no))).ToList();
    }

    /// <summary>
    /// The Legendary team: three captured beasts of ONE kin (the Kin Gambit), strongest first. The player's core picks
    /// lead when they are all one kin; otherwise the kin whose best three rank highest (capped at the board's rank sync,
    /// since rank above it does nothing there), then the higher weakest member, then the Bug team. Within a kin the Bug
    /// team's members go first among equal capped ranks.
    /// <para>On Board 1 every beast syncs to 5, so every kin ties and the first kin in the list won: Goobbue, Buffalo,
    /// Coeurl, with nothing to finish a boss (Korha 2026-10-27, died at Pas de Seul). The Bug team now wins that tie.</para>
    /// Fewer than three when no kin has three.
    /// <para>Without picks of the player's, the board's own team (<see cref="BoardLegendaryTeam"/>) is fielded when all
    /// three are captured and its weakest is no weaker (capped) than the weakest of the team worked out otherwise.</para>
    /// </summary>
    public static IReadOnlyList<int> LegendaryTeam(
        IReadOnlyList<int> chosen, IEnumerable<int> captured, Func<int, int> rankOf, int rankCap = MaxRank, int boardIndex = 0)
    {
        var pool = captured.Distinct()
            .Select(no => (No: no, Beast: BstFamiliars.ByBestiaryNo((uint)no)))
            .Where(x => x.Beast != null)
            .ToList();
        int Capped(int no) => Math.Min(rankOf(no), rankCap > 0 ? rankCap : MaxRank);

        var picks = chosen.Where(no => no != 0 && pool.Any(x => x.No == no)).Distinct().ToList();
        var pickedKin = picks.Select(no => pool.First(x => x.No == no).Beast!.Classification).Distinct().ToList();
        if (pickedKin.Count == 1)
            return picks
                .Concat(pool.Where(x => x.Beast!.Classification == pickedKin[0] && !picks.Contains(x.No))
                    .OrderByDescending(x => rankOf(x.No)).ThenBy(x => x.No).Select(x => x.No))
                .Take(LegendaryTeamSize)
                .ToList();

        var team = pool
            .GroupBy(x => x.Beast!.Classification)
            .Select(g => g.OrderByDescending(x => Capped(x.No)).ThenByDescending(x => BugTeam.Contains(x.No))
                .ThenByDescending(x => rankOf(x.No)).ThenBy(x => x.No).Take(LegendaryTeamSize).Select(x => x.No).ToList())
            .OrderByDescending(t => t.Count)
            .ThenByDescending(t => t.Sum(Capped))
            .ThenByDescending(t => t.Min(Capped))
            .ThenByDescending(t => BugTeam.All(t.Contains))
            .FirstOrDefault() ?? [];
        if (BoardLegendaryTeam(boardIndex) is { } own && own.All(no => pool.Any(x => x.No == no))
            && (team.Count < LegendaryTeamSize || own.Min(Capped) >= team.Min(Capped)))
            return own;
        return LegendaryHornOrder(team, poisonImmune: false);
    }

    /// <summary>The toggles that turn the team into the wanted roster: leavers first (room for the joiners), then joiners in order.</summary>
    public static IReadOnlyList<(int PetId, bool Add)> Changes(IReadOnlyList<int> team, IReadOnlyList<int> wanted) =>
        team.Where(no => !wanted.Contains(no)).Select(no => (no, false))
            .Concat(wanted.Where(no => !team.Contains(no)).Select(no => (no, true)))
            .ToList();
}
