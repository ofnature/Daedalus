using System;
using System.Collections.Generic;
using System.Linq;
using Daedalus.Data;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>
/// What a Legendary run can pay for. First Board figures: line 17,500, the route pays 7,550, three bugs earn Beast
/// Squad III 4,250 and the Gambit 750.
/// </summary>
public class CrucibleScoreBudgetTests
{
    private static readonly BeastClassification[] Bugs = [BeastClassification.Vilekin, BeastClassification.Vilekin, BeastClassification.Vilekin];

    private static IReadOnlyList<int> Values(params (CrucibleBonus Bonus, int Points)[] paid)
    {
        var values = new int[Enum.GetValues<CrucibleBonus>().Length];
        foreach (var (bonus, points) in paid)
            values[(int)bonus] = points;
        return values;
    }

    // Board 1's bonus row with the degrees (4,000 and 9,000 seen on result screens; the Second's is a stand-in).
    private static readonly IReadOnlyList<int> Board1Values = Values(
        (CrucibleBonus.BeastSquadIII, 4250), (CrucibleBonus.BeastSquadII, 2500), (CrucibleBonus.BeastSquadI, 1250),
        (CrucibleBonus.NoBeastLeftBehind, 1000), (CrucibleBonus.Unbeastable, 1250), (CrucibleBonus.Selfless, 1000),
        (CrucibleBonus.SleeplessSurvival, 1000), (CrucibleBonus.StarveTheFever, 750), (CrucibleBonus.NamedayAttire, 750),
        (CrucibleBonus.EconomicEthos, 750), (CrucibleBonus.CoinClutcher, 500), (CrucibleBonus.SelfPreservation, 250),
        (CrucibleBonus.WillingSacrificeI, 250), (CrucibleBonus.WillingSacrificeII, 500), (CrucibleBonus.WillingSacrificeIII, 750),
        (CrucibleBonus.VilekinGambit, 750), (CrucibleBonus.BeastkinGambit, 750),
        (CrucibleBonus.FirstDegree, 4000), (CrucibleBonus.SecondDegree, 6500), (CrucibleBonus.ThirdDegree, 9000));

    // 1 enemy, 2 enemy | enemy, 3 campsite | treasure, 4 enemy, 5 treasure, 6 elite, 7 treasure | campsite, 8 shop, 9 boss.
    private static readonly IReadOnlyList<CrucibleStage> Board1Route = CrucibleRouteBuilder.Build(
        [
            new(0, 1, -1), new(1, 2, 1), new(2, 2, 2), new(2, 2, 3), new(3, 6, 0), new(3, 7, 0), new(4, 2, 4),
            new(5, 7, 1), new(6, 3, 5), new(7, 7, 2), new(7, 6, 1), new(8, 5, 0), new(9, 4, 0),
        ],
        [6, 1, 2, 3, 4, 5], []);

    private static readonly CrucibleBoard Board1 =
        new(1, 1088, 1339, "First Board of the Unbroken", 30, [6, 1, 2, 3, 4, 5], Board1Values, Board1Route, RosterCap: 10, RankCap: 5);

    private static CrucibleBonusTracker Run(int degree = 0)
    {
        var run = new CrucibleBonusTracker(Board1, Bugs);
        run.SetDegree(degree);
        return run;
    }

    // --- the degree's bonus ---

    [Fact]
    public void TheDegreeEnteredOn_IsEarned_AndTheOthersAreNot()
    {
        var lines = Run(degree: 1).Snapshot().Lines;

        Assert.Equal(CrucibleBonusState.Earned, lines.Single(l => l.Bonus == CrucibleBonus.FirstDegree).State);
        Assert.Equal(CrucibleBonusState.Lost, lines.Single(l => l.Bonus == CrucibleBonus.ThirdDegree).State);
    }

    [Fact]
    public void TheDegreeBonus_IsInTheSureScore()
        => Assert.Equal(Run().Snapshot().Sure + 4000, Run(degree: 1).Snapshot().Sure);

    [Fact]
    public void ADegreeOnceRead_IsNotTakenBackByAZero()
    {
        var run = Run(degree: 3);
        run.SetDegree(0);

        Assert.Equal(CrucibleBonusState.Earned, run.Snapshot().Lines.Single(l => l.Bonus == CrucibleBonus.ThirdDegree).State);
    }

    // --- the dependable score ---

    [Fact]
    public void Standard_DependableScore_LeavesTheFragileBonusesOut()
    {
        // 7,550 route + 4,250 squad + 750 gambit + rest/feed/gear/item/tokens 3,750 + one knock-out's 250.
        Assert.Equal(16_550, CrucibleScoreBudget.DependableScore(Run().Snapshot()));
    }

    [Fact]
    public void FirstDegree_AddsItsBonusToTheDependableScore()
        => Assert.Equal(20_550, CrucibleScoreBudget.DependableScore(Run(degree: 1).Snapshot()));

    [Fact]
    public void AKnockOut_LeavesTheDependableScoreWhereItWas()
    {
        var run = Run(degree: 1);
        run.FamiliarKnockedOut();

        Assert.Equal(20_550, CrucibleScoreBudget.DependableScore(run.Snapshot()));
    }

    // --- what it pays for ---

    [Theory]
    [InlineData(CrucibleBonus.EconomicEthos)]
    [InlineData(CrucibleBonus.SleeplessSurvival)]
    public void Standard_CannotPayForARestOrAnItem(CrucibleBonus cost)
        => Assert.False(CrucibleScoreBudget.Affords(Run().Snapshot(), cost));

    [Theory]
    [InlineData(CrucibleBonus.EconomicEthos)]
    [InlineData(CrucibleBonus.SleeplessSurvival)]
    public void FirstDegree_PaysForARestOrAnItem(CrucibleBonus cost)
        => Assert.True(CrucibleScoreBudget.Affords(Run(degree: 1).Snapshot(), cost));

    [Fact]
    public void FirstDegree_PaysForARestAnItemAndGearTogether()
    {
        var run = Run(degree: 1);
        run.Rested();
        run.ItemUsed();

        // 20,550 - 1,000 - 750 = 18,800; gear would leave 18,050, still 550 over.
        Assert.True(CrucibleScoreBudget.Affords(run.Snapshot(), CrucibleBonus.NamedayAttire));
        Assert.Equal(1_300, CrucibleScoreBudget.Margin(run.Snapshot()));
    }

    [Fact]
    public void TheReserveIsKept()
    {
        var run = Run(degree: 1);
        run.Rested();
        run.ItemUsed();
        run.GearAcquired();
        run.TokensSpent(100);

        // 17,550 left: feeding (750) would end 700 under the line.
        Assert.Equal(50, CrucibleScoreBudget.Margin(run.Snapshot()));
        Assert.False(CrucibleScoreBudget.Affords(run.Snapshot(), CrucibleBonus.StarveTheFever));
    }

    [Fact]
    public void ABonusAlreadyLost_CostsNothingMore()
    {
        var run = Run();
        run.ItemUsed();

        Assert.True(CrucibleScoreBudget.Affords(run.Snapshot(), CrucibleBonus.EconomicEthos));
    }

    [Fact]
    public void LegendaryOutOfReach_SpendsToClear()
    {
        // Ten beasts of mixed kin on Standard: no squad, no gambit — nothing left to protect.
        var run = new CrucibleBonusTracker(Board1, Enumerable.Repeat(BeastClassification.Beastkin, 5).Concat(Enumerable.Repeat(BeastClassification.Vilekin, 6)));

        Assert.True(run.Snapshot().ProjectedIfKept < run.Snapshot().LegendaryLine);
        Assert.True(CrucibleScoreBudget.Affords(run.Snapshot(), CrucibleBonus.SleeplessSurvival));
    }

    [Fact]
    public void ADeadRun_PaysForNothing()
    {
        var run = Run(degree: 1);
        run.BeastmasterDied();

        Assert.False(CrucibleScoreBudget.Affords(run.Snapshot(), CrucibleBonus.EconomicEthos));
    }

    // --- the campsite ---

    [Theory]
    [InlineData(true, 0.59f, true)]
    [InlineData(true, 0.60f, false)] // hurt less than this, the rest buys little
    [InlineData(false, 0.10f, false)] // the score cannot pay: Sleepless Survival is kept
    public void LegendaryRests_WhenTheScorePaysAndItIsNeeded(bool affords, float hp, bool rests)
        => Assert.Equal(rests, CrucibleAutoPilotRules.LegendaryRests(affords, hp));

    // --- the ledger ---

    private static CrucibleLedgerEntry Entry(string rank, string completion, string lastEnemy = "", int degree = 1, int roster = 3, int board = 1) =>
        new(new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc), "Toon", board, "First Board of the Unbroken",
            CrucibleLoopRules.DegreeName(degree), degree, "Legendary", Enumerable.Range(1, roster).ToList(), 18_050, rank, 17_500,
            completion, 7_550, 10_500, ["First Degree 4000"], 900, 1_470, 400, 0, lastEnemy);

    [Fact]
    public void ALedgerLine_ReadsBackAsWritten()
    {
        var entry = Entry("Legendary", "100%");
        var back = CrucibleRunLedger.Parse(CrucibleRunLedger.ToLine(entry));

        Assert.NotNull(back);
        Assert.Equal(entry.Total, back!.Total);
        Assert.Equal(entry.Roster, back.Roster);
        Assert.Equal(entry.Bonuses, back.Bonuses);
        Assert.True(back.Legendary);
        Assert.True(back.Cleared);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    public void ABadLedgerLine_IsSkipped(string line)
        => Assert.Null(CrucibleRunLedger.Parse(line));

    [Fact]
    public void TheSummary_CountsOnlyRunsMadeTheSameWay()
    {
        var like = Entry("Legendary", "100%");
        CrucibleLedgerEntry[] runs =
        [
            like,
            Entry("Apex", "100%"),
            Entry("Adept", "55%", "Banemite"),
            Entry("Adept", "55%", "Banemite"),
            Entry("Legendary", "100%", degree: 0),   // Standard: another way
            Entry("Legendary", "100%", roster: 10),  // full roster: another way
            Entry("Legendary", "100%", board: 2),    // another board
        ];

        var summary = CrucibleRunLedger.Summarize(runs, like);

        Assert.Equal(4, summary.Runs);
        Assert.Equal(2, summary.Cleared);
        Assert.Equal(1, summary.Legendary);
        Assert.Equal(("Banemite", 2), summary.EndedAt.Single());
        Assert.Equal("Legendary 1 of 4 (25%), cleared 2; ended at Banemite x2", summary.Describe());
    }

    [Theory]
    [InlineData("Standard", 0)]
    [InlineData("First Degree", 1)]
    [InlineData("Third Degree", 3)]
    [InlineData("", 0)]
    public void TheResultScreensMode_NamesTheDegree(string mode, int degree)
        => Assert.Equal(degree, CrucibleLoopRules.DegreeOfMode(mode));
}
