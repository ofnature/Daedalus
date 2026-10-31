using System.Linq;
using Daedalus.Data;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>The Crucible result window's rules: the Legendary line, bonus names, and what a run missed.</summary>
public class CrucibleResultTests
{
    // Board 1's bonus values as XBMContent row 1 has them (2026-10 client).
    private static CrucibleBoard Board1()
    {
        var values = new int[33];
        void Set(CrucibleBonus b, int v) => values[(int)b] = v;
        Set(CrucibleBonus.WillingSacrificeI, 250); Set(CrucibleBonus.WillingSacrificeII, 500); Set(CrucibleBonus.WillingSacrificeIII, 750);
        Set(CrucibleBonus.NoBeastLeftBehind, 1000);
        Set(CrucibleBonus.BeastSquadI, 1250); Set(CrucibleBonus.BeastSquadII, 2500); Set(CrucibleBonus.BeastSquadIII, 4250);
        Set(CrucibleBonus.SelfPreservation, 250); Set(CrucibleBonus.Selfless, 1000); Set(CrucibleBonus.Unbeastable, 1250);
        Set(CrucibleBonus.NamedayAttire, 750); Set(CrucibleBonus.EconomicEthos, 750); Set(CrucibleBonus.StarveTheFever, 750);
        Set(CrucibleBonus.CoinClutcher, 500); Set(CrucibleBonus.SleeplessSurvival, 1000);
        foreach (var g in new[] { CrucibleBonus.BeastkinGambit, CrucibleBonus.VilekinGambit, CrucibleBonus.CloudkinGambit, CrucibleBonus.SeedkinGambit,
                     CrucibleBonus.WavekinGambit, CrucibleBonus.ScalekinGambit, CrucibleBonus.SoulkinGambit, CrucibleBonus.AshkinGambit })
            Set(g, 750);
        Set(CrucibleBonus.FirstDegree, 4000); Set(CrucibleBonus.SecondDegree, 6250); Set(CrucibleBonus.ThirdDegree, 9000);
        return new CrucibleBoard(1, 1088, 1339, "First Board of the Unbroken", 30, [6], values);
    }

    [Fact]
    public void TheLegendaryLineIsPerBoard()
    {
        Assert.Equal(17_500, CrucibleResultRules.LegendaryLine(1));
        Assert.Equal(17_875, CrucibleResultRules.LegendaryLine(3));
        Assert.Equal(18_750, CrucibleResultRules.LegendaryLine(4));
        Assert.Equal(0, CrucibleResultRules.LegendaryLine(9));
    }

    /// <summary>The result screen's names map onto the bonus list, hyphens and spaces aside.</summary>
    [Fact]
    public void ResultNamesParse()
    {
        Assert.Equal(CrucibleBonus.BeastSquadIII, CrucibleResultRules.ParseBonus("Beast Squad III"));
        Assert.Equal(CrucibleBonus.SelfPreservation, CrucibleResultRules.ParseBonus("Self-preservation"));
        Assert.Equal(CrucibleBonus.WillingSacrificeIII, CrucibleResultRules.ParseBonus("Willing Sacrifice III"));
        Assert.Null(CrucibleResultRules.ParseBonus("Something New"));
    }

    /// <summary>
    /// Korha's 16,550 Board 1 run (2026-10-27): missed Unbeastable, Selfless, Nameday Attire and Self-preservation —
    /// not the other Squad tiers or Gambits (a better one was earned), and never the Degree bonuses.
    /// </summary>
    [Fact]
    public void A_runs_missed_bonuses_leave_out_what_a_better_one_covers()
    {
        string[] earned = ["No Beast Left Behind", "Beast Squad III", "Economic Ethos", "Starve the Fever", "Coin Clutcher",
            "Sleepless Survival", "Beastkin Gambit"];
        var missed = CrucibleResultRules.Missed(Board1(), earned).Select(m => m.Bonus).ToList();
        Assert.Equal([CrucibleBonus.Unbeastable, CrucibleBonus.Selfless, CrucibleBonus.NamedayAttire, CrucibleBonus.SelfPreservation], missed);
    }

    /// <summary>Willing Sacrifice III earned: No Beast Left Behind (worth more) is still listed as missed.</summary>
    [Fact]
    public void A_better_member_of_the_group_is_still_missed()
    {
        var missed = CrucibleResultRules.Missed(Board1(), ["Willing Sacrifice III", "Beast Squad III"]).Select(m => m.Bonus).ToList();
        Assert.Contains(CrucibleBonus.NoBeastLeftBehind, missed);
        Assert.DoesNotContain(CrucibleBonus.WillingSacrificeI, missed);
    }

    [Fact]
    public void LabelsReadLikeTheGame()
    {
        Assert.Equal("Beast Squad III", CrucibleResultRules.Label(CrucibleBonus.BeastSquadIII));
        Assert.Equal("Self Preservation", CrucibleResultRules.Label(CrucibleBonus.SelfPreservation));
    }
}
