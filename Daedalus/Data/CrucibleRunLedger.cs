using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Daedalus.Data;

/// <summary>One finished or failed board run, as its result screen and the bonus tracker had it.</summary>
/// <param name="Degree">0 Standard, 1-3 the degrees.</param>
/// <param name="Roster">The beasts registered, by bestiary number.</param>
/// <param name="LastEnemy">The last enemy fought: where a run that did not finish ended.</param>
public sealed record CrucibleLedgerEntry(
    DateTime Utc,
    string Toon,
    int BoardIndex,
    string Board,
    string Mode,
    int Degree,
    string Goal,
    IReadOnlyList<int> Roster,
    int Total,
    string Rank,
    int Line,
    string Completion,
    int Performance,
    int BonusTotal,
    IReadOnlyList<string> Bonuses,
    int RemainingHp,
    int MaxHp,
    int HpPoints,
    int KnockOuts,
    string LastEnemy)
{
    public bool Cleared => Completion == "100%";

    public bool Legendary => string.Equals(Rank, "Legendary", StringComparison.OrdinalIgnoreCase);
}

/// <summary>How often one way of running a board has paid off.</summary>
public sealed record CrucibleLedgerSummary(int Runs, int Cleared, int Legendary, IReadOnlyList<(string Enemy, int Count)> EndedAt)
{
    public string Describe() =>
        Runs == 0
            ? "no runs yet"
            : $"Legendary {Legendary} of {Runs} ({100.0 * Legendary / Runs:0}%), cleared {Cleared}"
              + (EndedAt.Count > 0 ? "; ended at " + string.Join(", ", EndedAt.Select(e => $"{e.Enemy} x{e.Count}")) : "");
}

/// <summary>
/// The run ledger: every board result kept as one JSON line, so the rate at which a setup reaches Legendary can be
/// counted instead of remembered. Pure; the file is <c>CrucibleRunLedgerFile</c>'s.
/// </summary>
public static class CrucibleRunLedger
{
    public static string ToLine(CrucibleLedgerEntry entry) => JsonSerializer.Serialize(entry);

    /// <summary>Null for a blank or unreadable line: one bad line must not lose the rest.</summary>
    public static CrucibleLedgerEntry? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;
        try
        {
            return JsonSerializer.Deserialize<CrucibleLedgerEntry>(line);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The runs made the same way as <paramref name="like"/>: same board, degree and Beast Squad tier (the roster's
    /// size decides the score, not which beasts).
    /// </summary>
    public static CrucibleLedgerSummary Summarize(IEnumerable<CrucibleLedgerEntry> entries, CrucibleLedgerEntry like)
    {
        var same = entries.Where(e => e.BoardIndex == like.BoardIndex && e.Degree == like.Degree
                                      && CrucibleBonusRules.BeastSquadFor(e.Roster.Count) == CrucibleBonusRules.BeastSquadFor(like.Roster.Count))
            .ToList();
        var endedAt = same.Where(e => !e.Cleared && e.LastEnemy.Length > 0)
            .GroupBy(e => e.LastEnemy)
            .Select(g => (g.Key, g.Count()))
            .OrderByDescending(g => g.Item2)
            .ToList();
        return new CrucibleLedgerSummary(same.Count, same.Count(e => e.Cleared), same.Count(e => e.Legendary), endedAt);
    }
}
