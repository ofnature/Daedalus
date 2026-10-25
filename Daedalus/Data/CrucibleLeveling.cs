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
    /// otherwise, when levelling, the core team; otherwise none (all Auto).
    /// </summary>
    public static IReadOnlyList<int>? HornChoice(CrucibleTeamGoal goal, IReadOnlyList<int>? perBattle, IReadOnlyList<int> core)
    {
        if (perBattle != null && perBattle.Any(no => no != 0))
            return perBattle;
        return goal == CrucibleTeamGoal.Level ? core : null;
    }

    /// <summary>The toggles that turn the team into the wanted roster: leavers first (room for the joiners), then joiners in order.</summary>
    public static IReadOnlyList<(int PetId, bool Add)> Changes(IReadOnlyList<int> team, IReadOnlyList<int> wanted) =>
        team.Where(no => !wanted.Contains(no)).Select(no => (no, false))
            .Concat(wanted.Where(no => !team.Contains(no)).Select(no => (no, true)))
            .ToList();
}
