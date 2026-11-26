using System.Linq;

namespace Daedalus.Data;

/// <summary>
/// Whether a Legendary run can pay for something that costs a bonus (a rest, a first item) and still finish over the
/// line. Pure. It counts only what a finished run can depend on, so on Standard with three beasts it says no — that
/// score needs the fragile bonuses too — and on a degree, whose bonus is paid for finishing, it says yes.
/// </summary>
public static class CrucibleScoreBudget
{
    /// <summary>Points kept in hand above the line after a purchase.</summary>
    public const int Reserve = 300;

    // Lost only by something the autopilot itself chooses to do.
    private static bool OursToLose(CrucibleBonus bonus) => bonus is CrucibleBonus.SleeplessSurvival or CrucibleBonus.StarveTheFever
        or CrucibleBonus.NamedayAttire or CrucibleBonus.EconomicEthos or CrucibleBonus.CoinClutcher;

    // Decided at the end by what is held or how many beasts fell: paid as things stand, and not lost in a fight.
    private static bool HeldAtEnd(CrucibleBonus bonus) => bonus is CrucibleBonus.HabitualHoarder or CrucibleBonus.RollingInRiches
        or CrucibleBonus.WalkingArmory or CrucibleBonus.WillingSacrificeI or CrucibleBonus.WillingSacrificeII
        or CrucibleBonus.WillingSacrificeIII;

    private static bool Dependable(CrucibleBonusLine line) => line.State switch
    {
        CrucibleBonusState.Earned => true,
        CrucibleBonusState.Intact => OursToLose(line.Bonus),
        CrucibleBonusState.DecidedAtEnd => HeldAtEnd(line.Bonus),
        _ => false,
    };

    /// <summary>
    /// The score a finished run can depend on: the least the route pays, what is already earned, the bonuses only our
    /// own choices lose, the held-at-the-end ones that are met now, and Willing Sacrifice I in place of an unbroken No
    /// Beast Left Behind (one knock-out leaves that much). Left out: Unbeastable, Selfless, Self-preservation and the
    /// remaining-HP share, which a single hit takes away.
    /// </summary>
    public static int DependableScore(CrucibleBonusSnapshot run)
    {
        var counted = run.Lines.Where(Dependable).ToList();
        var grouped = CrucibleBonusRules.ExclusiveGroups
            .Sum(group => counted.Where(l => group.Contains(l.Bonus)).Select(l => l.Points).DefaultIfEmpty(0).Max());
        var ungrouped = counted
            .Where(l => !CrucibleBonusRules.ExclusiveGroups.Any(group => group.Contains(l.Bonus)))
            .Sum(l => l.Points);
        var unbroken = run.Lines.Any(l => l is { Bonus: CrucibleBonus.NoBeastLeftBehind, State: CrucibleBonusState.Intact });
        var oneKnockOut = unbroken
            ? run.Lines.FirstOrDefault(l => l.Bonus == CrucibleBonus.WillingSacrificeI)?.Points ?? 0
            : 0;
        return run.PerformanceIfFinished + grouped + ungrouped + oneKnockOut;
    }

    /// <summary>
    /// Whether giving up <paramref name="cost"/> is affordable: it is already gone or not paid here; or Legendary is out
    /// of reach even with every bonus kept, so only the clear is left to play for; or the dependable score stays
    /// <see cref="Reserve"/> over the line without it. False when the board's line is unknown.
    /// </summary>
    public static bool Affords(CrucibleBonusSnapshot run, CrucibleBonus cost)
    {
        if (run.Died || run.Finished)
            return false;
        var line = run.Lines.FirstOrDefault(l => l.Bonus == cost);
        if (line == null || line.State == CrucibleBonusState.Lost)
            return true;
        if (run.LegendaryLine <= 0)
            return false;
        if (run.ProjectedIfKept < run.LegendaryLine)
            return true;
        var points = Dependable(line) ? line.Points : 0;
        return DependableScore(run) - points >= run.LegendaryLine + Reserve;
    }

    /// <summary>Points the dependable score stands over the line (negative: under), for the log.</summary>
    public static int Margin(CrucibleBonusSnapshot run) => DependableScore(run) - run.LegendaryLine;
}
