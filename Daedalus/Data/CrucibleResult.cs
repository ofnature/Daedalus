using System;
using System.Collections.Generic;
using System.Linq;

namespace Daedalus.Data;

/// <summary>
/// A board's result screen (<c>XBMResult</c>), read the moment it opens: the autopilot closes it within seconds.
/// Layout from recordings (2026-10-27): [0] board, [1] mode, [2] performance (completion + spaces), [3] completion %,
/// [4] completion points, [5]/[6] enemies "x n" / points, [7]/[8] elites, [9]/[10] bosses, [13] bonus total, [14] bonus
/// count, bonus names at 15+i and points at 31+i, [58] remnant name, [63] remnant count, [68] TOTAL, [69] rank.
/// </summary>
public sealed record CrucibleRunResult(
    string Board,
    string Mode,
    int Total,
    string Rank,
    int Performance,
    string CompletionPct,
    int CompletionPoints,
    string Enemies,
    int EnemyPoints,
    string Elites,
    int ElitePoints,
    string Bosses,
    int BossPoints,
    int BonusTotal,
    IReadOnlyList<(string Name, int Points)> Bonuses,
    int Remnants,
    string RemnantName);

/// <summary>What the result window says about a run. Pure.</summary>
public static class CrucibleResultRules
{
    /// <summary>
    /// The score that guarantees Legendary, by board (XBMContent row): the Console Games Wiki's per-board pages and the
    /// community score sheet (lowest Legendary seen 17,500 / 17,512 / 17,875; Master's 18,750 / 18,500).
    /// </summary>
    public static int LegendaryLine(int boardIndex) => boardIndex switch
    {
        1 or 2 => 17_500,
        3 => 17_875,
        4 => 18_750,
        5 => 18_500,
        _ => 0,
    };

    /// <summary>The bonus a result-screen name stands for ("Beast Squad III", "Self-preservation"…), or null.</summary>
    public static CrucibleBonus? ParseBonus(string name)
    {
        var key = new string(name.Where(char.IsLetter).ToArray());
        foreach (var bonus in Enum.GetValues<CrucibleBonus>())
        {
            if (string.Equals(bonus.ToString(), key, StringComparison.OrdinalIgnoreCase))
                return bonus;
        }

        return null;
    }

    // Bonuses that exclude each other: earning one closes the rest of its group.
    private static readonly CrucibleBonus[][] Exclusive =
    [
        [CrucibleBonus.NoBeastLeftBehind, CrucibleBonus.WillingSacrificeI, CrucibleBonus.WillingSacrificeII, CrucibleBonus.WillingSacrificeIII],
        [CrucibleBonus.BeastSquadI, CrucibleBonus.BeastSquadII, CrucibleBonus.BeastSquadIII],
        [CrucibleBonus.NamedayAttire, CrucibleBonus.WalkingArmory],
        [CrucibleBonus.StarveTheFever, CrucibleBonus.FeedTheBold],
        [CrucibleBonus.BeastkinGambit, CrucibleBonus.VilekinGambit, CrucibleBonus.CloudkinGambit, CrucibleBonus.SeedkinGambit,
         CrucibleBonus.WavekinGambit, CrucibleBonus.ScalekinGambit, CrucibleBonus.SoulkinGambit, CrucibleBonus.AshkinGambit],
        [CrucibleBonus.FirstDegree, CrucibleBonus.SecondDegree, CrucibleBonus.ThirdDegree],
        [CrucibleBonus.InfinityAndBeyondI, CrucibleBonus.InfinityAndBeyondII, CrucibleBonus.InfinityAndBeyondIII],
    ];

    /// <summary>
    /// The bonuses this board pays that the run did not earn and could still have, worth most first. A group the run
    /// already scored in (a Squad tier, a Gambit, No Beast Left Behind / Willing Sacrifice…) offers only its better
    /// members; Degree bonuses are left out (a Standard run cannot earn them).
    /// </summary>
    public static IReadOnlyList<(CrucibleBonus Bonus, int Points)> Missed(CrucibleBoard board, IEnumerable<string> earnedNames)
    {
        var earned = earnedNames.Select(ParseBonus).OfType<CrucibleBonus>().ToHashSet();
        var missed = new List<(CrucibleBonus, int)>();
        foreach (var bonus in Enum.GetValues<CrucibleBonus>())
        {
            var points = board.BonusValue(bonus);
            if (points <= 0 || earned.Contains(bonus)
                || bonus is CrucibleBonus.FirstDegree or CrucibleBonus.SecondDegree or CrucibleBonus.ThirdDegree)
                continue;

            var group = Exclusive.FirstOrDefault(g => g.Contains(bonus));
            if (group != null && group.Where(earned.Contains).Select(board.BonusValue).DefaultIfEmpty(0).Max() is var got
                && got >= points)
                continue; // the group's better (or equal) member is already in

            missed.Add((bonus, points));
        }

        return missed.OrderByDescending(m => m.Item2).ThenBy(m => m.Item1).ToList();
    }

    /// <summary>"Beast Squad III" from <see cref="CrucibleBonus.BeastSquadIII"/>, for the window.</summary>
    public static string Label(CrucibleBonus bonus)
    {
        var name = bonus.ToString();
        var chars = new List<char>(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            // A space before a capital that follows a lowercase letter: "BeastSquadIII" → "Beast Squad III".
            if (i > 0 && char.IsUpper(name[i]) && char.IsLower(name[i - 1]))
                chars.Add(' ');
            chars.Add(name[i]);
        }

        return new string(chars.ToArray());
    }
}
