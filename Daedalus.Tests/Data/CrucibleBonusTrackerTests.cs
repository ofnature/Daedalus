using System.Collections.Generic;
using System.Linq;
using Daedalus.Data;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>The live Crucible bonus tracker: every bonus row, the exclusive groups, death, and two real Board 1 runs.</summary>
public class CrucibleBonusTrackerTests
{
    private static readonly BeastClassification[] BugTeam = [BeastClassification.Vilekin, BeastClassification.Vilekin, BeastClassification.Vilekin];
    private static readonly BeastClassification[] BeastkinTeam = [BeastClassification.Beastkin, BeastClassification.Beastkin, BeastClassification.Beastkin];

    private static IReadOnlyList<int> Values(params (CrucibleBonus Bonus, int Points)[] paid)
    {
        var values = new int[System.Enum.GetValues<CrucibleBonus>().Length];
        foreach (var (bonus, points) in paid)
            values[(int)bonus] = points;
        return values;
    }

    // Board 1's real bonus values (XBMContent row 1). It pays no Habitual Hoarder, Rolling in Riches, Walking Armory
    // or Feed the Bold.
    private static readonly IReadOnlyList<int> Board1Values = Values(
        (CrucibleBonus.BeastSquadIII, 4250), (CrucibleBonus.BeastSquadII, 2500), (CrucibleBonus.BeastSquadI, 1250),
        (CrucibleBonus.NoBeastLeftBehind, 1000), (CrucibleBonus.Unbeastable, 1250), (CrucibleBonus.Selfless, 1000),
        (CrucibleBonus.SleeplessSurvival, 1000), (CrucibleBonus.StarveTheFever, 750), (CrucibleBonus.NamedayAttire, 750),
        (CrucibleBonus.EconomicEthos, 750), (CrucibleBonus.CoinClutcher, 500), (CrucibleBonus.SelfPreservation, 250),
        (CrucibleBonus.WillingSacrificeI, 250), (CrucibleBonus.WillingSacrificeII, 500), (CrucibleBonus.WillingSacrificeIII, 750),
        (CrucibleBonus.BeastkinGambit, 750), (CrucibleBonus.VilekinGambit, 750), (CrucibleBonus.CloudkinGambit, 750),
        (CrucibleBonus.SeedkinGambit, 750), (CrucibleBonus.WavekinGambit, 750), (CrucibleBonus.ScalekinGambit, 750),
        (CrucibleBonus.SoulkinGambit, 750), (CrucibleBonus.AshkinGambit, 750));

    // Board 1's route (CrucibleRouteTests): 1 enemy, 2 enemy | enemy, 3 campsite | treasure, 4 enemy, 5 treasure,
    // 6 elite, 7 treasure | campsite, 8 shop, 9 boss.
    private static readonly IReadOnlyList<CrucibleStage> Board1Route = CrucibleRouteBuilder.Build(
        [
            new(0, 1, -1), new(1, 2, 1), new(2, 2, 2), new(2, 2, 3), new(3, 6, 0), new(3, 7, 0), new(4, 2, 4),
            new(5, 7, 1), new(6, 3, 5), new(7, 7, 2), new(7, 6, 1), new(8, 5, 0), new(9, 4, 0),
        ],
        [6, 1, 2, 3, 4, 5], []);

    private static readonly CrucibleBoard Board1 =
        new(1, 1088, 1339, "First Board of the Unbroken", 30, [6, 1, 2, 3, 4, 5], Board1Values, Board1Route, RosterCap: 10, RankCap: 5);

    // A board paying what Board 1 does not (Walking Armory, Habitual Hoarder, Rolling in Riches, Feed the Bold); test values.
    private static readonly CrucibleBoard Board3 = new(3, 1090, 1341, "Third Board of the Unbroken", 50, [21, 14], Values(
        (CrucibleBonus.NamedayAttire, 750), (CrucibleBonus.WalkingArmory, 1000), (CrucibleBonus.StarveTheFever, 750),
        (CrucibleBonus.FeedTheBold, 1000), (CrucibleBonus.HabitualHoarder, 500), (CrucibleBonus.RollingInRiches, 500),
        (CrucibleBonus.EconomicEthos, 750), (CrucibleBonus.CoinClutcher, 500)));

    private static CrucibleBonusTracker Bugs() => new(Board1, BugTeam);

    private static CrucibleBonusLine Line(CrucibleBonusSnapshot s, CrucibleBonus bonus) => s.Lines.Single(l => l.Bonus == bonus);

    private static CrucibleBonusState State(CrucibleBonusTracker t, CrucibleBonus bonus) => Line(t.Snapshot(), bonus).State;

    /// <summary>Clears Board 1 the way the Legendary walker goes: 2L, 3R treasure, 7L treasure, the shop left unused.</summary>
    private static void WalkBoard1(CrucibleBonusTracker t)
    {
        t.SpaceCleared(CrucibleStageType.Enemy, 1);
        t.SpaceCleared(CrucibleStageType.Enemy, 2);
        t.SpaceCleared(CrucibleStageType.Treasure, 3);
        t.SpaceCleared(CrucibleStageType.Enemy, 4);
        t.SpaceCleared(CrucibleStageType.Treasure, 5);
        t.SpaceCleared(CrucibleStageType.Elite, 6);
        t.SpaceCleared(CrucibleStageType.Treasure, 7);
        t.SpaceCleared(CrucibleStageType.Shop, 8);
        t.SpaceCleared(CrucibleStageType.Boss, 9);
    }

    // --- listing ---

    [Fact]
    public void ABonusTheBoardDoesNotPayIsNotListed()
    {
        var lines = Bugs().Snapshot().Lines.Select(l => l.Bonus).ToList();
        Assert.DoesNotContain(CrucibleBonus.WalkingArmory, lines);
        Assert.DoesNotContain(CrucibleBonus.HabitualHoarder, lines);
        Assert.DoesNotContain(CrucibleBonus.RollingInRiches, lines);
        Assert.DoesNotContain(CrucibleBonus.FeedTheBold, lines);
        Assert.DoesNotContain(CrucibleBonus.InfinityAndBeyondI, lines);
        Assert.Contains(CrucibleBonus.NoBeastLeftBehind, lines);
    }

    [Fact]
    public void ABoardWithUnreadBonusesListsNothing()
    {
        var bare = new CrucibleBoard(1, 1088, 1339, "First Board of the Unbroken", 30, [6]);
        var s = new CrucibleBonusTracker(bare, BugTeam).Snapshot();
        Assert.Empty(s.Lines);
        Assert.Equal(0, s.IfKept);
        Assert.Equal(CrucibleBonusRules.CompletionPoints, s.PerformanceIfFinished); // no route: completion only
    }

    /// <summary>At the start of an ascetic Bug-team run on Board 1: Beast Squad III + Vilekin Gambit are sure, everything is in play.</summary>
    [Fact]
    public void AtTheStartOnlyTheRosterBonusesAreSure()
    {
        var s = Bugs().Snapshot();
        Assert.Equal(4250 + 750, s.Sure);
        // Every bonus Board 1 pays once: 4,250 + 1,000 + 1,250 + 1,000 + 1,000 + 750 + 750 + 750 + 750 + 500 + 250.
        Assert.Equal(12_250, s.IfKept);
        Assert.Equal(5000 + 2550, s.PerformanceIfFinished);
        Assert.Equal(0, s.PerformanceSoFar);
        Assert.Equal(17_500, s.LegendaryLine);
        Assert.Equal(17_500 - (7550 + 12_250), s.GapIfKept);
        Assert.Equal(17_500 - (7550 + 5000), s.GapSure);
    }

    // --- Beast Squad ---

    [Fact]
    public void BeastSquadIIIIsEarnedWithThreeBeastsOrFewer()
    {
        var t = new CrucibleBonusTracker(Board1, [BeastClassification.Vilekin]);
        Assert.Equal(CrucibleBonusState.Earned, State(t, CrucibleBonus.BeastSquadIII));
        Assert.Equal(CrucibleBonusState.Lost, State(t, CrucibleBonus.BeastSquadII));
        Assert.Equal(CrucibleBonusReason.RosterSize, Line(t.Snapshot(), CrucibleBonus.BeastSquadI).Reason);
    }

    [Fact]
    public void ALargerRosterPaysALowerBeastSquadTier()
    {
        var four = new CrucibleBonusTracker(Board1, Enumerable.Repeat(BeastClassification.Vilekin, CrucibleBonusRules.BeastSquadIIIMaxRoster + 1));
        Assert.Equal(CrucibleBonusState.Earned, State(four, CrucibleBonus.BeastSquadII));
        Assert.Equal(CrucibleBonusState.Lost, State(four, CrucibleBonus.BeastSquadIII));

        var big = new CrucibleBonusTracker(Board1, Enumerable.Repeat(BeastClassification.Vilekin, CrucibleBonusRules.BeastSquadIIMaxRoster + 1));
        Assert.Equal(CrucibleBonusState.Earned, State(big, CrucibleBonus.BeastSquadI));

        var tooBig = new CrucibleBonusTracker(Board1, Enumerable.Repeat(BeastClassification.Vilekin, CrucibleBonusRules.BeastSquadIMaxRoster + 1));
        Assert.All(tooBig.Snapshot().Lines.Where(l => l.Bonus is CrucibleBonus.BeastSquadI or CrucibleBonus.BeastSquadII or CrucibleBonus.BeastSquadIII),
            l => Assert.Equal(CrucibleBonusState.Lost, l.State));
    }

    // --- Kin Gambit ---

    [Fact]
    public void AOneKinRosterEarnsItsGambitOnly()
    {
        var s = Bugs().Snapshot();
        Assert.Equal(CrucibleBonusState.Earned, Line(s, CrucibleBonus.VilekinGambit).State);
        var beastkin = Line(s, CrucibleBonus.BeastkinGambit);
        Assert.Equal(CrucibleBonusState.Lost, beastkin.State);
        Assert.Equal(CrucibleBonusReason.OtherKin, beastkin.Reason);
    }

    [Fact]
    public void AMixedRosterEarnsNoGambit()
    {
        var t = new CrucibleBonusTracker(Board1, [BeastClassification.Vilekin, BeastClassification.Vilekin, BeastClassification.Beastkin]);
        var gambits = t.Snapshot().Lines.Where(l => l.Bonus >= CrucibleBonus.BeastkinGambit && l.Bonus <= CrucibleBonus.AshkinGambit).ToList();
        Assert.Equal(8, gambits.Count);
        Assert.All(gambits, l => Assert.Equal(CrucibleBonusReason.MixedKin, l.Reason));
    }

    /// <summary>GambitFor reads an unknown kin as Ashkin; three unknowns must not earn Ashkin Gambit.</summary>
    [Fact]
    public void AnUnknownKinEarnsNoGambit()
    {
        var t = new CrucibleBonusTracker(Board1, [BeastClassification.Unknown, BeastClassification.Unknown]);
        Assert.Equal(CrucibleBonusState.Lost, State(t, CrucibleBonus.AshkinGambit));
    }

    // --- No Beast Left Behind / Willing Sacrifice ---

    [Fact]
    public void AKnockOutLosesNoBeastLeftBehindForWillingSacrificeI()
    {
        var t = Bugs();
        Assert.Equal(CrucibleBonusState.Intact, State(t, CrucibleBonus.NoBeastLeftBehind));
        Assert.Equal(CrucibleBonusReason.NoKnockOut, Line(t.Snapshot(), CrucibleBonus.WillingSacrificeI).Reason);

        t.FamiliarKnockedOut();
        var s = t.Snapshot();
        Assert.Equal(CrucibleBonusState.Lost, Line(s, CrucibleBonus.NoBeastLeftBehind).State);
        Assert.Equal(CrucibleBonusReason.FamiliarKnockedOut, Line(s, CrucibleBonus.NoBeastLeftBehind).Reason);
        Assert.Equal(CrucibleBonusState.DecidedAtEnd, Line(s, CrucibleBonus.WillingSacrificeI).State);
        Assert.Equal(12_250 - 1000 + 250, s.IfKept); // never both
    }

    [Fact]
    public void MoreKnockOutsRaiseTheWillingSacrificeTier()
    {
        var t = Bugs();
        for (var i = 0; i < CrucibleBonusRules.WillingSacrificeIIMinKnockOuts; i++)
            t.FamiliarKnockedOut();
        Assert.Equal(CrucibleBonusState.DecidedAtEnd, State(t, CrucibleBonus.WillingSacrificeII));
        Assert.Equal(CrucibleBonusReason.OtherTier, Line(t.Snapshot(), CrucibleBonus.WillingSacrificeI).Reason);

        for (var i = CrucibleBonusRules.WillingSacrificeIIMinKnockOuts; i < CrucibleBonusRules.WillingSacrificeIIIMinKnockOuts; i++)
            t.FamiliarKnockedOut();
        var s = t.Snapshot();
        Assert.Equal(CrucibleBonusState.DecidedAtEnd, Line(s, CrucibleBonus.WillingSacrificeIII).State);
        Assert.Equal(CrucibleBonusState.Lost, Line(s, CrucibleBonus.WillingSacrificeII).State);
        Assert.Equal(12_250 - 1000 + 750, s.IfKept);
    }

    // --- Unbeastable ---

    [Theory]
    [InlineData("Damage Down")]
    [InlineData("Vulnerability Up")]
    [InlineData(" vulnerability up ")]
    public void DamageDownOrVulnerabilityUpLosesUnbeastable(string status)
    {
        var t = Bugs();
        t.BeastmasterStatusGained(status);
        Assert.Equal(CrucibleBonusReason.DebuffTaken, Line(t.Snapshot(), CrucibleBonus.Unbeastable).Reason);
    }

    [Fact]
    public void OtherStatusesKeepUnbeastable()
    {
        var t = Bugs();
        t.BeastmasterStatusGained("Bind");
        t.BeastmasterStatusGained("Damage Up");
        Assert.Equal(CrucibleBonusState.Intact, State(t, CrucibleBonus.Unbeastable));
    }

    // --- Selfless / Self-preservation ---

    [Fact]
    public void SelflessIsAtRiskWhileAFamiliarIsHurtAndSettlesAtTheEnd()
    {
        var t = Bugs();
        Assert.Equal(CrucibleBonusState.DecidedAtEnd, State(t, CrucibleBonus.Selfless)); // nothing known yet
        t.SetHp(100, [100, 80, 100]);
        Assert.Equal(CrucibleBonusState.AtRisk, State(t, CrucibleBonus.Selfless));
        Assert.Equal(12_250, t.Snapshot().IfKept); // at risk still counts: it can be healed

        t.SetHp(100, [100, 100, 100]);
        Assert.Equal(CrucibleBonusState.DecidedAtEnd, State(t, CrucibleBonus.Selfless));

        t.SetHp(100, [100, 99.5f, 100]);
        t.Finished();
        var line = Line(t.Snapshot(), CrucibleBonus.Selfless);
        Assert.Equal(CrucibleBonusState.Lost, line.State);
        Assert.Equal(CrucibleBonusReason.HpNotFull, line.Reason);
    }

    [Fact]
    public void SelfPreservationFollowsTheBeastmastersHp()
    {
        var t = Bugs();
        t.SetHp(64, [100, 100, 100]);
        Assert.Equal(CrucibleBonusState.AtRisk, State(t, CrucibleBonus.SelfPreservation));
        t.SetHp(100, [100, 100, 100]);
        t.Finished();
        Assert.Equal(CrucibleBonusState.Earned, State(t, CrucibleBonus.SelfPreservation));
    }

    // --- Sleepless Survival ---

    [Fact]
    public void RestingLosesSleeplessSurvival()
    {
        var t = Bugs();
        t.Rested();
        Assert.Equal(CrucibleBonusReason.Rested, Line(t.Snapshot(), CrucibleBonus.SleeplessSurvival).Reason);
    }

    /// <summary>Past Board 1's last campsite (7R) nothing can cost Sleepless Survival any more: it is sure.</summary>
    [Fact]
    public void SleeplessSurvivalIsSurePastTheLastCampsite()
    {
        var t = Bugs();
        t.SpaceCleared(CrucibleStageType.Treasure, 6);
        Assert.Equal(CrucibleBonusState.Intact, State(t, CrucibleBonus.SleeplessSurvival));
        t.SpaceCleared(CrucibleStageType.Treasure, 7);
        Assert.Equal(CrucibleBonusState.Earned, State(t, CrucibleBonus.SleeplessSurvival));
        Assert.Equal(4250 + 750 + 1000, t.Snapshot().Sure);
    }

    [Fact]
    public void ARandomSpaceThatCanBeACampsiteCanStillCostSleepless()
    {
        var random = new CrucibleStage(2, 0, CrucibleStageType.Random, 0,
            [new CrucibleStageOption(CrucibleStageType.Enemy, 12), new CrucibleStageOption(CrucibleStageType.Campsite, 0)]);
        Assert.True(CrucibleBonusRules.CanRest(random));
        Assert.Equal(0, CrucibleBonusRules.LeastPoints(random));
    }

    // --- Starve the Fever / Feed the Bold ---

    [Fact]
    public void FeedingLosesStarveTheFever()
    {
        var t = Bugs();
        t.Fed();
        Assert.Equal(CrucibleBonusReason.Fed, Line(t.Snapshot(), CrucibleBonus.StarveTheFever).Reason);
    }

    [Fact]
    public void FeedingEarnsFeedTheBoldWhereItIsPaid()
    {
        var t = new CrucibleBonusTracker(Board3, BugTeam);
        Assert.Equal(CrucibleBonusState.Intact, State(t, CrucibleBonus.StarveTheFever));
        Assert.Equal(CrucibleBonusState.AtRisk, State(t, CrucibleBonus.FeedTheBold));

        for (var i = 0; i < CrucibleBonusRules.FeedTheBoldMinFeeds; i++)
            t.Fed();
        var s = t.Snapshot();
        Assert.Equal(CrucibleBonusState.Earned, Line(s, CrucibleBonus.FeedTheBold).State);
        Assert.Equal(CrucibleBonusState.Lost, Line(s, CrucibleBonus.StarveTheFever).State);
        Assert.Equal(1000, s.Sure);
    }

    // --- Nameday Attire / Walking Armory ---

    [Fact]
    public void AcquiringGearLosesNamedayAttire()
    {
        var t = Bugs();
        t.GearAcquired();
        Assert.Equal(CrucibleBonusReason.GearAcquired, Line(t.Snapshot(), CrucibleBonus.NamedayAttire).Reason);
    }

    [Fact]
    public void WalkingArmoryNeedsEnoughGearHeldAtTheEnd()
    {
        var t = new CrucibleBonusTracker(Board3, BugTeam);
        Assert.Equal(CrucibleBonusReason.NotEnoughHeld, Line(t.Snapshot(), CrucibleBonus.WalkingArmory).Reason);

        t.GearAcquired();
        t.SetHeld(CrucibleBonusRules.WalkingArmoryMinGear, 0, 0);
        Assert.Equal(CrucibleBonusState.DecidedAtEnd, State(t, CrucibleBonus.WalkingArmory));

        t.Finished();
        var s = t.Snapshot();
        Assert.Equal(CrucibleBonusState.Earned, Line(s, CrucibleBonus.WalkingArmory).State);
        Assert.Equal(CrucibleBonusState.Lost, Line(s, CrucibleBonus.NamedayAttire).State);
    }

    // --- Economic Ethos / Habitual Hoarder ---

    [Fact]
    public void UsingAnItemLosesEconomicEthos()
    {
        var t = Bugs();
        t.ItemUsed();
        var s = t.Snapshot();
        Assert.Equal(CrucibleBonusReason.ItemUsed, Line(s, CrucibleBonus.EconomicEthos).Reason);
        Assert.Equal(12_250 - 750, s.IfKept);
    }

    [Fact]
    public void HabitualHoarderNeedsTenItemsHeld()
    {
        var t = new CrucibleBonusTracker(Board3, BugTeam);
        t.SetHeld(0, CrucibleBonusRules.HabitualHoarderMinItems - 1, 0);
        Assert.Equal(CrucibleBonusState.AtRisk, State(t, CrucibleBonus.HabitualHoarder));
        t.SetHeld(0, CrucibleBonusRules.HabitualHoarderMinItems, 0);
        Assert.Equal(CrucibleBonusState.DecidedAtEnd, State(t, CrucibleBonus.HabitualHoarder));
    }

    // --- Coin Clutcher / Rolling in Riches ---

    [Fact]
    public void SpendingTokensLosesCoinClutcher()
    {
        var t = Bugs();
        t.TokensSpent(0);
        Assert.Equal(CrucibleBonusState.Intact, State(t, CrucibleBonus.CoinClutcher));
        t.TokensSpent(120);
        Assert.Equal(CrucibleBonusReason.TokensSpent, Line(t.Snapshot(), CrucibleBonus.CoinClutcher).Reason);
    }

    [Fact]
    public void RollingInRichesNeedsTwoThousandTokensHeld()
    {
        var t = new CrucibleBonusTracker(Board3, BugTeam);
        t.SetHeld(0, 0, CrucibleBonusRules.RollingInRichesMinTokens - 1);
        Assert.Equal(CrucibleBonusState.AtRisk, State(t, CrucibleBonus.RollingInRiches));
        t.SetHeld(0, 0, CrucibleBonusRules.RollingInRichesMinTokens);
        t.Finished();
        Assert.Equal(CrucibleBonusState.Earned, State(t, CrucibleBonus.RollingInRiches));
    }

    // --- not tracked ---

    [Fact]
    public void InfinityAndTheDegreesAreNotTrackedAndNeverCounted()
    {
        var master = new CrucibleBoard(5, 0, 0, "Second Master's Board", 90, [1], Values(
            (CrucibleBonus.InfinityAndBeyondI, 500), (CrucibleBonus.FirstDegree, 1000), (CrucibleBonus.EconomicEthos, 750)));
        var t = new CrucibleBonusTracker(master, BugTeam);
        var s = t.Snapshot();
        Assert.Equal(CrucibleBonusState.NotTracked, Line(s, CrucibleBonus.InfinityAndBeyondI).State);
        Assert.Equal(CrucibleBonusReason.OutOfScope, Line(s, CrucibleBonus.FirstDegree).Reason);
        Assert.Equal(750, s.IfKept);
        t.Finished();
        Assert.Equal(750, t.Snapshot().Sure);
    }

    // --- exclusive groups ---

    [Fact]
    public void EveryBonusIsInAtMostOneExclusiveGroup()
    {
        var all = CrucibleBonusRules.ExclusiveGroups.SelectMany(g => g).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.Equal(7, CrucibleBonusRules.ExclusiveGroups.Count);
    }

    /// <summary>Nameday Attire intact and Walking Armory reachable: if-kept counts the better of the two, never both.</summary>
    [Fact]
    public void AnExclusiveGroupCountsItsMostValuableMemberOnly()
    {
        var t = new CrucibleBonusTracker(Board3, BugTeam);
        var s = t.Snapshot();
        Assert.Equal(CrucibleBonusState.Intact, Line(s, CrucibleBonus.NamedayAttire).State);
        Assert.Equal(CrucibleBonusState.AtRisk, Line(s, CrucibleBonus.WalkingArmory).State);
        // Walking Armory 1,000 (not + Nameday 750), Feed the Bold 1,000 (not + Starve 750), Hoarder, Riches, Ethos, Coin.
        Assert.Equal(1000 + 1000 + 500 + 500 + 750 + 500, s.IfKept);
    }

    /// <summary>Gear held without a GearAcquired event (an adapter slip) still pays one of the pair, not both.</summary>
    [Fact]
    public void SureNeverDoubleCountsAGroup()
    {
        var t = new CrucibleBonusTracker(Board3, BugTeam);
        t.SetHeld(CrucibleBonusRules.WalkingArmoryMinGear, 0, 0);
        t.Fed();
        t.Finished();
        var s = t.Snapshot();
        Assert.Equal(CrucibleBonusState.Earned, Line(s, CrucibleBonus.NamedayAttire).State);
        Assert.Equal(CrucibleBonusState.Earned, Line(s, CrucibleBonus.WalkingArmory).State);
        Assert.Equal(1000 + 1000 + 750 + 500, s.Sure); // Armory, Feed the Bold, Ethos, Coin
    }

    // --- performance and death ---

    [Fact]
    public void PerformanceCountsClearedSpacesAndTheLeastStillAhead()
    {
        var t = Bugs();
        t.SpaceCleared(CrucibleStageType.Enemy, 1);
        t.SpaceCleared(CrucibleStageType.Enemy, 2);
        var s = t.Snapshot();
        Assert.Equal(500, s.PerformanceSoFar);
        Assert.Equal(500 + 5000 + 250 + 800 + 1000, s.PerformanceIfFinished); // 4 enemy, 6 elite, 9 boss

        t.SpaceCleared(CrucibleStageType.Enemy, 2); // repeated: not scored twice
        Assert.Equal(500, t.Snapshot().PerformanceSoFar);

        var full = Bugs();
        WalkBoard1(full);
        Assert.Equal(2550, full.Snapshot().PerformanceSoFar);
        Assert.Equal(7550, full.Snapshot().PerformanceIfFinished);
        full.Finished();
        s = full.Snapshot();
        Assert.Equal(7550, s.PerformanceSoFar);
        Assert.Equal(7550, s.PerformanceIfFinished);
    }

    [Fact]
    public void ADeathScoresPerformanceSoFarAndNoBonus()
    {
        var t = Bugs();
        t.SpaceCleared(CrucibleStageType.Enemy, 1);
        t.SpaceCleared(CrucibleStageType.Enemy, 2);
        t.BeastmasterDied();
        t.Finished(); // ignored: the run is over
        t.SpaceCleared(CrucibleStageType.Enemy, 4);

        var s = t.Snapshot();
        Assert.True(s.Died);
        Assert.False(s.Finished);
        Assert.All(s.Lines, l => Assert.Equal(CrucibleBonusReason.BeastmasterDied, l.Reason));
        Assert.Equal(0, s.Sure);
        Assert.Equal(0, s.IfKept);
        Assert.Equal(500, s.PerformanceSoFar);
        Assert.Equal(500, s.ProjectedSure);
        Assert.Equal(500, s.ProjectedIfKept);
        Assert.Equal(17_000, s.GapIfKept);
    }

    [Theory]
    [InlineData(1, 17_500)]
    [InlineData(2, 17_500)]
    [InlineData(3, 17_875)]
    [InlineData(4, 18_750)]
    [InlineData(5, 18_500)]
    [InlineData(9, 0)]
    public void TheLegendaryLineIsTheBoardsStandardOne(int board, int line) => Assert.Equal(line, CrucibleBonusRules.LegendaryLine(board));

    [Fact]
    public void AnUnknownLegendaryLineHasNoGap()
    {
        var s = new CrucibleBonusTracker(Board1 with { Index = 9 }, BugTeam).Snapshot();
        Assert.Null(s.GapSure);
        Assert.Null(s.GapIfKept);
    }

    // --- real runs, 2026-10-27 ---

    /// <summary>
    /// Board 1, Bug team (three vilekin): no KO, no rest, no gear, no feed, no token spent, one Crucible item used. Real
    /// result: performance 7,550 + bonuses 9,000 = 16,550, 950 short of Legendary. The result paid no Unbeastable,
    /// Selfless or Self-preservation, so the run took a Damage Down / Vulnerability Up and ended below full HP (which
    /// status and how much HP is not recorded; these stand in).
    /// </summary>
    [Fact]
    public void ReplayBoard1BugTeamMissesLegendaryBy950()
    {
        var t = Bugs();
        t.SpaceCleared(CrucibleStageType.Enemy, 1);
        t.SpaceCleared(CrucibleStageType.Enemy, 2);
        t.SpaceCleared(CrucibleStageType.Treasure, 3);
        t.BeastmasterStatusGained("Vulnerability Up");
        t.SpaceCleared(CrucibleStageType.Enemy, 4);
        t.SpaceCleared(CrucibleStageType.Treasure, 5);
        Assert.Equal(17_500 - (7550 + 12_250 - 1250), t.Snapshot().GapIfKept); // still above the line: -1,050

        t.ItemUsed(); // the moment the window would show Economic Ethos lost
        var mid = t.Snapshot();
        Assert.Equal(CrucibleBonusState.Lost, Line(mid, CrucibleBonus.EconomicEthos).State);
        Assert.Equal(17_500 - (7550 + 12_250 - 1250 - 750), mid.GapIfKept); // -300: Selfless + Self-preservation now decide it

        t.SpaceCleared(CrucibleStageType.Elite, 6);
        t.SpaceCleared(CrucibleStageType.Treasure, 7);
        t.SpaceCleared(CrucibleStageType.Shop, 8);
        t.SpaceCleared(CrucibleStageType.Boss, 9);
        t.SetHeld(0, 2, 640);
        t.SetHp(62, [100, 71, 100]);
        t.Finished();

        var s = t.Snapshot();
        var earned = s.Lines.Where(l => l.State == CrucibleBonusState.Earned).ToDictionary(l => l.Bonus, l => l.Points);
        Assert.Equal(new Dictionary<CrucibleBonus, int>
        {
            [CrucibleBonus.NoBeastLeftBehind] = 1000,
            [CrucibleBonus.BeastSquadIII] = 4250,
            [CrucibleBonus.NamedayAttire] = 750,
            [CrucibleBonus.StarveTheFever] = 750,
            [CrucibleBonus.CoinClutcher] = 500,
            [CrucibleBonus.SleeplessSurvival] = 1000,
            [CrucibleBonus.VilekinGambit] = 750,
        }, earned);
        Assert.Equal(9000, s.Sure);
        Assert.Equal(9000, s.IfKept);
        Assert.Equal(7550, s.PerformanceSoFar);
        Assert.Equal(16_550, s.ProjectedSure);
        Assert.Equal(950, s.GapSure);
    }

    /// <summary>
    /// Board 1, beastkin team: one familiar KO'd and an item used. Willing Sacrifice I (250) is paid instead of No Beast
    /// Left Behind (1,000). Real result 16,197, 1,303 short. The other conditions are taken as in the Bug run; the tracker
    /// then pays 8,250, and the 397 between its 15,800 and the real score is the HP share it leaves out.
    /// </summary>
    [Fact]
    public void ReplayBoard1BeastkinTeamTradesNoBeastLeftBehindForWillingSacrificeI()
    {
        var t = new CrucibleBonusTracker(Board1, BeastkinTeam);
        t.FamiliarKnockedOut();
        var mid = t.Snapshot();
        Assert.Equal(CrucibleBonusState.Lost, Line(mid, CrucibleBonus.NoBeastLeftBehind).State);
        Assert.Equal(CrucibleBonusState.DecidedAtEnd, Line(mid, CrucibleBonus.WillingSacrificeI).State);

        t.ItemUsed();
        t.BeastmasterStatusGained("Damage Down");
        WalkBoard1(t);
        t.SetHp(40, [0, 100, 100]);
        t.Finished();

        var s = t.Snapshot();
        Assert.Equal(CrucibleBonusState.Earned, Line(s, CrucibleBonus.WillingSacrificeI).State);
        Assert.Equal(CrucibleBonusState.Earned, Line(s, CrucibleBonus.BeastkinGambit).State);
        Assert.Equal(9000 - 1000 + 250, s.Sure);
        Assert.Equal(7550 + 8250, s.ProjectedSure);
        const int realScore = 16_197;
        Assert.Equal(397, realScore - s.ProjectedSure);
        Assert.Equal(1303, 17_500 - realScore);
        Assert.Equal(17_500 - s.ProjectedSure, s.GapSure);
    }
}
