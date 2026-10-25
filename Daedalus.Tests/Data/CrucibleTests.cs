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

    // --- the ascetic-run plan ---

    // Board 1's bonus row from the 2026.10.16 client (XBMContent row 1, columns 4-36).
    private static readonly int[] Board1Bonuses =
        [250, 500, 750, 1000, 1250, 2500, 4250, 250, 1000, 0, 0, 0, 1250, 750, 0, 750, 0, 750, 0, 500, 0, 1000,
         750, 750, 750, 750, 750, 750, 750, 750, 4000, 6250, 9000];

    private static readonly CrucibleBoard Board1WithBonuses = Board1 with { Bonuses = Board1Bonuses };

    [Fact]
    public void BonusValuesAreReadInScoreBonusOrder()
    {
        Assert.Equal(4250, Board1WithBonuses.BonusValue(CrucibleBonus.BeastSquadIII));
        Assert.Equal(0, Board1WithBonuses.BonusValue(CrucibleBonus.InfinityAndBeyondI)); // Second Master's Board only
        Assert.Equal(9000, Board1WithBonuses.BonusValue(CrucibleBonus.ThirdDegree));
    }

    /// <summary>
    /// Board 1, a vilekin team: Beast Squad III 4,250 + Nameday 750 + Economic Ethos 750 + Starve 750 + Coin Clutcher
    /// 500 + Sleepless 1,000 + Vile Kin Gambit 750 = 8,750 sure; No Beast Left Behind 1,000 + Unbeastable 1,250 +
    /// Self-preservation 250 + Selfless 1,000 = 3,500 more if the fights go cleanly.
    /// </summary>
    [Fact]
    public void TheAsceticRunEstimateOnBoard1()
        => Assert.Equal((8750, 3500), CrucibleRunPlan.BonusEstimate(Board1WithBonuses, BeastClassification.Vilekin));

    [Fact]
    public void AnUnreadBoardEstimatesNothing() => Assert.Equal((0, 0), CrucibleRunPlan.BonusEstimate(Board1, BeastClassification.Vilekin));

    private static System.Collections.Generic.IEnumerable<string> Names(CrucibleTeam? team) =>
        team?.Beasts.Select(b => b.Name) ?? [];

    /// <summary>
    /// Board 1's final boss (Pas de Seul) is piercing-weak: the community Bug team, Damselfly > Mantis > Wespe.
    /// Field, 2026-10-23: matching attack types alone recommended Antling > Diremite > Wespe, dropping Mantis's
    /// vulnerability and Damselfly's opener because all three of those deal piercing damage.
    /// </summary>
    [Fact]
    public void Board1RecommendsTheBugTeam()
    {
        var captured = new System.Collections.Generic.HashSet<int> { 8, 10, 16, 37, 44 }; // + Antling, Damselfly
        var team = CrucibleRunPlan.RecommendTeam(Index, Board1, captured.Contains);

        Assert.Equal(["Damselfly", "Mantis", "Wespe"], Names(team));
        Assert.Equal(BeastClassification.Vilekin, team!.Kin);
        Assert.True(team.HitsFinalBoss);
    }

    /// <summary>Without Damselfly the first horn falls to the next on its list — Diremite, keeping one kin.</summary>
    [Fact]
    public void WithoutDamselflyDiremiteOpens()
        => Assert.Equal(["Diremite", "Mantis", "Wespe"], Names(CrucibleRunPlan.RecommendTeam(Index, Board1, new[] { 8, 10, 16 }.Contains)));

    /// <summary>Dullahan is also on the first horn's list, but Diremite keeps the team one kin (the Gambit bonus).</summary>
    [Fact]
    public void OneKinIsPreferredForTheOpener()
        => Assert.Equal(["Diremite", "Mantis", "Wespe"], Names(CrucibleRunPlan.RecommendTeam(Index, Board1, new[] { 8, 10, 16, 18 }.Contains)));

    /// <summary>With only Dullahan for the first horn the team is mixed: no Gambit in the estimate.</summary>
    [Fact]
    public void AMixedCuratedTeamHasNoKin()
    {
        var team = CrucibleRunPlan.RecommendTeam(Index, Board1, new[] { 10, 16, 18 }.Contains);
        Assert.Equal(["Dullahan", "Mantis", "Wespe"], Names(team));
        Assert.Null(team!.Kin);
    }

    /// <summary>No Mantis: the Bug team cannot be made, so the pick falls back to matching attack types.</summary>
    [Fact]
    public void WithoutTheBugTeamTheWeaknessMatchIsUsed()
    {
        var team = CrucibleRunPlan.RecommendTeam(Index, Board1, new[] { 8, 10, 37 }.Contains); // Diremite, Wespe, Antling
        Assert.Equal("matched to weaknesses", team!.Source);
        Assert.Equal(3, team.Beasts.Count);
        Assert.Equal(CrucibleElement.Piercing, BstFamiliarAttacks.Of(team.Beasts[^1].BestiaryNo));
    }

    [Fact]
    public void NoKinWithThreeCapturedMeansNoTeam()
        => Assert.Null(CrucibleRunPlan.RecommendTeam(Index, Board1, no => no is 8 or 10)); // only two vilekin

    // --- Snarl / Challenge: the familiar tanks; the beastmaster's HP is the run (field, 2026-10-24) ---

    /// <summary>
    /// Field 2026-10-24: the beastmaster died at the Voidmancer twice, pulling every enemy off a full-HP familiar. A
    /// healthy familiar keeps the enemy.
    /// </summary>
    [Fact]
    public void NoChallengeOffAHealthyFamiliar()
        => Assert.False(ArtemisCooldownRules.ShouldChallenge(true, true, true, true, enemyOnFamiliar: true, familiarHpPct: 0.9f, selfHpPct: 1f));

    [Fact]
    public void ChallengeSavesAFamiliarAboutToFall()
        => Assert.True(ArtemisCooldownRules.ShouldChallenge(true, true, true, true, enemyOnFamiliar: true, familiarHpPct: 0.2f, selfHpPct: 0.9f));

    [Fact]
    public void NoChallengeWhileTheEnemyIsOnYou()
        => Assert.False(ArtemisCooldownRules.ShouldChallenge(true, true, true, true, enemyOnFamiliar: false, familiarHpPct: 0.2f, selfHpPct: 0.9f));

    /// <summary>Saving a familiar is never worth the run: not when you are under 70%.</summary>
    [Fact]
    public void NoChallengeWhenYouAreHurt()
        => Assert.False(ArtemisCooldownRules.ShouldChallenge(true, true, true, true, true, familiarHpPct: 0.2f, selfHpPct: 0.65f));

    [Fact]
    public void NoChallengeOffTheBoardOrWithoutAFamiliar()
    {
        Assert.False(ArtemisCooldownRules.ShouldChallenge(true, onBoardInBattle: false, true, true, true, 0.2f, 0.9f));
        Assert.False(ArtemisCooldownRules.ShouldChallenge(true, true, true, hasFamiliar: false, true, 0.2f, 0.9f));
    }

    /// <summary>
    /// Field 2026-10-25: held until you were under 50%, the first fight took you 100 → 55% with every familiar
    /// untouched. Your HP does not refill: Snarl goes out whenever ready.
    /// </summary>
    [Fact]
    public void SnarlGoesOutEvenAtFullHp()
        => Assert.True(ArtemisCooldownRules.ShouldSnarl(true, true, true, true, familiarHpPct: 1f));

    [Fact]
    public void NoSnarlOntoAFamiliarWithNothingLeft()
        => Assert.False(ArtemisCooldownRules.ShouldSnarl(true, true, true, true, familiarHpPct: 0.2f));

    [Fact]
    public void NoSnarlOffTheBoardOrWhileItRecasts()
    {
        Assert.False(ArtemisCooldownRules.ShouldSnarl(true, onBoardInBattle: false, true, true, 1f));
        Assert.False(ArtemisCooldownRules.ShouldSnarl(true, true, ready: false, true, 1f));
    }

    // --- a falling familiar is spent, not lost (field 2026-10-25) ---

    [Fact]
    public void AFamiliarUnder20PercentOnABoardIsFalling()
        => Assert.True(ArtemisCooldownRules.IsFamiliarFalling(true, onBoard: true, familiarHpPct: 0.15f));

    [Fact]
    public void AHealthyFamiliarIsNotFalling()
        => Assert.False(ArtemisCooldownRules.IsFamiliarFalling(true, onBoard: true, familiarHpPct: 0.5f));

    /// <summary>Off the board the general Parting Blow setting stands alone; a knocked-out familiar has nothing left to spend.</summary>
    [Fact]
    public void OffTheBoardOrAlreadyFallenIsNotFalling()
    {
        Assert.False(ArtemisCooldownRules.IsFamiliarFalling(true, onBoard: false, familiarHpPct: 0.1f));
        Assert.False(ArtemisCooldownRules.IsFamiliarFalling(true, onBoard: true, familiarHpPct: 0f));
        Assert.False(ArtemisCooldownRules.IsFamiliarFalling(false, onBoard: true, familiarHpPct: 0.1f));
    }

    /// <summary>Parting Blow is off by default — but a falling familiar loses nothing by retreating.</summary>
    [Fact]
    public void AFallingFamiliarAllowsPartingBlowEvenWhenItIsOff()
    {
        Assert.True(ArtemisCooldownRules.AllowsPartingBlow(enabled: false, familiarFalling: true));
        Assert.False(ArtemisCooldownRules.AllowsPartingBlow(enabled: false, familiarFalling: false));
        Assert.True(ArtemisCooldownRules.AllowsPartingBlow(enabled: true, familiarFalling: false));
    }

    [Fact]
    public void TheDutyActionsAreTheGamesChallengeAndSnarl()
    {
        Assert.Equal(46750u, BSTActions.Challenge.ActionId);
        Assert.Equal(46751u, BSTActions.Snarl.ActionId);
    }
}
