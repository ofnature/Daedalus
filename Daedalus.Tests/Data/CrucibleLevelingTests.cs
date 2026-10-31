using System.Collections.Generic;
using System.Linq;
using Daedalus.Data;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>The core team, the levelling roster and the team changes that load it.</summary>
public class CrucibleLevelingTests
{
    // Ranks as the Second Board's Team Composition showed them (2026-10-23): Damselfly 8, Goobbue 5, Buffalo 5, the
    // rest 1. Wespe is at the cap.
    private static readonly Dictionary<int, int> Ranks = new() { [44] = 8, [30] = 5, [26] = 5, [10] = 25 };
    private static int RankOf(int no) => Ranks.GetValueOrDefault(no, 1);
    private static readonly int[] Captured = [44, 30, 26, 10, 5, 7, 11, 16, 20, 40];

    [Fact]
    public void TheCoreDefaultsToTheHighestRanks()
        => Assert.Equal([10, 44, 30], CrucibleLevelPlan.Core([0, 0, 0], Captured, RankOf)); // Goobbue (caught Lv12) over Buffalo (Lv9) at 5

    /// <summary>
    /// Field 2026-10-25: with everyone read at 5 on board 1, the lowest Bestiary numbers (Opo-opo, Wespe, Mantis) took
    /// the core and Damselfly was left out. On equal ranks the later-caught (stronger) beast leads.
    /// </summary>
    [Fact]
    public void OnEqualRanksTheStrongerBaseLeads()
    {
        int[] captured = [5, 10, 16, 44];
        Assert.Equal(44, CrucibleLevelPlan.Core([0, 0, 0], captured, _ => 5)[0]);
    }

    [Fact]
    public void ThePlayersCoreChoicesComeFirst()
        => Assert.Equal([5, 7, 10], CrucibleLevelPlan.Core([5, 0, 7], Captured, RankOf));

    /// <summary>A core choice that is not captured is replaced, not kept as a hole.</summary>
    [Fact]
    public void AnUncapturedCoreChoiceIsReplaced()
    {
        var core = CrucibleLevelPlan.Core([99, 0, 0], Captured, RankOf);
        Assert.Equal(3, core.Count);
        Assert.DoesNotContain(99, core);
    }

    /// <summary>The core rides along, then the lowest ranks; a beast at 25 stays home.</summary>
    [Fact]
    public void TheLevelRosterIsTheCoreThenTheLowestRanks()
    {
        int[] core = [44, 30, 26];
        var roster = CrucibleLevelPlan.LevelRoster(core, Captured, RankOf, cap: 6);
        Assert.Equal(core, roster.Take(3));
        Assert.Equal([5, 7, 11], roster.Skip(3)); // all rank 1, lowest Bestiary number first
        Assert.DoesNotContain(10, roster);         // Wespe, rank 25
    }

    [Fact]
    public void TheLevelRosterStopsWhenNobodyIsLeftToLevel()
    {
        var roster = CrucibleLevelPlan.LevelRoster([44], [44, 10], RankOf, cap: 14);
        Assert.Equal([44], roster);
    }

    /// <summary>When levelling, the core carries every battle unless the player picked that battle's horns.</summary>
    [Fact]
    public void LevellingPutsTheCoreOnTheHorns()
    {
        int[] core = [44, 30, 26];
        Assert.Equal(core, CrucibleLevelPlan.HornChoice(CrucibleTeamGoal.Level, null, core));
        Assert.Equal(core, CrucibleLevelPlan.HornChoice(CrucibleTeamGoal.Level, [0, 0, 0], core));
        Assert.Equal([7, 0, 0], CrucibleLevelPlan.HornChoice(CrucibleTeamGoal.Level, [7, 0, 0], core));
        Assert.Null(CrucibleLevelPlan.HornChoice(CrucibleTeamGoal.Clear, null, core));
    }

    /// <summary>Leavers go first so the joiners have room under the cap.</summary>
    [Fact]
    public void ChangesRemoveBeforeTheyAdd()
    {
        var changes = CrucibleLevelPlan.Changes([44, 30, 10], [44, 5, 7]);
        Assert.Equal([(30, false), (10, false), (5, true), (7, true)], changes);
    }

    [Fact]
    public void NoChangesWhenTheTeamMatches() => Assert.Empty(CrucibleLevelPlan.Changes([44, 30], [30, 44]));

    // --- LevelRoster: edges ---

    /// <summary>A cap smaller than the core keeps the core's first picks and nothing else.</summary>
    [Fact]
    public void ALevelCapUnderTheCoreKeepsItsFirstPicks()
        => Assert.Equal([44, 30], CrucibleLevelPlan.LevelRoster([44, 30, 26], Captured, RankOf, cap: 2));

    [Fact]
    public void AZeroLevelCapIsAnEmptyRoster()
        => Assert.Empty(CrucibleLevelPlan.LevelRoster([44, 30, 26], Captured, RankOf, cap: 0));

    /// <summary>A core beast already at 25 still rides along; only the fill skips beasts at 25.</summary>
    [Fact]
    public void ACoreBeastAtTwentyFiveStillComes()
        => Assert.Equal(10, CrucibleLevelPlan.LevelRoster([10], Captured, RankOf, cap: 3)[0]);

    /// <summary>A core beast is not added a second time by the fill.</summary>
    [Fact]
    public void TheCoreIsNotAddedTwice()
    {
        var roster = CrucibleLevelPlan.LevelRoster([5, 7], Captured, RankOf, cap: 10);
        Assert.Equal(roster.Count, roster.Distinct().Count());
        Assert.Equal([5, 7], roster.Take(2));
    }

    /// <summary>Lowest rank first, before Bestiary order: a rank-1 beast goes ahead of a rank-5 one with a lower number.</summary>
    [Fact]
    public void TheFillIsLowestRankFirst()
        => Assert.Equal([16, 26], CrucibleLevelPlan.LevelRoster([], [26, 16], RankOf, cap: 5));

    /// <summary>With no core and nothing captured there is nothing to bring.</summary>
    [Fact]
    public void NothingCapturedAndNoCoreIsEmpty()
        => Assert.Empty(CrucibleLevelPlan.LevelRoster([], [], RankOf, cap: 10));

    /// <summary>Legendary: three of ONE kin, strongest kin first (Wespe 25, Damselfly 8, Mantis 1 are all vilekin), in
    /// the Bug team's horn order.</summary>
    [Fact]
    public void TheLegendaryTeamIsThreeOfOneKin()
        => Assert.Equal([44, 16, 10], CrucibleLevelPlan.LegendaryTeam([0, 0, 0], Captured, RankOf));

    /// <summary>The player's core picks lead when they are one kin: Goobbue picked → the beastkin team.</summary>
    [Fact]
    public void LegendaryKeepsCorePicksOfOneKin()
        => Assert.Equal([30, 26, 5], CrucibleLevelPlan.LegendaryTeam([30, 0, 0], Captured, RankOf));

    /// <summary>Core picks of two kins cannot make a Gambit: the strongest kin is used instead.</summary>
    [Fact]
    public void LegendaryIgnoresMixedKinPicks()
        => Assert.Equal([44, 16, 10], CrucibleLevelPlan.LegendaryTeam([30, 44, 0], Captured, RankOf));

    /// <summary>Korha 2026-10-31, Board 3 (sync 15): bugs and Beastkin all at 15 — the board's own team (Cu Sith, Coeurl, Behemoth) is fielded.</summary>
    [Fact]
    public void ThirdBoardFieldsItsBeastkinTeam()
    {
        int[] captured = [44, 16, 10, 1, 33, 50, 30];
        int Rank(int no) => no == 1 ? 19 : 15;
        Assert.Equal([1, 33, 50], CrucibleLevelPlan.LegendaryTeam([0, 0, 0], captured, Rank, rankCap: 15, boardIndex: 3));
        Assert.Equal([44, 16, 10], CrucibleLevelPlan.LegendaryTeam([0, 0, 0], captured, Rank, rankCap: 15, boardIndex: 1));
    }

    /// <summary>Not when one of the three is weaker at the sync than the team worked out otherwise.</summary>
    [Fact]
    public void ThirdBoardTeamYieldsWhenWeaker()
    {
        int[] captured = [44, 16, 10, 1, 33, 50];
        int Rank(int no) => no == 50 ? 12 : 15;
        Assert.Equal([44, 16, 10], CrucibleLevelPlan.LegendaryTeam([0, 0, 0], captured, Rank, rankCap: 15, boardIndex: 3));
    }

    /// <summary>Not when one of the three is not captured, and never over the player's own picks.</summary>
    [Fact]
    public void ThirdBoardTeamNeedsAllThreeAndNoPicks()
    {
        Assert.Equal([44, 16, 10], CrucibleLevelPlan.LegendaryTeam([0, 0, 0], [44, 16, 10, 1, 33], _ => 15, rankCap: 15, boardIndex: 3));
        Assert.Equal([44, 10, 16], CrucibleLevelPlan.LegendaryTeam([44, 0, 0], [44, 16, 10, 1, 33, 50], _ => 15, rankCap: 15, boardIndex: 3));
    }

    /// <summary>Its horns keep the board team's order: Cu Sith starts, Coeurl debuffs, Behemoth finishes.</summary>
    [Fact]
    public void ThirdBoardHornsKeepTheGuideOrder()
        => Assert.Equal([1, 33, 50], CrucibleLevelPlan.HornChoice(CrucibleTeamGoal.Legendary, null, [1, 33, 50], "siren piece"));

    /// <summary>Board 1 syncs everyone to 5, so every kin ties: the Bug team wins the tie (Korha 2026-10-27 got beastkin).</summary>
    [Fact]
    public void TheBugTeamWinsATieAtTheSync()
    {
        int[] captured = [44, 16, 10, 28, 30, 26, 1];
        Assert.Equal([44, 16, 10], CrucibleLevelPlan.LegendaryTeam([0, 0, 0], captured, _ => 14, rankCap: 5));
    }

    /// <summary>Inside vilekin, Wespe beats Worm at the same capped rank: the Bug team's members go first.</summary>
    [Fact]
    public void BugMembersLeadTheirKinAtEqualRank()
    {
        var ranks = new Dictionary<int, int> { [44] = 14, [16] = 14, [10] = 8, [28] = 9 };
        Assert.Equal([44, 16, 10], CrucibleLevelPlan.LegendaryTeam([0, 0, 0], [44, 16, 10, 28], no => ranks[no], rankCap: 5));
    }

    /// <summary>Per boss: poison first against a poisonable lead, Mantis first against a poison-immune one (Meoni).</summary>
    [Fact]
    public void BugTeamHornOrderFollowsPoisonImmunity()
    {
        int[] team = [44, 16, 10];
        Assert.Equal([44, 16, 10], CrucibleLevelPlan.HornChoice(CrucibleTeamGoal.Legendary, null, team, "arch demon piece"));
        Assert.Equal([16, 10, 44], CrucibleLevelPlan.HornChoice(CrucibleTeamGoal.Legendary, null, team, "Pas de Seul"));
        Assert.Equal([16, 10, 44], CrucibleLevelPlan.HornChoice(CrucibleTeamGoal.Legendary, null, team, "bone knight"));
        Assert.Equal([44, 16, 10], CrucibleLevelPlan.HornChoice(CrucibleTeamGoal.Legendary, null, team, null));
    }

    /// <summary>The player's own per-battle order is kept, and another team keeps its order.</summary>
    [Fact]
    public void HornOrderLeavesPlayerPicksAndOtherTeamsAlone()
    {
        Assert.Equal([10, 44, 16], CrucibleLevelPlan.HornChoice(CrucibleTeamGoal.Legendary, [10, 44, 16], [44, 16, 10], "Pas de Seul"));
        Assert.Equal([30, 26, 5], CrucibleLevelPlan.HornChoice(CrucibleTeamGoal.Legendary, null, [30, 26, 5], "Pas de Seul"));
    }

    /// <summary>The board's rank sync caps what a rank is worth: at a cap of 5 Wespe's 25 counts as 5.</summary>
    [Fact]
    public void LegendaryWeighsRanksAtTheBoardsSync()
    {
        var ranks = new Dictionary<int, int> { [10] = 25, [44] = 1, [16] = 1, [30] = 5, [26] = 5, [5] = 4 };
        Assert.Equal([26, 30, 5], CrucibleLevelPlan.LegendaryTeam([0, 0, 0], Captured, no => ranks.GetValueOrDefault(no, 1), rankCap: 5)); // ties by Bestiary number
    }

    /// <summary>Legendary horns come from the team; a per-battle pick outside it is ignored, one inside it is kept.</summary>
    [Fact]
    public void LegendaryHornsStayInsideTheTeam()
    {
        IReadOnlyList<int> team = [10, 44, 16];
        IReadOnlyList<int> poisonFirst = [44, 16, 10]; // the Bug team, in its poison-first order
        Assert.Equal(poisonFirst, CrucibleLevelPlan.HornChoice(CrucibleTeamGoal.Legendary, [30, 0, 0], team));
        Assert.Equal([16, 0, 0], CrucibleLevelPlan.HornChoice(CrucibleTeamGoal.Legendary, [16, 0, 0], team));
        Assert.Equal(poisonFirst, CrucibleLevelPlan.HornChoice(CrucibleTeamGoal.Legendary, null, team));
    }
}
