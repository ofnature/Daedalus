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
}
