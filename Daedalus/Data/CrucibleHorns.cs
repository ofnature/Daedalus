using System;
using System.Collections.Generic;
using System.Linq;

namespace Daedalus.Data;

/// <summary>One familiar on the board's team, as the horn screen lists it.</summary>
/// <param name="PetId">XBMPet row (= Bestiary number).</param>
/// <param name="Alive">False once incapacitated (0 HP) — it cannot go on a horn.</param>
/// <param name="Rank">Beast rank as last read (0 = not known).</param>
/// <param name="Health">HP left as a fraction of max (1 when not known). HP carries over between a board's battles.</param>
public readonly record struct CrucibleTeamMember(int PetId, bool Alive, int Rank = 0, float Health = 1f);

/// <summary>
/// Which of the board's team to put on the three Battlehorns before a battle, and the clicks that get there. Pure.
/// <para>
/// The horn screen (XBMPetParty, mode 2) picks by POSITION in the team: the agent's selected ids are 0-based indices
/// into the team list, and a click on a team member sends <c>[1 | position]</c> to toggle it (recorded at a campsite,
/// 2026-10-22). Horn order is selection order, so a wrong order is cleared and picked again rather than patched.
/// </para>
/// </summary>
public static class CrucibleHornPlan
{
    /// <summary>
    /// Up to three living team members for this battle, in horn order. The player's own picks for the battle
    /// (<paramref name="chosen"/>, Bestiary numbers, 0 = Auto) go first where they are on the team and standing; the
    /// horns left over are filled with <see cref="Recommend"/>'s picks.
    /// </summary>
    public static IReadOnlyList<int> Pick(
        CrucibleIndex index, CrucibleBoard board, int battle, IReadOnlyList<CrucibleTeamMember> team, IReadOnlyList<int>? chosen = null)
    {
        var picks = new List<int>(3);
        foreach (var petId in chosen ?? [])
        {
            var position = Enumerable.Range(0, team.Count)
                .FirstOrDefault(i => petId != 0 && team[i].Alive && team[i].PetId == petId && !picks.Contains(i), -1);
            if (position >= 0 && picks.Count < 3)
                picks.Add(position);
        }

        foreach (var position in Recommend(index, board, battle, team))
        {
            if (picks.Count < 3 && !picks.Contains(position))
                picks.Add(position);
        }

        return picks;
    }

    /// <summary>
    /// Up to three living team members for this battle, in horn order: the final boss gets the community team if it
    /// is all on the team and alive; otherwise the lead enemy's weakness first, then the rest of the lineup's, then
    /// any living member to fill the horns (a horn left empty is a beast lost). Among equals the strongest goes first:
    /// highest rank, then most HP left — so a horn the weakness cannot fill gets your best beast, not your newest
    /// (field 2026-10-24: Antling and Drake, both rank 1, went in beside Rotting Goobbue while rank-8 Damselfly sat out).
    /// </summary>
    public static IReadOnlyList<int> Recommend(CrucibleIndex index, CrucibleBoard board, int battle, IReadOnlyList<CrucibleTeamMember> team)
    {
        var alive = Enumerable.Range(0, team.Count).Where(i => team[i].Alive).ToList();
        if (alive.Count == 0)
            return [];

        if (battle == board.FinalBattle
            && CrucibleRunPlan.RecommendTeam(index, board, no => alive.Any(i => team[i].PetId == no)) is { Source: not "matched to weaknesses" } curated)
        {
            var positions = curated.Beasts.Select(b => alive.FirstOrDefault(i => team[i].PetId == b.BestiaryNo, -1)).ToList();
            if (positions.All(p => p >= 0))
                return positions;
        }

        var lineup = index.EnemiesInBattle(battle);
        var leader = lineup.FirstOrDefault()?.Weakness ?? CrucibleElement.None;
        var others = lineup.Skip(1).Select(e => e.Weakness).Where(w => w != CrucibleElement.None).ToList();

        var ordered = alive
            .Select(i => (Position: i, Attack: BstFamiliarAttacks.Of(team[i].PetId), Level: BstFamiliars.ByBestiaryNo((uint)team[i].PetId)?.CaptureLevel ?? 0))
            .OrderByDescending(x => leader != CrucibleElement.None && x.Attack == leader)
            .ThenByDescending(x => others.Count(w => w == x.Attack))
            .ThenByDescending(x => EffectiveRank(board, team[x.Position]))
            .ThenByDescending(x => team[x.Position].Health)
            .ThenByDescending(x => x.Level)
            .ThenBy(x => x.Position)
            .Select(x => x.Position)
            .ToList();

        // Worn familiars (HP carries over between battles) go behind healthy ones, keeping their order otherwise: the
        // first horn tanks alone, and one that starts a battle low cannot be Snarled for long (field 2026-10-25: the
        // same worn Damselfly led every battle of a board).
        ordered = ordered.OrderByDescending(p => team[p].Health >= WornHealthPct).ToList();
        return FirstHornTank(board, team, ordered).Take(3).ToList();
    }

    /// <summary>A familiar under this much HP starts a battle worn: it goes on the horns after the healthy ones.</summary>
    public const float WornHealthPct = 0.35f;

    /// <summary>A beast's rank on this board: boards sync ranks down to their cap (board 1: 5).</summary>
    public static int EffectiveRank(CrucibleBoard board, CrucibleTeamMember member) =>
        board.RankCap > 0 ? Math.Min(member.Rank, board.RankCap) : member.Rank;

    /// <summary>Ranks below the board's recommendation a weakness match may sit and still take the first horn.</summary>
    public const int FirstHornRankSlack = 1;

    /// <summary>
    /// The first horn fights alone until it falls (field 2026-10-24/25: the second and third took no damage while the
    /// first lived), so it must be able to take the hits. Keep the weakness pick there only when it is at or near the
    /// board's recommended rank; otherwise the highest-rank, healthiest living beast leads and the weakness picks
    /// follow on horns two and three. Field 2026-10-25: rank-4 Rotting Goobbue led the Voidmancer on a board built
    /// for rank 6+ while rank-9 Damselfly waited. Unknown ranks or recommendation leave the order as it is.
    /// </summary>
    private static IEnumerable<int> FirstHornTank(CrucibleBoard board, IReadOnlyList<CrucibleTeamMember> team, IReadOnlyList<int> ordered)
    {
        if (ordered.Count == 0 || board.RecommendedRank == 0)
            return ordered;

        var lead = ordered[0];
        var leadRank = EffectiveRank(board, team[lead]);
        if (leadRank == 0 || leadRank >= board.RecommendedRank - FirstHornRankSlack)
            return ordered;

        var tank = ordered
            .OrderByDescending(p => team[p].Health >= WornHealthPct)
            .ThenByDescending(p => EffectiveRank(board, team[p]))
            .ThenByDescending(p => team[p].Health)
            .First();
        if (EffectiveRank(board, team[tank]) <= leadRank)
            return ordered;

        return ordered.Where(p => p != tank).Prepend(tank);
    }

    /// <summary>Where the player's picks for one battle are kept (<c>BeastmasterConfig.CrucibleHornPicks</c>).</summary>
    public static string PickKey(CrucibleBoard board, int battle) => $"{board.Index}:{battle}";

    /// <summary>
    /// Whether Daedalus sets the horns: the setting is on and no automation is driving the Crucible. AutoDuty runs the
    /// whole board itself — team, horns, shop, treasure — and its horn picks would be undone by ours and back again.
    /// </summary>
    public static bool ShouldSetHorns(bool enabled, string? automationSource) =>
        enabled && !string.Equals(automationSource, "AutoDuty", StringComparison.Ordinal);

    /// <summary>
    /// The toggles that turn the current selection into the wanted one. What already matches from the first horn on
    /// is kept; past that, the selected members are taken off from the last (so the order of those kept never shifts),
    /// then the missing ones are picked in horn order. Recomputed after every click, so it always continues from what
    /// the game shows (the first version cleared a correct first horn on every pass and toggled it forever).
    /// </summary>
    public static IReadOnlyList<int> Toggles(IReadOnlyList<int> selected, IReadOnlyList<int> wanted)
    {
        var kept = 0;
        while (kept < selected.Count && kept < wanted.Count && selected[kept] == wanted[kept])
            kept++;

        return selected.Skip(kept).Reverse().Concat(wanted.Skip(kept)).ToList();
    }

    /// <summary>
    /// The roster to bring onto a board (the Team Composition screen), up to the board's cap: the final boss's team
    /// first, then every other battle's best match before any battle gets a second, then the seconds, then the thirds
    /// — so the cap is spent covering as many weaknesses as possible rather than three deep on the first few.
    /// </summary>
    public static IReadOnlyList<BstFamiliar> Roster(CrucibleIndex index, CrucibleBoard board, Func<int, bool> isCaptured)
    {
        var cap = board.RosterCap > 0 ? board.RosterCap : 10;
        var roster = new List<BstFamiliar>(cap);

        void Add(BstFamiliar beast)
        {
            if (roster.Count < cap && roster.All(b => b.BestiaryNo != beast.BestiaryNo))
                roster.Add(beast);
        }

        foreach (var beast in CrucibleRoutePlan.TeamFor(index, board, board.FinalBattle, isCaptured))
            Add(beast);

        var teams = board.Route
            .SelectMany(s => s.IsBattle ? [s.Battle] : s.Options.Where(o => o.Battle != 0).Select(o => o.Battle))
            .Distinct()
            .Where(b => b != board.FinalBattle)
            .Select(b => CrucibleRoutePlan.TeamFor(index, board, b, isCaptured))
            .ToList();
        // A battle already covered (one of its beasts is on the roster, e.g. through the final boss's team) waits for
        // the next round.
        for (var depth = 0; depth < 3; depth++)
        {
            foreach (var team in teams)
            {
                if (team.Count(b => roster.Any(r => r.BestiaryNo == b.BestiaryNo)) <= depth
                    && team.FirstOrDefault(b => roster.All(r => r.BestiaryNo != b.BestiaryNo)) is { } next)
                    Add(next);
            }
        }

        return roster;
    }
}
