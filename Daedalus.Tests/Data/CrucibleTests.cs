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

    /// <summary>
    /// Field 2026-10-25 (Minerva session, Saar board 2): a familiar on 20-25% was neither Snarled (floor 25%) nor
    /// retreated (falling under 20%), and a tankbuster locked on the beastmaster killed her. No gap now: Snarl down
    /// to the falling line.
    /// </summary>
    [Theory]
    [InlineData(0.22f, true)]
    [InlineData(0.20f, true)]
    [InlineData(0.19f, false)]
    public void SnarlReachesDownToTheFallingLine(float familiarHp, bool expected)
        => Assert.Equal(expected, ArtemisCooldownRules.ShouldSnarl(true, true, true, true, familiarHp));

    /// <summary>Every familiar HP is covered by one of the two: Snarled, or spent and retreated.</summary>
    [Theory]
    [InlineData(0.05f)]
    [InlineData(0.199f)]
    [InlineData(0.2f)]
    [InlineData(0.24f)]
    [InlineData(0.9f)]
    public void NoFamiliarHpIsNeitherSnarledNorSpent(float familiarHp)
        => Assert.True(ArtemisCooldownRules.ShouldSnarl(true, true, true, true, familiarHp)
                       || ArtemisCooldownRules.IsFamiliarFalling(true, onBoard: true, familiarHp));

    /// <summary>RotationSolver's rule: at 30% or under, the familiar takes it even if it falls.</summary>
    [Fact]
    public void ACriticalBeastmasterSnarlsAnyStandingFamiliar()
    {
        Assert.True(ArtemisCooldownRules.ShouldSnarl(true, true, true, true, familiarHpPct: 0.05f, selfHpPct: 0.25f));
        Assert.False(ArtemisCooldownRules.ShouldSnarl(true, true, true, true, familiarHpPct: 0f, selfHpPct: 0.25f));
    }

    /// <summary>
    /// Field 2026-10-25: the Elder's 100-tonze Slash (locked on the beastmaster) killed Saar uncovered. With the boss
    /// engine's warning, a tankbuster due within 10 s is Snarled onto any standing familiar — even a low one.
    /// </summary>
    [Fact]
    public void ATankbusterOnItsWaySnarlsAnyStandingFamiliar()
    {
        Assert.True(ArtemisCooldownRules.ShouldSnarl(true, true, true, true, familiarHpPct: 0.1f, selfHpPct: 0.9f, secondsUntilTankbuster: 8.7f));
        Assert.False(ArtemisCooldownRules.ShouldSnarl(true, true, true, true, familiarHpPct: 0.1f, selfHpPct: 0.9f, secondsUntilTankbuster: 30f));
        Assert.False(ArtemisCooldownRules.ShouldSnarl(true, true, true, true, familiarHpPct: 0f, selfHpPct: 0.9f, secondsUntilTankbuster: 2f));
    }

    /// <summary>A falling familiar is kept, not retreated, when a tankbuster is about to land — it takes the hit.</summary>
    [Fact]
    public void AFallingFamiliarIsKeptForATankbuster()
    {
        Assert.False(ArtemisCooldownRules.IsFamiliarFalling(true, onBoard: true, familiarHpPct: 0.1f, selfHpPct: 0.9f, secondsUntilTankbuster: 5f));
        Assert.True(ArtemisCooldownRules.IsFamiliarFalling(true, onBoard: true, familiarHpPct: 0.1f, selfHpPct: 0.9f, secondsUntilTankbuster: 40f));
    }

    /// <summary>
    /// Field 2026-10-25 (Saar vs Lakhamu Piece): Parting Blow's opt-in toggle (off) made the scheduler drop every retreat
    /// the module asked for. The retreat has its own behavior, gated on the falling-familiar setting alone.
    /// </summary>
    [Fact]
    public void TheFallingRetreatDoesNotNeedPartingBlowTurnedOn()
    {
        var cfg = new Daedalus.Configuration();
        cfg.Beastmaster.EnablePartingBlow = false;
        cfg.Beastmaster.CruciblePartingBlowWhenFalling = true;
        var retreat = Daedalus.Rotation.ArtemisCore.Abilities.ArtemisAbilities.PartingBlowRetreat;

        Assert.Equal(BSTActions.PartingBlow.ActionId, retreat.Action.ActionId);
        Assert.True(retreat.Toggle!(cfg));
        Assert.False(Daedalus.Rotation.ArtemisCore.Abilities.ArtemisAbilities.PartingBlow.Toggle!(cfg));
        cfg.Beastmaster.CruciblePartingBlowWhenFalling = false;
        Assert.False(retreat.Toggle!(cfg));
    }

    /// <summary>The falling line rises with the familiar's biggest recent hit and its loss rate, capped at half.</summary>
    [Fact]
    public void TheFallingLineRisesForAFamiliarBeingHitHard()
    {
        Assert.Equal(ArtemisCooldownRules.FamiliarFallingHpPct, ArtemisCooldownRules.FallingLine(0f, 0f));
        Assert.Equal(ArtemisCooldownRules.FamiliarFallingHpPct, ArtemisCooldownRules.FallingLine(0.05f, 0.02f));
        Assert.Equal(0.33f, ArtemisCooldownRules.FallingLine(0.22f, 0f), 3);      // one 22% hit: retreat under 33%
        Assert.Equal(0.24f, ArtemisCooldownRules.FallingLine(0.05f, 0.06f), 3);   // 6%/s: four seconds of HP
        Assert.Equal(ArtemisCooldownRules.FallingLineMax, ArtemisCooldownRules.FallingLine(0.6f, 0.5f));
    }

    /// <summary>A familiar at 28% after a 22% hit is falling; with only small hits it tanks on.</summary>
    [Fact]
    public void ARaisedLineRetreatsBeforeTheNextBigHit()
    {
        var line = ArtemisCooldownRules.FallingLine(0.22f, 0f);
        Assert.True(ArtemisCooldownRules.IsFamiliarFalling(true, true, 0.28f, 0.9f, float.MaxValue, line));
        Assert.False(ArtemisCooldownRules.IsFamiliarFalling(true, true, 0.28f, 0.9f));
        Assert.False(ArtemisCooldownRules.ShouldSnarl(true, true, true, true, 0.28f, 0.9f, float.MaxValue, line));
        Assert.True(ArtemisCooldownRules.ShouldSnarl(true, true, true, true, 0.28f, 0.9f));
        Assert.NotNull(ArtemisCooldownRules.SnarlHeldReason(true, true, 0.28f, 0.9f, line));
    }

    /// <summary>The trend keeps the biggest drop for 10 s, measures loss over 3 s, and starts over for a new familiar.</summary>
    [Fact]
    public void TheHpTrendTracksHitsAndRateAndResetsOnANewFamiliar()
    {
        var t0 = new System.DateTime(2026, 10, 25, 19, 14, 40, System.DateTimeKind.Utc);
        var trend = new FamiliarHpTrend();
        trend.Sample(1, 0.40f, t0);
        trend.Sample(1, 0.18f, t0.AddSeconds(1));   // a 22% hit
        trend.Sample(1, 0.16f, t0.AddSeconds(2));
        Assert.Equal(0.22f, trend.BiggestHitPct, 3);
        Assert.Equal(0.08f, trend.LossPerSecond, 3); // 24% over the 3 s window

        trend.Sample(1, 0.16f, t0.AddSeconds(13));   // both drops are now over 10 s old
        Assert.Equal(0f, trend.BiggestHitPct);

        trend.Sample(2, 1f, t0.AddSeconds(14));      // the next horn
        Assert.Equal(0f, trend.BiggestHitPct);
        Assert.Equal(0f, trend.LossPerSecond);
    }

    /// <summary>A raidwide Cover can take is Snarled onto any standing familiar, like a tankbuster.</summary>
    [Fact]
    public void ACoverableRaidwideSnarlsAnyStandingFamiliar()
    {
        Assert.True(ArtemisCooldownRules.ShouldSnarl(true, true, true, true, 0.1f, 0.9f, secondsUntilRaidwide: 6f));
        Assert.False(ArtemisCooldownRules.ShouldSnarl(true, true, true, true, 0.1f, 0.9f, secondsUntilRaidwide: 25f));
        Assert.False(ArtemisCooldownRules.ShouldSnarl(true, true, true, true, 0f, 0.9f, secondsUntilRaidwide: 2f));
    }

    /// <summary>A falling familiar is kept, not retreated, for a raidwide on its way (its Cover takes your share).</summary>
    [Fact]
    public void AFallingFamiliarIsKeptForARaidwide()
    {
        Assert.False(ArtemisCooldownRules.IsFamiliarFalling(true, true, 0.1f, 0.9f, secondsUntilRaidwide: 4f));
        Assert.True(ArtemisCooldownRules.IsFamiliarFalling(true, true, 0.1f, 0.9f, secondsUntilRaidwide: 30f));
    }

    /// <summary>
    /// Durga's Atomic Ray (49272): Cover does not take it (Icy Veins), so it is not a reason to Snarl. A raidwide the
    /// engine does not name (id 0) is read as coverable.
    /// </summary>
    [Fact]
    public void AnUncoverableRaidwideIsNotAReasonToSnarl()
    {
        Assert.Equal(float.MaxValue, ArtemisCooldownRules.CoverableRaidwideSeconds(5f, 49272));
        Assert.Equal(5f, ArtemisCooldownRules.CoverableRaidwideSeconds(5f, 48669));
        Assert.Equal(5f, ArtemisCooldownRules.CoverableRaidwideSeconds(5f, 0));
        Assert.False(ArtemisCooldownRules.ShouldSnarl(true, true, true, true, 0.1f, 0.9f,
            secondsUntilRaidwide: ArtemisCooldownRules.CoverableRaidwideSeconds(5f, 49272)));
    }

    /// <summary>Soul Crush goes to the nearest flagged caster in melee that is still casting.</summary>
    [Fact]
    public void SoulCrushPicksTheNearestCasterInMelee()
    {
        Assert.Equal(2UL, ArtemisCooldownRules.PickInterruptTarget([(1UL, 2.5f, true), (2UL, 1.0f, true)]));
        Assert.Equal(1UL, ArtemisCooldownRules.PickInterruptTarget([(1UL, 2.5f, true), (2UL, 1.0f, false)]));
    }

    /// <summary>Nothing to interrupt out of melee (Soul Crush is a 3y action) or once the cast is over.</summary>
    [Fact]
    public void SoulCrushHasNoTargetOutOfMeleeOrWhenNobodyCasts()
    {
        Assert.Equal(0UL, ArtemisCooldownRules.PickInterruptTarget([(1UL, 6f, true)]));
        Assert.Equal(0UL, ArtemisCooldownRules.PickInterruptTarget([(1UL, 1f, false)]));
        Assert.Equal(0UL, ArtemisCooldownRules.PickInterruptTarget([]));
        Assert.Equal(7UL, ArtemisCooldownRules.PickInterruptTarget([(7UL, ArtemisCooldownRules.MeleeYalms, true)]));
    }

    /// <summary>A critical beastmaster keeps the falling familiar tanking — its Cover is all that is left.</summary>
    [Fact]
    public void ACriticalBeastmasterDoesNotRetreatTheFamiliar()
        => Assert.False(ArtemisCooldownRules.IsFamiliarFalling(true, onBoard: true, familiarHpPct: 0.1f, selfHpPct: 0.2f));

    [Fact]
    public void TheHeldReasonNamesTheHp()
    {
        Assert.Null(ArtemisCooldownRules.SnarlHeldReason(true, true, 0.5f, 0.9f));
        Assert.Contains("familiar at", ArtemisCooldownRules.SnarlHeldReason(true, true, 0.1f, 0.9f));
        Assert.Equal("no familiar out", ArtemisCooldownRules.SnarlHeldReason(true, false, 0f, 0.9f));
    }

    [Fact]
    public void NoSnarlOntoAFamiliarWithNothingLeft()
        => Assert.False(ArtemisCooldownRules.ShouldSnarl(true, true, true, true, familiarHpPct: 0.15f));

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

    // --- Snarl / falling: boundaries, and the two rules never disagreeing ---

    /// <summary>Turned off, or no familiar out: no Snarl, whatever is on its way.</summary>
    [Fact]
    public void NoSnarlWhenOffOrWithoutAFamiliarEvenForATankbuster()
    {
        Assert.False(ArtemisCooldownRules.ShouldSnarl(false, true, true, true, 0.1f, 0.9f, secondsUntilTankbuster: 2f));
        Assert.False(ArtemisCooldownRules.ShouldSnarl(true, true, true, hasFamiliar: false, 1f, 0.2f, secondsUntilTankbuster: 2f));
    }

    /// <summary>The leads are inclusive: a tankbuster or raidwide exactly 10 s out already Snarls a low familiar.</summary>
    [Fact]
    public void TheLeadsAreInclusive()
    {
        Assert.True(ArtemisCooldownRules.ShouldSnarl(true, true, true, true, 0.1f, 0.9f,
            secondsUntilTankbuster: ArtemisCooldownRules.TankbusterLeadSeconds));
        Assert.True(ArtemisCooldownRules.ShouldSnarl(true, true, true, true, 0.1f, 0.9f,
            secondsUntilRaidwide: ArtemisCooldownRules.RaidwideLeadSeconds));
        Assert.False(ArtemisCooldownRules.IsFamiliarFalling(true, true, 0.1f, 0.9f,
            secondsUntilTankbuster: ArtemisCooldownRules.TankbusterLeadSeconds));
        Assert.False(ArtemisCooldownRules.IsFamiliarFalling(true, true, 0.1f, 0.9f,
            secondsUntilRaidwide: ArtemisCooldownRules.RaidwideLeadSeconds));
    }

    /// <summary>Exactly 30% is critical for both rules: Snarl a low familiar, and do not retreat it.</summary>
    [Fact]
    public void ExactlyThirtyPercentIsCritical()
    {
        Assert.True(ArtemisCooldownRules.ShouldSnarl(true, true, true, true, 0.1f, selfHpPct: ArtemisCooldownRules.SelfCriticalHpPct));
        Assert.False(ArtemisCooldownRules.IsFamiliarFalling(true, true, 0.1f, selfHpPct: ArtemisCooldownRules.SelfCriticalHpPct));
    }

    /// <summary>A familiar exactly on a raised line is Snarled, not retreated; just under it is the other way round.</summary>
    [Fact]
    public void ARaisedLineIsInclusiveForSnarl()
    {
        Assert.True(ArtemisCooldownRules.ShouldSnarl(true, true, true, true, 0.5f, 0.9f, fallingLine: 0.5f));
        Assert.False(ArtemisCooldownRules.IsFamiliarFalling(true, true, 0.5f, 0.9f, fallingLine: 0.5f));
        Assert.False(ArtemisCooldownRules.ShouldSnarl(true, true, true, true, 0.4f, 0.9f, fallingLine: 0.5f));
        Assert.True(ArtemisCooldownRules.IsFamiliarFalling(true, true, 0.4f, 0.9f, fallingLine: 0.5f));
    }

    /// <summary>
    /// Under the falling line a standing familiar is either Snarled (an emergency: you critical, a tankbuster or
    /// raidwide due) or spent and retreated — never both, never neither — across every edge of the three conditions.
    /// </summary>
    [Fact]
    public void BelowTheLineExactlyOneOfSnarlAndRetreatApplies()
    {
        float[] familiar = [0.01f, 0.1f, 0.19f];
        float[] self = [0.1f, 0.3f, 0.31f, 1f];
        float[] seconds = [0f, 10f, 10.01f, float.MaxValue];
        foreach (var f in familiar)
        foreach (var me in self)
        foreach (var tb in seconds)
        foreach (var rw in seconds)
        {
            var snarl = ArtemisCooldownRules.ShouldSnarl(true, true, true, true, f, me, tb, secondsUntilRaidwide: rw);
            var falling = ArtemisCooldownRules.IsFamiliarFalling(true, true, f, me, tb, secondsUntilRaidwide: rw);
            Assert.True(snarl != falling, $"familiar {f}, you {me}, tankbuster {tb}, raidwide {rw}: snarl={snarl} falling={falling}");
        }
    }

    /// <summary>
    /// SUSPECTED BUG: SnarlHeldReason says Snarl is held ("familiar at 10% (under 20%)") while ShouldSnarl sends it for
    /// a tankbuster or raidwide on its way — the reason has no tankbuster/raidwide inputs, so the debug log contradicts
    /// what the rotation does. Debug text only; the rotation itself is right.
    /// </summary>
    [Fact]
    public void TheHeldReasonAgreesWithSnarlForATankbuster()
    {
        Assert.True(ArtemisCooldownRules.ShouldSnarl(true, true, true, true, 0.1f, 0.9f, secondsUntilTankbuster: 5f));
        Assert.Null(ArtemisCooldownRules.SnarlHeldReason(true, true, 0.1f, 0.9f, secondsUntilTankbuster: 5f));
        Assert.Null(ArtemisCooldownRules.SnarlHeldReason(true, true, 0.1f, 0.9f, secondsUntilRaidwide: 5f));
        Assert.NotNull(ArtemisCooldownRules.SnarlHeldReason(true, true, 0.1f, 0.9f, secondsUntilTankbuster: 30f));
    }

    /// <summary>Nonsense inputs (negative hit or rate) leave the flat line, never lower it.</summary>
    [Fact]
    public void TheFallingLineNeverDropsBelowTheFlatLine()
        => Assert.Equal(ArtemisCooldownRules.FamiliarFallingHpPct, ArtemisCooldownRules.FallingLine(-0.3f, -1f));

    /// <summary>The bigger of the two raises wins: a 20% hit asks for 30%, 10%/s for 40%.</summary>
    [Fact]
    public void TheFallingLineTakesTheBiggerRaise()
        => Assert.Equal(0.4f, ArtemisCooldownRules.FallingLine(0.2f, 0.1f), 3);

    /// <summary>No raidwide known (float.MaxValue) stays unknown whatever the id.</summary>
    [Fact]
    public void NoRaidwideStaysNoRaidwide()
    {
        Assert.Equal(float.MaxValue, ArtemisCooldownRules.CoverableRaidwideSeconds(float.MaxValue, 0));
        Assert.Equal(float.MaxValue, ArtemisCooldownRules.CoverableRaidwideSeconds(float.MaxValue, 49272));
        Assert.Contains(49272u, ArtemisCooldownRules.UncoverableRaidwides);
    }

    /// <summary>Two casters at the same distance: the first listed is interrupted.</summary>
    [Fact]
    public void SoulCrushTiesGoToTheFirstListed()
        => Assert.Equal(3UL, ArtemisCooldownRules.PickInterruptTarget([(3UL, 1.5f, true), (4UL, 1.5f, true)]));

    /// <summary>Edge distance 0 (standing in its hitbox) is melee.</summary>
    [Fact]
    public void SoulCrushReachesACasterAtZeroDistance()
        => Assert.Equal(5UL, ArtemisCooldownRules.PickInterruptTarget([(5UL, 0f, true)]));

    // --- the HP trend: edges ---

    private static readonly System.DateTime T0 = new(2026, 10, 25, 19, 14, 40, System.DateTimeKind.Utc);

    [Fact]
    public void OneSampleIsNoTrend()
    {
        var trend = new FamiliarHpTrend();
        trend.Sample(1, 0.5f, T0);
        Assert.Equal(0f, trend.BiggestHitPct);
        Assert.Equal(0f, trend.LossPerSecond);
    }

    /// <summary>HP going up is not a hit, and a window that ends higher than it began has no loss.</summary>
    [Fact]
    public void HealingIsNeitherAHitNorALoss()
    {
        var trend = new FamiliarHpTrend();
        trend.Sample(1, 0.5f, T0);
        trend.Sample(1, 0.9f, T0.AddSeconds(1));
        Assert.Equal(0f, trend.BiggestHitPct);
        Assert.Equal(0f, trend.LossPerSecond);
    }

    /// <summary>The biggest drop counts, not the latest.</summary>
    [Fact]
    public void TheBiggestHitIsTheLargestNotTheLatest()
    {
        var trend = new FamiliarHpTrend();
        trend.Sample(1, 0.9f, T0);
        trend.Sample(1, 0.8f, T0.AddSeconds(1));
        trend.Sample(1, 0.5f, T0.AddSeconds(2));
        trend.Sample(1, 0.45f, T0.AddSeconds(3));
        Assert.Equal(0.3f, trend.BiggestHitPct, 3);
    }

    /// <summary>Half a second in, a 30% drop reads as 10%/s (the whole 3 s window), not 60%/s.</summary>
    [Fact]
    public void AnUnfilledWindowStillDividesByThreeSeconds()
    {
        var trend = new FamiliarHpTrend();
        trend.Sample(1, 1f, T0);
        trend.Sample(1, 0.7f, T0.AddSeconds(0.5));
        Assert.Equal(0.1f, trend.LossPerSecond, 3);
    }

    /// <summary>A sample exactly 3 s old still anchors the rate; one older than that has left the window.</summary>
    [Fact]
    public void TheRateLooksBackExactlyThreeSeconds()
    {
        var kept = new FamiliarHpTrend();
        kept.Sample(1, 0.5f, T0);
        kept.Sample(1, 0.2f, T0.AddSeconds(FamiliarHpTrend.RateWindowSeconds));
        Assert.Equal(0.1f, kept.LossPerSecond, 3);

        var dropped = new FamiliarHpTrend();
        dropped.Sample(1, 0.5f, T0);
        dropped.Sample(1, 0.2f, T0.AddSeconds(FamiliarHpTrend.RateWindowSeconds + 0.5));
        Assert.Equal(0f, dropped.LossPerSecond);
    }

    /// <summary>A hit is remembered for exactly 10 s, and forgotten after.</summary>
    [Fact]
    public void AHitIsRememberedForExactlyTenSeconds()
    {
        var trend = new FamiliarHpTrend();
        trend.Sample(1, 0.5f, T0);
        trend.Sample(1, 0.3f, T0.AddSeconds(1));
        trend.Sample(1, 0.3f, T0.AddSeconds(1 + FamiliarHpTrend.HitMemorySeconds));
        Assert.Equal(0.2f, trend.BiggestHitPct, 3);
        trend.Sample(1, 0.3f, T0.AddSeconds(1.5 + FamiliarHpTrend.HitMemorySeconds));
        Assert.Equal(0f, trend.BiggestHitPct);
    }

    /// <summary>Going back to an earlier familiar starts over too: nothing carries across a swap.</summary>
    [Fact]
    public void SwappingBackStartsOver()
    {
        var trend = new FamiliarHpTrend();
        trend.Sample(1, 0.9f, T0);
        trend.Sample(1, 0.5f, T0.AddSeconds(1));
        trend.Sample(2, 1f, T0.AddSeconds(2));
        trend.Sample(1, 0.5f, T0.AddSeconds(3));
        Assert.Equal(0f, trend.BiggestHitPct);
        Assert.Equal(0f, trend.LossPerSecond);
    }

    /// <summary>Legendary, beastmaster tanking: Challenge whenever the enemy is not on you (the pull too), not while critical.</summary>
    [Fact]
    public void TankingChallengesAnyEnemyNotOnYou()
    {
        Assert.True(ArtemisCooldownRules.ShouldChallengeToTank(true, true, true, enemyOnBeastmaster: false, selfHpPct: 0.9f));
        Assert.False(ArtemisCooldownRules.ShouldChallengeToTank(true, true, true, enemyOnBeastmaster: true, selfHpPct: 0.9f));
        Assert.False(ArtemisCooldownRules.ShouldChallengeToTank(true, true, true, enemyOnBeastmaster: false, selfHpPct: 0.25f));
        Assert.False(ArtemisCooldownRules.ShouldChallengeToTank(true, true, ready: false, enemyOnBeastmaster: false, selfHpPct: 0.9f));
    }

    /// <summary>Legendary, beastmaster tanking: Snarl only rescues a critical beastmaster, never on cooldown.</summary>
    [Fact]
    public void TankingSnarlsOnlyWhenYouAreCritical()
    {
        Assert.False(ArtemisCooldownRules.ShouldSnarlWhileTanking(true, true, true, true, familiarHpPct: 1f, selfHpPct: 0.9f));
        Assert.True(ArtemisCooldownRules.ShouldSnarlWhileTanking(true, true, true, true, familiarHpPct: 1f, selfHpPct: 0.3f));
        Assert.False(ArtemisCooldownRules.ShouldSnarlWhileTanking(true, true, true, true, familiarHpPct: 0f, selfHpPct: 0.2f));
    }

    /// <summary>A falling familiar stays out when no other horn could replace it (Korha's Board 1 boss death).</summary>
    [Fact]
    public void AFallingFamiliarStaysWhenNoHornCanReplaceIt()
    {
        Assert.True(ArtemisCooldownRules.IsFamiliarFalling(true, true, 0.1f, 0.6f));
        Assert.False(ArtemisCooldownRules.IsFamiliarFalling(true, true, 0.1f, 0.6f, replacementReady: false));
    }

    /// <summary>Shared tanking: the familiar holds; the beastmaster (50%+) takes over only from a familiar about to fall.</summary>
    [Fact]
    public void SharedTankingGivesTheEnemyToWhoeverHasMoreHp()
    {
        Assert.True(ArtemisCooldownRules.BeastmasterHoldsShared(0.58f, familiarOut: true, familiarHpPct: 0.19f)); // Korha vs buffalo
        Assert.False(ArtemisCooldownRules.BeastmasterHoldsShared(0.90f, true, 0.55f));  // a healthy familiar keeps it (Cover = no debuffs on you)
        Assert.False(ArtemisCooldownRules.BeastmasterHoldsShared(0.90f, true, 0.30f));  // at the take-over line it still holds
        Assert.False(ArtemisCooldownRules.BeastmasterHoldsShared(0.45f, true, 0.10f));  // under the 50% floor
        Assert.True(ArtemisCooldownRules.BeastmasterHoldsShared(0.45f, familiarOut: false, familiarHpPct: 0f)); // nobody else
        Assert.False(ArtemisCooldownRules.BeastmasterHoldsShared(0.25f, false, 0f));     // critical: never
    }

    /// <summary>While the beastmaster holds, Snarl only hands a tankbuster or raidwide to a familiar above the line.</summary>
    [Fact]
    public void SharedTankingSnarlsOnlyForBustersAndRaidwides()
    {
        var line = ArtemisCooldownRules.FamiliarFallingHpPct;
        Assert.False(ArtemisCooldownRules.ShouldSnarlWhileHoldingShared(true, true, true, 0.8f, line, float.MaxValue, float.MaxValue));
        Assert.True(ArtemisCooldownRules.ShouldSnarlWhileHoldingShared(true, true, true, 0.8f, line, 5f, float.MaxValue));
        Assert.True(ArtemisCooldownRules.ShouldSnarlWhileHoldingShared(true, true, true, 0.8f, line, float.MaxValue, 5f));
        Assert.False(ArtemisCooldownRules.ShouldSnarlWhileHoldingShared(true, true, true, 0.1f, line, 5f, float.MaxValue));
    }

    [Fact]
    public void SharedTankingIsOffByDefault() => Assert.False(new Daedalus.Config.DPS.BeastmasterConfig().CrucibleLegendarySharedTank);

    // --- the Siren on Legendary (user 2026-10-30): familiar through Summon and the crawler, beastmaster otherwise ---

    [Fact]
    public void SirenSummonCastHandsHerToTheFamiliar() =>
        Assert.True(ArtemisCooldownRules.SirenFamiliarHolds(summonCasting: true, crawlerAlive: false));

    [Fact]
    public void SirenCrawlerAliveKeepsHerOnTheFamiliar() =>
        Assert.True(ArtemisCooldownRules.SirenFamiliarHolds(summonCasting: false, crawlerAlive: true));

    [Fact]
    public void SirenCrawlerDeadHandsHerBackToTheBeastmaster() =>
        Assert.False(ArtemisCooldownRules.SirenFamiliarHolds(summonCasting: false, crawlerAlive: false));

    [Fact]
    public void SirenSummonWithACrawlerStillUpStaysOnTheFamiliar() =>
        Assert.True(ArtemisCooldownRules.SirenFamiliarHolds(summonCasting: true, crawlerAlive: true));

    // --- the Zu: a release can break eggs, and every broken egg makes the Zu stronger ---

    [Fact]
    public void ZuHealthyHoldsTheRelease() =>
        Assert.True(ArtemisCooldownRules.HoldReleaseAtZu(onBoard: true, zuHpPct: 0.60f, familiarFalling: false));

    [Fact]
    public void ZuNearlyDeadLetsTheReleaseFinishIt() =>
        Assert.False(ArtemisCooldownRules.HoldReleaseAtZu(true, ArtemisCooldownRules.ZuReleaseAtHpPct, false));

    [Fact]
    public void ZuHoldGivesWayToAFallingFamiliar() =>
        Assert.False(ArtemisCooldownRules.HoldReleaseAtZu(true, 0.60f, familiarFalling: true));

    /// <summary>No Zu in the fight (the catoblepas side, another board): nothing held.</summary>
    [Fact]
    public void ZuHoldIsOnlyForTheZu()
    {
        Assert.False(ArtemisCooldownRules.HoldReleaseAtZu(true, zuHpPct: null, false));
        Assert.False(ArtemisCooldownRules.HoldReleaseAtZu(onBoard: false, 0.60f, false));
    }

    /// <summary>Korha 2026-10-31: the release went out at a hatched Pullet — the hold reads the Zu, not the target.</summary>
    [Fact]
    public void ZuHoldAppliesWhileAnAddIsTargeted() =>
        Assert.True(ArtemisCooldownRules.HoldReleaseAtZu(true, zuHpPct: 0.45f, familiarFalling: false));

    /// <summary>The recording's Shield Charge at the Cockerel (128, -412) caught eggs at (128, -406) and (134, -412).</summary>
    [Fact]
    public void ShieldChargeSplashCatchesTheEggsBeside()
    {
        System.Numerics.Vector2[] eggs = [new(128f, -406f), new(134f, -412f)];
        Assert.True(ArtemisCooldownRules.SplashBreaksEgg(eggs, new(128f, -412f), ArtemisCooldownRules.ShieldChargeSplashYalms));
        Assert.False(ArtemisCooldownRules.SplashBreaksEgg(eggs, new(120f, -426.9f), ArtemisCooldownRules.ShieldChargeSplashYalms)); // the Zu, centre
        Assert.False(ArtemisCooldownRules.SplashBreaksEgg([], new(128f, -412f), ArtemisCooldownRules.ShieldChargeSplashYalms));
    }

    /// <summary>Replay 19:19, 96.9 s: you and the Zu at (130.3, -412.4), eggs at (134, -412) and (132, -408) — held.</summary>
    [Fact]
    public void ShieldChargeAtTheZuBesideEggsIsHeld()
    {
        System.Numerics.Vector2[] eggs = [new(134f, -412f), new(132f, -408f)];
        Assert.True(ArtemisCooldownRules.ShieldChargeBreaksEgg(eggs, new(130.26f, -412.39f), 3.6f, new(130.27f, -412.39f), 0.5f));
    }

    /// <summary>Replay 19:19, 0.3 s before the charge: the Zu still at (119.2, -415.2) mid-dash, you beside the eggs — held.</summary>
    [Fact]
    public void ShieldChargeIsHeldWhenYouStandBesideEggs()
    {
        System.Numerics.Vector2[] eggs = [new(134f, -412f), new(132f, -408f)];
        Assert.True(ArtemisCooldownRules.ShieldChargeBreaksEgg(eggs, new(119.19f, -415.2f), 3.6f, new(130.19f, -412.38f), 0.5f));
        Assert.True(ArtemisCooldownRules.ShieldChargeBreaksEgg(eggs, new(113.1f, -416.8f), 3.6f, new(130.19f, -412.38f), 0.5f));
    }

    /// <summary>A gap close lands at the target's edge on your side: an egg 6 yalms past that edge breaks, one across the Zu does not.</summary>
    [Fact]
    public void ShieldChargeSplashIsWhereYouLand()
    {
        var zu = new System.Numerics.Vector2(120f, -420f);
        var you = new System.Numerics.Vector2(120f, -400f); // 20 y north; landing at (120, -415.9)
        Assert.True(ArtemisCooldownRules.ShieldChargeBreaksEgg([new(120f, -410f)], zu, 3.6f, you, 0.5f));
        Assert.False(ArtemisCooldownRules.ShieldChargeBreaksEgg([new(120f, -434f)], zu, 3.6f, you, 0.5f));
        Assert.False(ArtemisCooldownRules.ShieldChargeBreaksEgg([], zu, 3.6f, you, 0.5f));
    }

    /// <summary>The eggs (BNpcName 14575, 14576) are never attacked; the Zu (14572) and its hatchlings are.</summary>
    [Fact]
    public void ZuEggsAreNeverAttacked()
    {
        Assert.Contains(14575u, FFXIVConstants.NeverAttackNameIds);
        Assert.Contains(14576u, FFXIVConstants.NeverAttackNameIds);
        Assert.DoesNotContain(ArtemisCooldownRules.ZuNameId, FFXIVConstants.NeverAttackNameIds);
        Assert.DoesNotContain(14573u, FFXIVConstants.NeverAttackNameIds); // cockerel piece
        Assert.DoesNotContain(14574u, FFXIVConstants.NeverAttackNameIds); // pullet piece
    }

    /// <summary>Lingering Vantage (Status sheet, 2026-10-31): "Next Parting Blow will deal increased damage".</summary>
    [Fact]
    public void LingeringVantageIdIsTheReleaseBuff() => Assert.Equal(4614u, ArtemisCooldownRules.LingeringVantageStatusId);

    /// <summary>The Siren's release-before-Blow skips Wespe: Final Sting would send it away before the Blow.</summary>
    [Fact]
    public void SirenVantageReleaseSkipsWespe()
    {
        Assert.False(Daedalus.Rotation.ArtemisCore.Modules.DamageModule.ShouldTemperedRelease(
            true, true, Daedalus.Rotation.ArtemisCore.Modules.DamageModule.WespeBestiaryNo, partingBlowEnabled: false, 1f, double.MaxValue));
        Assert.True(Daedalus.Rotation.ArtemisCore.Modules.DamageModule.ShouldTemperedRelease(
            true, true, bestiaryNo: 44, partingBlowEnabled: false, 1f, double.MaxValue)); // Damselfly
    }

    /// <summary>The Zu's BNpcName, read off XBMBattleDetail 16.0 (2026-10-31).</summary>
    [Fact]
    public void ZuIdIsTheThirdBoardElite() => Assert.Equal(14572u, ArtemisCooldownRules.ZuNameId);

    /// <summary>The ids read off the Minerva replay of 2026-10-30 (Summon BDB7, crawler 14586, Siren 14583).</summary>
    [Fact]
    public void SirenIdsMatchTheReplay()
    {
        Assert.Equal(48567u, ArtemisCooldownRules.SirenSummonActionId);
        Assert.Equal(14586u, ArtemisCooldownRules.CrawlingPieceNameId);
        Assert.Equal(14583u, ArtemisCooldownRules.SirenNameId);
    }

    // --- boss research 2026-11-01 ---

    /// <summary>Burning Ward comes at 52 s in every recorded Ogre fight: Final Sting goes in the 8 s before it.</summary>
    [Theory]
    [InlineData(true, 43.9, false)]
    [InlineData(true, 44.0, true)]
    [InlineData(true, 51.9, true)]
    [InlineData(true, 52.0, false)]
    [InlineData(false, 48.0, false)]
    public void FinalStingGoesJustBeforeTheOgresWard(bool ogreFight, double secondsIn, bool sting)
        => Assert.Equal(sting, ArtemisCooldownRules.StingBeforeOgreWard(ogreFight, secondsIn));

    /// <summary>Curtains for Rank 5, Molten Metal and Beastly Aura send the familiar away; a Thunderbolt does not.</summary>
    [Fact]
    public void TheFamiliarIsSentAwayFromTheCastsThatKillIt()
    {
        Assert.Contains(49429u, ArtemisCooldownRules.SendFamiliarAwayCastIds);
        Assert.Contains(49465u, ArtemisCooldownRules.SendFamiliarAwayCastIds);
        Assert.Contains(48607u, ArtemisCooldownRules.SendFamiliarAwayCastIds);
        Assert.DoesNotContain(48620u, ArtemisCooldownRules.SendFamiliarAwayCastIds);
    }

    /// <summary>Parting Blow's 8 yalms (plus the egg margin) round its target break an egg; a little further does not.</summary>
    [Fact]
    public void PartingBlowNearAnEggIsHeld()
    {
        System.Numerics.Vector2[] eggs = [new(108.5f, -400f)];
        Assert.True(ArtemisCooldownRules.PartingBlowBreaksEgg(eggs, new(100f, -400f)));
        Assert.False(ArtemisCooldownRules.PartingBlowBreaksEgg(eggs, new(99f, -400f)));
        Assert.False(ArtemisCooldownRules.PartingBlowBreaksEgg([], new(100f, -400f)));
    }

    /// <summary>The resist row reads true = immune: the Ogre (row 16) takes Blind and Poison only.</summary>
    [Fact]
    public void AnEnemyTakesWhatItsResistRowLeavesOpen()
    {
        var ogre = new CrucibleEnemy(14538, "ogre piece", 5, false, CrucibleElement.Wind, [4, 3, 4, 2, 4], [],
            [true, true, true, true, false, false, true, true, true, true, true]);
        Assert.True(ogre.TakesStatus(CrucibleStatus.Poison));
        Assert.True(ogre.TakesStatus(CrucibleStatus.Blind));
        Assert.False(ogre.TakesStatus(CrucibleStatus.Stun));
        Assert.Null(new CrucibleEnemy(1, "x", 1, false, CrucibleElement.None, [1, 1, 1, 1, 1], []).TakesStatus(CrucibleStatus.Poison));
    }

    /// <summary>
    /// A wisp blows up 19 s after it appears: the soonest one counts, a just-spawned wave is 19 s off, none is never.
    /// </summary>
    [Fact]
    public void TheNextWispExplosionIsNineteenSecondsAfterItAppears()
    {
        Assert.Equal(19f, ArtemisCooldownRules.WispArrivalSeconds([0.0]), 3);
        Assert.Equal(4f, ArtemisCooldownRules.WispArrivalSeconds([3.0, 15.0, 1.0]), 3);
        Assert.Equal(0f, ArtemisCooldownRules.WispArrivalSeconds([25.0]), 3);
        Assert.Equal(float.MaxValue, ArtemisCooldownRules.WispArrivalSeconds([]));
    }

    /// <summary>
    /// Snarl so the familiar's Cover takes a wisp's Vulnerability Up: within 10 s of it, and only when the Cover up now
    /// would run out before it lands (Snarl's Cover lasts 45 s, so one covers the whole ward).
    /// </summary>
    [Theory]
    [InlineData(8f, 0f, true)]    // a wisp in 8 s, no Cover
    [InlineData(8f, 5f, true)]    // the Cover ends first
    [InlineData(8f, 30f, false)]  // already covered past it
    [InlineData(15f, 0f, false)]  // too early: Snarl would be spent
    public void SnarlForAWispOnlyWhenTheCoverWouldNotLast(float wispIn, float coverLeft, bool snarl)
        => Assert.Equal(snarl, ArtemisCooldownRules.ShouldSnarlForWisp(true, true, true, 0.8f, wispIn, coverLeft));

    [Fact]
    public void NoWispSnarlWithoutAFamiliarOrWithSnarlOff()
    {
        Assert.False(ArtemisCooldownRules.ShouldSnarlForWisp(true, true, familiarOut: false, 0.8f, 5f, 0f));
        Assert.False(ArtemisCooldownRules.ShouldSnarlForWisp(enabled: false, true, true, 0.8f, 5f, 0f));
        Assert.False(ArtemisCooldownRules.ShouldSnarlForWisp(true, ready: false, true, 0.8f, 5f, 0f));
    }

    /// <summary>The Bone Knight's guard: Snarl at him so he faces the familiar — not when he already does.</summary>
    [Fact]
    public void SnarlTurnsAGuardedBoneKnightToTheFamiliar()
    {
        Assert.True(ArtemisCooldownRules.ShouldSnarlBoneKnight(true, true, true, 0.9f, knightOnFamiliar: false));
        Assert.False(ArtemisCooldownRules.ShouldSnarlBoneKnight(true, true, true, 0.9f, knightOnFamiliar: true));
        Assert.False(ArtemisCooldownRules.ShouldSnarlBoneKnight(true, true, familiarOut: false, 0f, false));
        Assert.False(ArtemisCooldownRules.ShouldSnarlBoneKnight(true, ready: false, true, 0.9f, false));
    }

    /// <summary>
    /// The beasts ahead of a waiting Wespe are cycled out (Release, then Parting Blow) so it comes out to sting — not once
    /// it is out, not with no other horn ready, not off a board.
    /// </summary>
    [Fact]
    public void BeastsAheadOfAWaitingWespeAreCycledOut()
    {
        Assert.True(ArtemisCooldownRules.CyclesToWespe(true, true, false, true, float.MaxValue, float.MaxValue));
        Assert.False(ArtemisCooldownRules.CyclesToWespe(true, true, wespeOut: true, true, float.MaxValue, float.MaxValue));
        Assert.False(ArtemisCooldownRules.CyclesToWespe(true, true, false, replacementReady: false, float.MaxValue, float.MaxValue));
        Assert.False(ArtemisCooldownRules.CyclesToWespe(onBoard: false, true, false, true, float.MaxValue, float.MaxValue));
        Assert.False(ArtemisCooldownRules.CyclesToWespe(true, wespeWaiting: false, false, true, float.MaxValue, float.MaxValue));
    }

    /// <summary>No cycling with a raidwide, wisp explosion or tankbuster due: the familiar out takes it.</summary>
    [Fact]
    public void NoCycleWithAHitDue()
    {
        Assert.False(ArtemisCooldownRules.CyclesToWespe(true, true, false, true, secondsUntilRaidwide: 6f, float.MaxValue));
        Assert.False(ArtemisCooldownRules.CyclesToWespe(true, true, false, true, float.MaxValue, secondsUntilTankbuster: 3f));
    }

    /// <summary>A Wespe waits in a ready horn other than the out one; its own horn out, on cooldown, or unread is no wait.</summary>
    [Fact]
    public void AWespeWaitsOnlyInAnotherReadyHorn()
    {
        uint[] bugs = [44, 16, 10]; // Damselfly, Mantis, Wespe
        Assert.True(Daedalus.Rotation.ArtemisCore.Helpers.ArtemisFamiliarKeeper.WespeWaiting(true, 0, bugs, [false, true, true]));
        Assert.False(Daedalus.Rotation.ArtemisCore.Helpers.ArtemisFamiliarKeeper.WespeWaiting(true, 2, bugs, [true, true, true]));
        Assert.False(Daedalus.Rotation.ArtemisCore.Helpers.ArtemisFamiliarKeeper.WespeWaiting(true, 0, bugs, [false, true, false]));
        Assert.False(Daedalus.Rotation.ArtemisCore.Helpers.ArtemisFamiliarKeeper.WespeWaiting(false, 0, bugs, [true, true, true]));
        Assert.False(Daedalus.Rotation.ArtemisCore.Helpers.ArtemisFamiliarKeeper.WespeWaiting(true, 0, [44u, 16u, 1u], [true, true, true]));
    }

    /// <summary>The Crucible's own Parting Blows sit behind the Crucible toggle, not the opt-in Parting Blow one.</summary>
    [Fact]
    public void CrucibleBlowsDoNotNeedTheOptInPartingBlowSetting()
    {
        var cfg = new Daedalus.Configuration();
        cfg.Beastmaster.EnablePartingBlow = false;
        Assert.True(Daedalus.Rotation.ArtemisCore.Abilities.ArtemisAbilities.CruciblePartingBlow.Toggle!(cfg));
        Assert.False(Daedalus.Rotation.ArtemisCore.Abilities.ArtemisAbilities.PartingBlow.Toggle!(cfg));
    }

    /// <summary>A falling familiar is swapped out with a horn only when Parting Blow (its retreat) is not ready.</summary>
    [Fact]
    public void AFallingFamiliarIsSwappedOutOnlyWhenPartingBlowIsDown()
    {
        Assert.True(ArtemisCooldownRules.ShouldSwapOutFalling(familiarFalling: true, partingBlowReady: false));
        Assert.False(ArtemisCooldownRules.ShouldSwapOutFalling(familiarFalling: true, partingBlowReady: true));
        Assert.False(ArtemisCooldownRules.ShouldSwapOutFalling(familiarFalling: false, partingBlowReady: false));
    }

    /// <summary>The swap goes to the next ready horn after the out one, wrapping round, never the out one itself.</summary>
    [Fact]
    public void TheSwapHornIsTheNextReadyOneAfterTheOutBeast()
    {
        Assert.Equal(1, Daedalus.Rotation.ArtemisCore.Helpers.ArtemisFamiliarKeeper.PickSwapHorn(true, true, 10, 0, [true, true, true]));
        Assert.Equal(2, Daedalus.Rotation.ArtemisCore.Helpers.ArtemisFamiliarKeeper.PickSwapHorn(true, true, 10, 0, [true, false, true]));
        Assert.Equal(0, Daedalus.Rotation.ArtemisCore.Helpers.ArtemisFamiliarKeeper.PickSwapHorn(true, true, 10, 2, [true, false, true]));
        Assert.Equal(-1, Daedalus.Rotation.ArtemisCore.Helpers.ArtemisFamiliarKeeper.PickSwapHorn(true, true, 10, 1, [false, true, false]));
    }

    /// <summary>No swap while moving (the horn is a cast), right after a try, or when not wanted.</summary>
    [Fact]
    public void NoSwapWhileMovingOrJustAfterATry()
    {
        Assert.Equal(-1, Daedalus.Rotation.ArtemisCore.Helpers.ArtemisFamiliarKeeper.PickSwapHorn(true, canAct: false, 10, 0, [true, true, true]));
        Assert.Equal(-1, Daedalus.Rotation.ArtemisCore.Helpers.ArtemisFamiliarKeeper.PickSwapHorn(true, true, 1.0, 0, [true, true, true]));
        Assert.Equal(-1, Daedalus.Rotation.ArtemisCore.Helpers.ArtemisFamiliarKeeper.PickSwapHorn(wanted: false, true, 10, 0, [true, true, true]));
    }

    /// <summary>On the First Board only the Ogre and the final boss get the Wespe's Final Sting; other boards, every fight.</summary>
    [Theory]
    [InlineData(1, true, false, true)]   // First Board, the Ogre
    [InlineData(1, false, true, true)]   // First Board, the final boss
    [InlineData(1, false, false, false)] // First Board, any other fight: normal play
    [InlineData(3, false, false, true)]  // another board: unchanged
    [InlineData(2, false, false, true)]
    public void TheFirstBoardStingsOnlyTheOgreAndTheFinalBoss(int board, bool ogre, bool finalBoss, bool sting)
        => Assert.Equal(sting, ArtemisCooldownRules.WespeStingFight(board, ogre, finalBoss));

    /// <summary>
    /// The Mantis holds its Soundwave while a Wespe waits in a sting fight, until the target is at 35% (the sting is at
    /// 30%): the vulnerability (30 s) is then up when Final Sting lands.
    /// </summary>
    [Theory]
    [InlineData(0.80f, false)]
    [InlineData(0.36f, false)]
    [InlineData(0.35f, true)]
    [InlineData(0.20f, true)]
    public void TheMantisHoldsItsSoundwaveForTheSting(float targetHp, bool releases)
        => Assert.Equal(releases, ArtemisCooldownRules.MantisReleasesNow(true, true, false, targetHp, false, 10));

    /// <summary>Against the Ogre the Mantis lets go 14 s before the ward (52 s), so the Wespe can sting before it.</summary>
    [Theory]
    [InlineData(30.0, false)]
    [InlineData(38.0, true)]
    [InlineData(51.0, true)]
    [InlineData(60.0, false)] // after the ward: back to the HP line
    public void AgainstTheOgreTheMantisLetsGoBeforeTheWard(double secondsIn, bool releases)
        => Assert.Equal(releases, ArtemisCooldownRules.MantisReleasesNow(true, true, false, 0.9f, ogreFight: true, secondsIn));

    /// <summary>No hold outside a sting fight, with no Wespe waiting, or when the Mantis is falling.</summary>
    [Fact]
    public void TheMantisDoesNotHoldWithoutAStingToCome()
    {
        Assert.True(ArtemisCooldownRules.MantisReleasesNow(stingFight: false, true, false, 0.9f, false, 10));
        Assert.True(ArtemisCooldownRules.MantisReleasesNow(true, wespeWaiting: false, false, 0.9f, false, 10));
        Assert.True(ArtemisCooldownRules.MantisReleasesNow(true, true, falling: true, 0.9f, false, 10));
    }
}
