using System.Linq;
using Daedalus.Data;
using Daedalus.Rotation.ArtemisCore.Helpers;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>The Crucible of the Unbroken: the board/enemy index, the familiar advice, and Snarl / Challenge.</summary>
public class CrucibleTests
{
    // Board 1 as the 2026.10.16 client has it: First Board of the Unbroken, territory 1339, final battle 6.
    private static readonly CrucibleBoard Board1 = new(1, 1088, 1339, "First Board of the Unbroken", 30, [6, 1, 2]);
    private static readonly CrucibleBoard Board3 = new(3, 1090, 1341, "Third Board of the Unbroken", 50, [21, 14]);

    private static CrucibleEnemy Enemy(uint nameId, string name, int battle, CrucibleElement weakness, bool final = false) =>
        new(nameId, name, battle, final, weakness, [1, 1, 1, 1, 1], [new CrucibleMove(46866, "Tumulus", "Self", "Circle", "")]);

    private static readonly CrucibleIndex Index = new(
        [Board1, Board3],
        [
            Enemy(14531, "bone knight", 1, CrucibleElement.Blunt),
            Enemy(14532, "bone bishop", 1, CrucibleElement.Blunt),
            Enemy(14541, "Pas de Seul", 6, CrucibleElement.Piercing, final: true),
            Enemy(14532, "bone bishop", 14, CrucibleElement.Blunt), // the same name on board 3
        ]);

    [Fact]
    public void ABoardIsFoundByItsTerritory()
    {
        Assert.Same(Board1, Index.BoardForTerritory(1339));
        Assert.Null(Index.BoardForTerritory(129)); // Limsa: not the Crucible
    }

    [Fact]
    public void TheFinalBattleIsTheFirstInTheBoardsPool() => Assert.Equal(6, Board1.FinalBattle);

    /// <summary>A name that appears on two boards resolves to the board you are on.</summary>
    [Fact]
    public void ASharedNameResolvesToThisBoardsBattle()
    {
        Assert.Equal(1, Index.FindEnemy(Board1, 14532)!.Battle);
        Assert.Equal(14, Index.FindEnemy(Board3, 14532)!.Battle);
    }

    [Fact]
    public void ABattlesLineupIsEveryoneInIt()
        => Assert.Equal(["bone knight", "bone bishop"], Index.EnemiesInBattle(1).Select(e => e.Name));

    [Fact]
    public void AnUnknownNameIsNoEnemy() => Assert.Null(Index.FindEnemy(Board1, 999));

    /// <summary>Pas de Seul is piercing-weak: the piercing familiars, captured first.</summary>
    [Fact]
    public void PiercingFamiliarsAreSuggestedForAPiercingWeakness()
    {
        var beasts = CrucibleAdvice.FamiliarsFor(CrucibleElement.Piercing, no => no == 10); // only Wespe captured
        Assert.Equal("Wespe", beasts[0].Beast.Name);
        Assert.True(beasts[0].Captured);
        Assert.Contains(beasts, b => b.Beast.Name == "Diremite" && !b.Captured);
        Assert.All(beasts, b => Assert.Equal(CrucibleElement.Piercing, BstFamiliarAttacks.Of(b.Beast.BestiaryNo)));
    }

    [Fact]
    public void NoWeaknessMeansNoSuggestions() => Assert.Empty(CrucibleAdvice.FamiliarsFor(CrucibleElement.None, _ => true));

    /// <summary>Spot checks against the familiars' attack text in the game data.</summary>
    [Theory]
    [InlineData(16, CrucibleElement.Slashing)] // Mantis: "Delivers a slashing physical attack."
    [InlineData(10, CrucibleElement.Piercing)] // Wespe
    [InlineData(18, CrucibleElement.Slashing)] // Dullahan
    [InlineData(17, CrucibleElement.None)]     // Slime: unaspected
    [InlineData(47, CrucibleElement.Ice)]      // Ice Golem
    public void FamiliarAttacksMatchTheGameData(int bestiaryNo, CrucibleElement attack)
        => Assert.Equal(attack, BstFamiliarAttacks.Of(bestiaryNo));

    [Fact]
    public void EveryBestiaryBeastHasAnAttackEntry()
        => Assert.All(Enumerable.Range(1, 50), no => Assert.True(no is 17 or 19 || BstFamiliarAttacks.Of(no) != CrucibleElement.None));

    // --- Snarl / Challenge ---

    [Fact]
    public void SnarlKeepsAHealthyFamiliarTanking()
        => Assert.True(ArtemisCooldownRules.ShouldSnarl(true, true, true, hasFamiliar: true, familiarHpPct: 0.9f, secondsSinceChallenge: 999));

    [Fact]
    public void NoSnarlOntoALowFamiliar()
        => Assert.False(ArtemisCooldownRules.ShouldSnarl(true, true, true, true, familiarHpPct: 0.4f, secondsSinceChallenge: 999));

    /// <summary>Snarl straight after a Challenge would hand back the enemy just pulled off the familiar.</summary>
    [Fact]
    public void NoSnarlRightAfterAChallenge()
        => Assert.False(ArtemisCooldownRules.ShouldSnarl(true, true, true, true, 0.9f, secondsSinceChallenge: 10));

    [Fact]
    public void NoSnarlWithoutAFamiliarOrOffTheBoard()
    {
        Assert.False(ArtemisCooldownRules.ShouldSnarl(true, true, true, hasFamiliar: false, 0.9f, 999));
        Assert.False(ArtemisCooldownRules.ShouldSnarl(true, onBoardInBattle: false, true, true, 0.9f, 999));
    }

    [Fact]
    public void ChallengeTakesTheEnemyOffAFamiliarAboutToFall()
        => Assert.True(ArtemisCooldownRules.ShouldChallenge(true, true, true, true, familiarHpPct: 0.2f, enemyOnFamiliar: true, selfHpPct: 0.9f));

    [Fact]
    public void NoChallengeWhenTheEnemyIsNotOnTheFamiliar()
        => Assert.False(ArtemisCooldownRules.ShouldChallenge(true, true, true, true, 0.2f, enemyOnFamiliar: false, 0.9f));

    [Fact]
    public void NoChallengeWhenYouAreTooLowToTakeTheHits()
        => Assert.False(ArtemisCooldownRules.ShouldChallenge(true, true, true, true, 0.2f, true, selfHpPct: 0.3f));

    [Fact]
    public void NoChallengeWhileTheFamiliarIsHealthy()
        => Assert.False(ArtemisCooldownRules.ShouldChallenge(true, true, true, true, familiarHpPct: 0.6f, true, 0.9f));

    [Fact]
    public void TheDutyActionsAreTheGamesChallengeAndSnarl()
    {
        Assert.Equal(46750u, BSTActions.Challenge.ActionId);
        Assert.Equal(46751u, BSTActions.Snarl.ActionId);
    }
}
