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
    /// and none of the things that cost a bonus: no beast gear, no rest, no shopping. Research 2026-10-27
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
    public static IReadOnlyList<int>? HornChoice(CrucibleTeamGoal goal, IReadOnlyList<int>? perBattle, IReadOnlyList<int> core)
    {
        var picked = perBattle != null && perBattle.Any(no => no != 0);
        if (goal == CrucibleTeamGoal.Legendary)
            return picked && perBattle!.Where(no => no != 0).All(core.Contains) ? perBattle : core;
        if (picked)
            return perBattle;
        return goal == CrucibleTeamGoal.Level ? core : null;
    }

    /// <summary>The Legendary roster size: Beast Squad III is a team of 1 to 3.</summary>
    public const int LegendaryTeamSize = 3;

    /// <summary>
    /// The Legendary team: three captured beasts of ONE kin (the Kin Gambit), strongest first. The player's core picks
    /// lead when they are all one kin; otherwise the kin whose best three rank highest (capped at the board's rank sync,
    /// since rank above it does nothing there), then the higher weakest member. Fewer than three when no kin has three.
    /// </summary>
    public static IReadOnlyList<int> LegendaryTeam(
        IReadOnlyList<int> chosen, IEnumerable<int> captured, Func<int, int> rankOf, int rankCap = MaxRank)
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

        return pool
            .GroupBy(x => x.Beast!.Classification)
            .Select(g => g.OrderByDescending(x => rankOf(x.No)).ThenBy(x => x.No).Take(LegendaryTeamSize).Select(x => x.No).ToList())
            .OrderByDescending(t => t.Count)
            .ThenByDescending(t => t.Sum(Capped))
            .ThenByDescending(t => t.Min(Capped))
            .FirstOrDefault() ?? [];
    }

    /// <summary>The toggles that turn the team into the wanted roster: leavers first (room for the joiners), then joiners in order.</summary>
    public static IReadOnlyList<(int PetId, bool Add)> Changes(IReadOnlyList<int> team, IReadOnlyList<int> wanted) =>
        team.Where(no => !wanted.Contains(no)).Select(no => (no, false))
            .Concat(wanted.Where(no => !team.Contains(no)).Select(no => (no, true)))
            .ToList();
}
