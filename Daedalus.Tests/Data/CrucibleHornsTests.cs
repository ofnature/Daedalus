using System.Linq;
using Daedalus.Data;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>Setting the Battlehorns before each Crucible battle, and the roster to bring.</summary>
public class CrucibleHornsTests
{
    private static CrucibleEnemy Enemy(uint nameId, string name, int battle, CrucibleElement weakness) =>
        new(nameId, name, battle, battle == 6, weakness, [1, 1, 1, 1, 1], []);

    private static readonly CrucibleBoard Board = new(
        1, 1088, 1339, "First Board of the Unbroken", 30, [6, 1, 2, 5], null,
        [
            new CrucibleStage(1, 0, CrucibleStageType.Enemy, 1, []),
            new CrucibleStage(2, 1, CrucibleStageType.Enemy, 2, []),
            new CrucibleStage(6, 0, CrucibleStageType.Elite, 5, []),
            new CrucibleStage(9, 0, CrucibleStageType.Boss, 6, []),
        ],
        RosterCap: 10);

    private static readonly CrucibleIndex Index = new(
        [Board],
        [
            Enemy(14531, "bone knight", 1, CrucibleElement.Blunt),
            Enemy(14533, "arch demon piece", 2, CrucibleElement.Lightning),
            Enemy(14538, "ogre piece", 5, CrucibleElement.Wind),
            Enemy(14539, "wisp piece", 5, CrucibleElement.Water),
            Enemy(14541, "Pas de Seul", 6, CrucibleElement.Piercing),
        ]);

    private static CrucibleTeamMember M(int petId, bool alive = true, int rank = 0, float health = 1f) => new(petId, alive, rank, health);

    // A team of five, in the order the horn screen lists it: Goobbue (blunt), Buffalo (blunt), Opo-opo (blunt),
    // Coblyn (lightning), Vulture (wind).
    private static readonly CrucibleTeamMember[] Team = [M(30), M(26), M(5), M(7), M(11)];

    /// <summary>The bone knight is blunt-weak: the three blunt members, by position.</summary>
    [Fact]
    public void TheBattlesWeaknessGetsTheHorns()
    {
        var picks = CrucibleHornPlan.Pick(Index, Board, 1, Team);
        Assert.Equal(3, picks.Count);
        Assert.All(picks, p => Assert.Equal(CrucibleElement.Blunt, BstFamiliarAttacks.Of(Team[p].PetId)));
    }

    /// <summary>The arch demon is lightning-weak: Coblyn first; the other two horns still get filled.</summary>
    [Fact]
    public void TheMatchGoesFirstAndTheHornsAreStillFilled()
    {
        var picks = CrucibleHornPlan.Pick(Index, Board, 2, Team);
        Assert.Equal(3, picks[0]); // Coblyn's position
        Assert.Equal(3, picks.Count);
    }

    /// <summary>A fallen familiar cannot go on a horn.</summary>
    [Fact]
    public void AFallenFamiliarIsSkipped()
    {
        CrucibleTeamMember[] team = [M(30), M(26, alive: false), M(5), M(7), M(11)];
        Assert.DoesNotContain(1, CrucibleHornPlan.Pick(Index, Board, 1, team));
    }

    [Fact]
    public void NobodyLeftMeansNoPicks()
        => Assert.Empty(CrucibleHornPlan.Pick(Index, Board, 1, [M(30, false), M(26, false)]));

    /// <summary>The final boss gets the Bug team when all three are on the team and standing, in horn order.</summary>
    [Fact]
    public void TheFinalBossGetsTheBugTeamInHornOrder()
    {
        CrucibleTeamMember[] team = [M(10), M(30), M(44), M(16)]; // Wespe, Goobbue, Damselfly, Mantis
        Assert.Equal([2, 3, 0], CrucibleHornPlan.Pick(Index, Board, 6, team)); // Damselfly > Mantis > Wespe
    }

    /// <summary>With Mantis fallen there is no Bug team: the boss's weakness decides instead (Wespe hits piercing).</summary>
    [Fact]
    public void WithoutTheWholeBugTeamTheWeaknessDecides()
    {
        CrucibleTeamMember[] team = [M(10), M(30), M(44), M(16, alive: false)];
        Assert.Equal(0, CrucibleHornPlan.Pick(Index, Board, 6, team)[0]);
    }

    // --- filling horns the weakness cannot ---

    /// <summary>
    /// Field 2026-10-24: one beast hit the weakness, and the other two horns went to the newest-caught (Antling, Drake,
    /// rank 1) while rank-8 Damselfly sat out. The match goes first; the rest by rank.
    /// </summary>
    [Fact]
    public void HornsTheWeaknessCannotFillGoToTheHighestRanks()
    {
        CrucibleTeamMember[] team = [M(37, rank: 1), M(35, rank: 1), M(44, rank: 8), M(7, rank: 1), M(30, rank: 5)];
        Assert.Equal([3, 2, 4], CrucibleHornPlan.Pick(Index, Board, 2, team)); // Coblyn (lightning), Damselfly 8, Goobbue 5
    }

    /// <summary>Between two of the same rank, the one with more HP left — HP does not refill between battles.</summary>
    [Fact]
    public void AmongEqualRanksTheHealthierGoesFirst()
    {
        CrucibleTeamMember[] team = [M(7), M(30, rank: 5, health: 0.2f), M(26, rank: 5, health: 0.9f), M(12, rank: 1)];
        Assert.Equal([0, 2, 1], CrucibleHornPlan.Pick(Index, Board, 2, team));
    }

    /// <summary>A matching beast still beats a stronger one that does not match.</summary>
    [Fact]
    public void TheWeaknessStillComesBeforeRank()
    {
        CrucibleTeamMember[] team = [M(44, rank: 25), M(7, rank: 1)];
        Assert.Equal(1, CrucibleHornPlan.Pick(Index, Board, 2, team)[0]);
    }

    // --- the first horn tanks ---

    private static readonly CrucibleBoard Board2Like = Board with { RankCap = 10 }; // recommended rank 6

    [Fact]
    public void TheRecommendedRankIsTheBottomOfTheBand()
    {
        Assert.Equal(6, Board2Like.RecommendedRank);
        Assert.Equal(1, (Board with { RankCap = 5 }).RecommendedRank);
        Assert.Equal(0, Board.RecommendedRank);
    }

    /// <summary>
    /// Field 2026-10-25: rank-4 Rotting Goobbue led the Voidmancer (board 2, rank 6+) and fell; rank-9 Damselfly sat
    /// behind it. An under-rank weakness match moves to horn two behind the strongest beast.
    /// </summary>
    [Fact]
    public void AnUnderRankMatchGivesTheFirstHornToTheStrongest()
    {
        CrucibleTeamMember[] team = [M(7, rank: 4), M(44, rank: 9), M(30, rank: 7)]; // Coblyn hits the arch demon's lightning
        Assert.Equal([1, 0, 2], CrucibleHornPlan.Pick(Index, Board2Like, 2, team));
    }

    /// <summary>A match one rank short of the recommendation still leads.</summary>
    [Fact]
    public void AMatchNearTheRecommendedRankKeepsTheFirstHorn()
    {
        CrucibleTeamMember[] team = [M(7, rank: 5), M(44, rank: 9), M(30, rank: 7)];
        Assert.Equal(0, CrucibleHornPlan.Pick(Index, Board2Like, 2, team)[0]);
    }

    /// <summary>Between equally under-rank beasts the weakness still decides.</summary>
    [Fact]
    public void NoStrongerBeastMeansTheMatchLeads()
    {
        CrucibleTeamMember[] team = [M(7, rank: 4), M(44, rank: 4), M(30, rank: 3)];
        Assert.Equal(0, CrucibleHornPlan.Pick(Index, Board2Like, 2, team)[0]);
    }

    /// <summary>A board syncs ranks to its cap: on board 1 a rank-9 and a rank-5 beast are equals.</summary>
    [Fact]
    public void RanksAreComparedAsTheBoardSyncsThem()
    {
        var board1Like = Board with { RankCap = 5 };
        Assert.Equal(5, CrucibleHornPlan.EffectiveRank(board1Like, M(44, rank: 9)));
        CrucibleTeamMember[] team = [M(12, rank: 5, health: 0.9f), M(44, rank: 9, health: 0.5f)];
        Assert.Equal(0, CrucibleHornPlan.Pick(Index, board1Like, 2, team)[0]); // equal at 5: the healthier leads
    }

    /// <summary>Unknown ranks change nothing.</summary>
    [Fact]
    public void UnknownRanksLeaveTheWeaknessFirst()
    {
        CrucibleTeamMember[] team = [M(7), M(44), M(30)];
        Assert.Equal(0, CrucibleHornPlan.Pick(Index, Board2Like, 2, team)[0]);
    }

    // --- the player's own picks ---

    /// <summary>The player's picks go on the horns first, in their order, ahead of the weakness match.</summary>
    [Fact]
    public void ThePlayersPicksGoFirst()
        => Assert.Equal([4, 3], CrucibleHornPlan.Pick(Index, Board, 1, Team, [11, 7]).Take(2));

    /// <summary>A horn left on Auto is filled with the recommendation, never left empty.</summary>
    [Fact]
    public void AutoHornsAreFilledWithTheRecommendation()
    {
        var picks = CrucibleHornPlan.Pick(Index, Board, 1, Team, [11, 0, 0]);
        Assert.Equal(3, picks.Count);
        Assert.Equal(4, picks[0]); // Vulture, chosen
        Assert.All(picks.Skip(1), p => Assert.Equal(CrucibleElement.Blunt, BstFamiliarAttacks.Of(Team[p].PetId)));
    }

    /// <summary>A pick that has fallen, or is not on this board's team, is passed over for the recommendation.</summary>
    [Fact]
    public void AFallenOrMissingPickIsPassedOver()
    {
        CrucibleTeamMember[] team = [M(30), M(26), M(5), M(7), M(11, alive: false)];
        var picks = CrucibleHornPlan.Pick(Index, Board, 1, team, [11, 44]);
        Assert.Equal(3, picks.Count);
        Assert.DoesNotContain(4, picks);
    }

    /// <summary>The same beast picked twice takes one horn.</summary>
    [Fact]
    public void ADuplicatePickTakesOneHorn()
        => Assert.Equal(3, CrucibleHornPlan.Pick(Index, Board, 1, Team, [7, 7, 7]).Distinct().Count());

    [Fact]
    public void NoPicksIsTheRecommendation()
        => Assert.Equal(CrucibleHornPlan.Recommend(Index, Board, 2, Team), CrucibleHornPlan.Pick(Index, Board, 2, Team, [0, 0, 0]));

    [Fact]
    public void PicksAreKeptPerBoardAndBattle()
        => Assert.NotEqual(CrucibleHornPlan.PickKey(Board, 1), CrucibleHornPlan.PickKey(Board with { Index = 2 }, 1));

    // --- who sets the horns ---

    /// <summary>AutoDuty runs the whole board and picks its own horns: two pickers would undo each other.</summary>
    [Theory]
    [InlineData(true, "", true)]
    [InlineData(true, "Odysseus", true)]
    [InlineData(true, "AutoDuty", false)]
    [InlineData(false, "", false)]
    public void HornsYieldToAutoDuty(bool enabled, string source, bool expected)
        => Assert.Equal(expected, CrucibleHornPlan.ShouldSetHorns(enabled, source));

    // --- clicks ---

    [Fact]
    public void NothingToDoWhenTheHornsAlreadyMatch() => Assert.Empty(CrucibleHornPlan.Toggles([0, 1, 2], [0, 1, 2]));

    /// <summary>Horn order is selection order: a wrong selection is taken off from the last, then picked in order.</summary>
    [Fact]
    public void AWrongSelectionIsClearedThenPickedInOrder()
        => Assert.Equal([0, 1, 2, 3, 4], CrucibleHornPlan.Toggles([1, 0], [2, 3, 4]));

    /// <summary>
    /// Field bug 2026-10-24: with the first horn already right ([6] of [6, 2, 3]) the setter clicked 6 again — taking it
    /// off — and so toggled Damselfly on and off twelve times. A correct start is kept and the rest added.
    /// </summary>
    [Fact]
    public void ACorrectFirstHornIsKept()
        => Assert.Equal([2, 3], CrucibleHornPlan.Toggles([6], [6, 2, 3]));

    /// <summary>Only what goes wrong after the matching start is taken off.</summary>
    [Fact]
    public void OnlyTheWrongTailIsTakenOff()
        => Assert.Equal([5, 2, 3], CrucibleHornPlan.Toggles([6, 5], [6, 2, 3]));

    /// <summary>Following the toggles click by click always ends on the wanted horns — never a loop.</summary>
    [Theory]
    [InlineData(new int[0], new[] { 6, 2, 3 })]
    [InlineData(new[] { 6 }, new[] { 6, 2, 3 })]
    [InlineData(new[] { 3, 2, 6 }, new[] { 6, 2, 3 })]
    [InlineData(new[] { 1, 4, 5 }, new[] { 6, 2, 3 })]
    [InlineData(new[] { 6, 2, 3 }, new[] { 6, 2 })]
    public void ClickingOneToggleAtATimeConverges(int[] start, int[] wanted)
    {
        var selected = start.ToList();
        for (var click = 0; click < 12; click++)
        {
            var toggles = CrucibleHornPlan.Toggles(selected, wanted);
            if (toggles.Count == 0)
                break;
            var position = toggles[0];
            if (!selected.Remove(position))
                selected.Add(position); // the game's toggle: off if on, else on at the end
        }

        Assert.Equal(wanted, selected);
    }

    [Fact]
    public void FromNothingItJustPicks() => Assert.Equal([3, 0, 1], CrucibleHornPlan.Toggles([], [3, 0, 1]));

    // --- roster ---

    /// <summary>The final boss's team leads the roster, then what the route's battles call for, up to the cap.</summary>
    [Fact]
    public void TheRosterLeadsWithTheFinalBossTeam()
    {
        var captured = new System.Collections.Generic.HashSet<int> { 44, 16, 10, 30, 26, 5, 7, 11, 4 };
        var roster = CrucibleHornPlan.Roster(Index, Board, captured.Contains);
        Assert.Equal(["Damselfly", "Mantis", "Wespe"], roster.Take(3).Select(b => b.Name));
        Assert.Contains(roster, b => b.Name == "Coblyn"); // the arch demon's lightning
        Assert.True(roster.Count <= Board.RosterCap);
    }

    /// <summary>
    /// Every battle's best match comes before any battle's second: with room for two after the final boss's team, the
    /// bone knight and the arch demon get one beast each instead of the bone knight getting both — and the ogre needs
    /// none, as the final team's Damselfly already hits its wind.
    /// </summary>
    [Fact]
    public void TheRosterCoversEveryBattleBeforeGoingDeep()
    {
        var small = Board with { RosterCap = 5 };
        var captured = new System.Collections.Generic.HashSet<int> { 44, 16, 10, 30, 26, 5, 7, 11, 4 };
        var extra = CrucibleHornPlan.Roster(Index, small, captured.Contains).Skip(3).ToList();
        Assert.Contains(extra, b => b.Name == "Coblyn"); // arch demon: lightning
        Assert.Single(extra, b => BstFamiliarAttacks.Of(b.BestiaryNo) == CrucibleElement.Blunt); // bone knight
        Assert.DoesNotContain(extra, b => b.Name == "Vulture");
    }

    /// <summary>Once every battle is covered, the room left goes to second beasts for them.</summary>
    [Fact]
    public void SpareRoomGoesToSecondBeasts()
    {
        var captured = new System.Collections.Generic.HashSet<int> { 44, 16, 10, 30, 26, 5, 7, 11, 4 };
        var roster = CrucibleHornPlan.Roster(Index, Board with { RosterCap = 7 }, captured.Contains);
        Assert.Equal(7, roster.Count);
        Assert.True(roster.Count(b => BstFamiliarAttacks.Of(b.BestiaryNo) == CrucibleElement.Blunt) >= 2);
    }

    [Fact]
    public void TheRosterRespectsTheCap()
    {
        var small = Board with { RosterCap = 4 };
        var captured = new System.Collections.Generic.HashSet<int> { 44, 16, 10, 30, 26, 5, 7, 11, 4 };
        Assert.Equal(4, CrucibleHornPlan.Roster(Index, small, captured.Contains).Count);
    }
}
