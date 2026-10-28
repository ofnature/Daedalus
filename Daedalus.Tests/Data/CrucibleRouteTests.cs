using System.Collections.Generic;
using System.Linq;
using Daedalus.Data;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>
/// The board route, built from the client's own XBMContentStageEvent rows (2026.10.16) and checked against the
/// published layouts (consolegameswiki, First and Second Board of the Unbroken).
/// </summary>
public class CrucibleRouteTests
{
    // Board 1: XBMContentStageEvent subrows 1.0-1.12 (move, type, index), XBMContentBattle 1 = [6, 1, 2, 3, 4, 5].
    private static readonly CrucibleStageRow[] Board1Rows =
    [
        new(0, 1, -1), new(1, 2, 1), new(2, 2, 2), new(2, 2, 3), new(3, 6, 0), new(3, 7, 0), new(4, 2, 4),
        new(5, 7, 1), new(6, 3, 5), new(7, 7, 2), new(7, 6, 1), new(8, 5, 0), new(9, 4, 0),
    ];

    private static readonly int[] Board1Battles = [6, 1, 2, 3, 4, 5];

    // Board 2: rows 2.0-2.13; XBMContentBattle 2 = [13, 7, 8, 9, 10, 11, 12]; its random space (subrow 0) is
    // XBMRandomStageEvent 1 = [(2, 6), (6, 1)] — an enemy (battle subrow 6 = 12) or a campsite.
    private static readonly CrucibleStageRow[] Board2Rows =
    [
        new(0, 1, -1), new(1, 2, 1), new(2, 5, 0), new(3, 3, 2), new(3, 2, 3), new(4, 7, 0), new(4, 6, 0),
        new(5, 2, 4), new(6, 7, 1), new(7, 3, 5), new(8, 8, 0), new(8, 7, 2), new(9, 5, 1), new(10, 4, 0),
    ];

    private static readonly int[] Board2Battles = [13, 7, 8, 9, 10, 11, 12];

    private static readonly IReadOnlyList<IReadOnlyList<(byte Type, int Index)>> Board2Random =
        [new List<(byte, int)> { (2, 6), (6, 1) }];

    private static IReadOnlyList<CrucibleStage> Board1() => CrucibleRouteBuilder.Build(Board1Rows, Board1Battles, []);

    private static IReadOnlyList<CrucibleStage> Board2() => CrucibleRouteBuilder.Build(Board2Rows, Board2Battles, Board2Random);

    private static string Describe(CrucibleStage s) => $"{s.Move}{s.SideLabel}:{s.Type}{(s.Battle != 0 ? "#" + s.Battle : "")}";

    /// <summary>Board 1, as the wiki lays it out: nine moves, forks at 2, 3 and 7, the boss last.</summary>
    [Fact]
    public void Board1MatchesThePublishedLayout()
        => Assert.Equal(
            [
                "1:Enemy#1", "2L:Enemy#2", "2R:Enemy#3", "3L:Campsite", "3R:Treasure", "4:Enemy#4", "5:Treasure",
                "6:Elite#5", "7L:Treasure", "7R:Campsite", "8:Shop", "9:Boss#6",
            ],
            Board1().Select(Describe));

    /// <summary>Board 2: the shop at 2, the elite or the wyvern at 3, and a random space at 8L.</summary>
    [Fact]
    public void Board2MatchesThePublishedLayout()
        => Assert.Equal(
            [
                "1:Enemy#7", "2:Shop", "3L:Elite#8", "3R:Enemy#9", "4L:Treasure", "4R:Campsite", "5:Enemy#10",
                "6:Treasure", "7:Elite#11", "8L:Random", "8R:Treasure", "9:Shop", "10:Boss#13",
            ],
            Board2().Select(Describe));

    /// <summary>Board 2's random space is "Random Enemy or Campsite" — the demon pieces (battle 12) or a camp.</summary>
    [Fact]
    public void ARandomSpaceKnowsWhatItCanBe()
    {
        var random = Board2().Single(s => s.Type == CrucibleStageType.Random);
        Assert.Equal([new CrucibleStageOption(CrucibleStageType.Enemy, 12), new CrucibleStageOption(CrucibleStageType.Campsite, 0)], random.Options);
    }

    // --- forks and teams ---

    private static CrucibleEnemy Enemy(uint nameId, string name, int battle, CrucibleElement weakness) =>
        new(nameId, name, battle, battle == 6, weakness, [1, 1, 1, 1, 1], []);

    private static readonly CrucibleBoard Board1Board = new(1, 1088, 1339, "First Board of the Unbroken", 30, [6, 1, 2, 3, 4, 5], null, Board1());

    private static readonly CrucibleIndex Index = new(
        [Board1Board],
        [
            Enemy(14531, "bone knight", 1, CrucibleElement.Blunt),
            Enemy(14532, "bone bishop", 1, CrucibleElement.Blunt),
            Enemy(14533, "arch demon piece", 2, CrucibleElement.Lightning),
            Enemy(14535, "piscodemon piece", 3, CrucibleElement.Slashing),
            Enemy(14538, "ogre piece", 5, CrucibleElement.Wind),
            Enemy(14539, "wisp piece", 5, CrucibleElement.Water),
            Enemy(14541, "Pas de Seul", 6, CrucibleElement.Piercing),
        ]);

    private static CrucibleStage[] Fork(int move) => Board1().Where(s => s.Move == move).ToArray();

    /// <summary>Campsite over treasure: HP never refills on a board otherwise.</summary>
    [Fact]
    public void ACampsiteIsSuggestedOverATreasure()
    {
        Assert.Equal(1, CrucibleRoutePlan.SuggestedSide(Index, Board1Board, Fork(3), _ => true)); // 3L campsite
        Assert.Equal(2, CrucibleRoutePlan.SuggestedSide(Index, Board1Board, Fork(7), _ => true)); // 7R campsite
    }

    /// <summary>Between two battles, the one your beasts cover: with a lightning beast and no slashing one, the arch demon.</summary>
    [Fact]
    public void BetweenTwoBattlesTheCoveredOneIsSuggested()
    {
        var onlyCoblyn = new HashSet<int> { 7 }; // Coblyn: lightning
        Assert.Equal(1, CrucibleRoutePlan.SuggestedSide(Index, Board1Board, Fork(2), onlyCoblyn.Contains)); // arch demon
        var onlyMantis = new HashSet<int> { 16 }; // Mantis: slashing
        Assert.Equal(2, CrucibleRoutePlan.SuggestedSide(Index, Board1Board, Fork(2), onlyMantis.Contains)); // piscodemon
    }

    [Fact]
    public void AMoveWithOneSpaceIsNotAFork() => Assert.Equal(0, CrucibleRoutePlan.SuggestedSide(Index, Board1Board, Fork(4), _ => true));

    /// <summary>Bone knight and bishop are blunt-weak: the captured blunt beasts.</summary>
    [Fact]
    public void ABattlesTeamHitsItsWeakness()
    {
        var captured = new HashSet<int> { 3, 5, 12, 26, 16 }; // Lamb, Opo-opo, Mandragora, Buffalo (blunt), Mantis
        var team = CrucibleRoutePlan.TeamFor(Index, Board1Board, 1, captured.Contains);
        Assert.Equal(3, team.Count);
        Assert.All(team, b => Assert.Equal(CrucibleElement.Blunt, BstFamiliarAttacks.Of(b.BestiaryNo)));
    }

    /// <summary>The ogre (wind) leads, so wind first; then the wisps' water.</summary>
    [Fact]
    public void TheLeadersWeaknessComesFirst()
    {
        var captured = new HashSet<int> { 4, 11 }; // Pugil (water), Vulture (wind)
        var team = CrucibleRoutePlan.TeamFor(Index, Board1Board, 5, captured.Contains);
        Assert.Equal(["Vulture", "Pugil"], team.Select(b => b.Name));
    }

    /// <summary>The final boss keeps the community team when it can be made.</summary>
    [Fact]
    public void TheFinalBossGetsTheBugTeam()
    {
        var captured = new HashSet<int> { 44, 16, 10 };
        Assert.Equal(["Damselfly", "Mantis", "Wespe"], CrucibleRoutePlan.TeamFor(Index, Board1Board, 6, captured.Contains).Select(b => b.Name));
    }

    /// <summary>Legendary never takes a campsite side (resting costs Sleepless Survival): the treasure instead.</summary>
    [Fact]
    public void LegendaryAvoidsCampsites()
    {
        Assert.Equal(2, CrucibleRoutePlan.SuggestedSide(Index, Board1Board, Fork(3), _ => true, legendary: true)); // 3R treasure
        Assert.Equal(1, CrucibleRoutePlan.SuggestedSide(Index, Board1Board, Fork(7), _ => true, legendary: true)); // 7L treasure
    }

    /// <summary>Board 1 move 3 is known to be the other way round (Korha 2026-10-27); a learned entry toggles it back.</summary>
    [Fact]
    public void Board1Move3IsAKnownFlippedFork()
    {
        Assert.True(CrucibleWalkPlan.IsForkFlipped(CrucibleWalkPlan.ForkKey(1, 3), null));
        Assert.False(CrucibleWalkPlan.IsForkFlipped(CrucibleWalkPlan.ForkKey(1, 3), new List<string> { "1:3" }));
        Assert.False(CrucibleWalkPlan.IsForkFlipped(CrucibleWalkPlan.ForkKey(1, 7), null));
        Assert.True(CrucibleWalkPlan.IsForkFlipped(CrucibleWalkPlan.ForkKey(2, 4), new List<string> { "2:4" }));
    }

    /// <summary>Aimed for the treasure and a campsite (its sibling) opened: the fork is the other way round. The
    /// treasure itself, or a screen that is neither, says nothing.</summary>
    [Fact]
    public void AFlippedForkIsSeenFromTheSpaceThatOpened()
    {
        var fork = Fork(3); // 3L campsite, 3R treasure
        var treasure = fork.First(s => s.Type == CrucibleStageType.Treasure);
        Assert.True(CrucibleWalkPlan.ForkIsFlipped(Board1(), treasure, CrucibleStageType.Campsite));
        Assert.False(CrucibleWalkPlan.ForkIsFlipped(Board1(), treasure, CrucibleStageType.Treasure));
        Assert.False(CrucibleWalkPlan.ForkIsFlipped(Board1(), treasure, CrucibleStageType.Shop));
    }

    /// <summary>The screen that opened names the space: coffer, shop, or the team screen without the battle list (camp).</summary>
    [Fact]
    public void TheOpenedScreenNamesTheSpace()
    {
        Assert.Equal(CrucibleStageType.Treasure, CrucibleWalkPlan.OpenedSpace(true, false, false, false));
        Assert.Equal(CrucibleStageType.Shop, CrucibleWalkPlan.OpenedSpace(false, true, false, false));
        Assert.Equal(CrucibleStageType.Campsite, CrucibleWalkPlan.OpenedSpace(false, false, true, false));
        Assert.Null(CrucibleWalkPlan.OpenedSpace(false, false, true, true)); // the horn screen before a battle
    }
}
