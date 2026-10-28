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
}
