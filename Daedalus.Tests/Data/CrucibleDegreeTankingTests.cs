using Daedalus.Data;
using Daedalus.Rotation.ArtemisCore.Helpers;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>
/// Who takes what on a degree (user, 2026-11-15, after TPLemont's and fuurie's Third Degree clears): a beast no later
/// battle of the board needs does the tanking and takes the tankbusters; while the beast out is one a later battle
/// needs, the beastmaster holds the enemy and the beast is kept out of it, a critical beastmaster included.
/// <para>Korha, First Board Third Degree, 2026-11-15. Every beast tanking: battle 1 left the Wespe at 28%, Mandragora
/// at 25% and Opo-opo at 3%, and the plan's Wespe never came out again. The beastmaster tanking for every beast: 28%
/// HP a minute into each fight, and once critical all three beasts were fed to the Arch Demon.</para>
/// </summary>
public class CrucibleDegreeTankingTests
{
    private const int Wespe = 10, IceGolem = 47, Behemoth = 50, Mandragora = 12, OpoOpo = 5, Colibri = 32, Damselfly = 44,
        Megalocrab = 9, Geshunpest = 13, Coeurl = 33;

    [Fact]
    public void TheBeastmasterTanksForABeastALaterBattleNeeds()
        => Assert.True(ArtemisCooldownRules.BeastmasterTanksOnDegree(3, beastNeededLater: true));

    [Fact]
    public void ABeastNoLaterBattleNeedsDoesTheTanking()
        => Assert.False(ArtemisCooldownRules.BeastmasterTanksOnDegree(3, beastNeededLater: false));

    [Fact]
    public void OffADegreeTheFamiliarTanksAsBefore()
        => Assert.False(ArtemisCooldownRules.BeastmasterTanksOnDegree(0, beastNeededLater: true));

    // --- "we take tank busters unless the pet is not needed later" ---

    [Fact]
    public void ABeastALaterBattleNeedsDoesNotTakeTheBuster()
        => Assert.False(ArtemisCooldownRules.FamiliarTakesTheBusterOnDegree(true, true, 1f, neededLater: true, anotherHornReady: true));

    [Fact]
    public void ABeastNoLaterBattleNeedsTakesIt()
        => Assert.True(ArtemisCooldownRules.FamiliarTakesTheBusterOnDegree(true, true, 1f, neededLater: false, anotherHornReady: true));

    [Fact]
    public void ButNotTheLastBeastStanding()
        => Assert.False(ArtemisCooldownRules.FamiliarTakesTheBusterOnDegree(true, true, 1f, neededLater: false, anotherHornReady: false));

    [Fact]
    public void AndNotWithNoneOut()
        => Assert.False(ArtemisCooldownRules.FamiliarTakesTheBusterOnDegree(true, false, 0f, neededLater: false, anotherHornReady: true));

    // --- which beasts a later battle still needs ---

    /// <summary>Banemite (battle 4): the Geshunpest is on neither the Ogre's nor Pas de Seul's team.</summary>
    [Fact]
    public void AtTheBanemiteTheGeshunpestIsNotNeededLater()
    {
        var later = CrucibleDegreeTeams.NeededLater(3, 1, 4);
        Assert.DoesNotContain(Geshunpest, later);
        Assert.Contains(Wespe, later);       // the Ogre and Pas de Seul
        Assert.Contains(Behemoth, later);    // Pas de Seul
        Assert.Contains(Colibri, later);
    }

    /// <summary>The Bone pair: the Wespe is kept, the Mandragora and the Opo-opo tank.</summary>
    [Fact]
    public void AtTheFirstBattleTheWespeIsNeededAndItsTeammatesAreNot()
    {
        var later = CrucibleDegreeTeams.NeededLater(3, 1, 1);
        Assert.Contains(Wespe, later);
        Assert.DoesNotContain(Mandragora, later);
        Assert.DoesNotContain(OpoOpo, later);
        Assert.Equal(Wespe, CrucibleDegreeTeams.For(3, 1)![0]); // first out: it stings the Bishop at once and leaves whole
    }

    /// <summary>
    /// Korha, 2026-11-15 16:24: at the Ogre the Damselfly was kept "for later" — for the Piscodemon, the side of the
    /// fork she had not taken two battles before — so it neither tanked nor covered her, and she died 28 s in.
    /// </summary>
    [Fact]
    public void AtTheOgreOnlyPasDeSeulsBeastsAreNeededLater()
    {
        var later = CrucibleDegreeTeams.NeededLater(3, 1, 5);
        Assert.DoesNotContain(Damselfly, later);
        Assert.DoesNotContain(Megalocrab, later);
        Assert.Equal([Wespe, Behemoth, Colibri], later);
    }

    /// <summary>A battle already behind is not "later": the Arch Demon's Ice Golem and Coeurl can be spent from the Banemite on.</summary>
    [Fact]
    public void ABattleBehindIsNotLater()
    {
        var later = CrucibleDegreeTeams.NeededLater(3, 1, 4);
        Assert.DoesNotContain(IceGolem, later);
        Assert.DoesNotContain(Coeurl, later);
    }

    /// <summary>At the Arch Demon the Geshunpest is ahead (the Banemite), so it is not on this team to be spent.</summary>
    [Fact]
    public void AtTheArchDemonTheGeshunpestIsStillToCome()
    {
        Assert.Contains(Geshunpest, CrucibleDegreeTeams.NeededLater(3, 1, 2));
        Assert.DoesNotContain(Geshunpest, CrucibleDegreeTeams.For(3, 2)!);
        Assert.DoesNotContain(IceGolem, CrucibleDegreeTeams.NeededLater(3, 1, 2));
    }

    /// <summary>The fork: at the Arch Demon the Piscodemon's team is not ahead, and the other way round.</summary>
    [Fact]
    public void TheOtherSideOfTheForkIsNotLater()
    {
        Assert.DoesNotContain(16, CrucibleDegreeTeams.NeededLater(3, 1, 2)); // Mantis, the Piscodemon's
        Assert.DoesNotContain(IceGolem, CrucibleDegreeTeams.NeededLater(3, 1, 3)); // the Arch Demon's
    }

    [Fact]
    public void AtTheFinalBattleNothingIsNeededLater()
        => Assert.Empty(CrucibleDegreeTeams.NeededLater(3, 1, 6));

    [Fact]
    public void OffADegreeOrOnABoardWithNoTeamsNothingIsListed()
    {
        Assert.Empty(CrucibleDegreeTeams.NeededLater(0, 1, 1));
        Assert.Empty(CrucibleDegreeTeams.NeededLater(3, 2, 7));
    }

    // --- the hits the familiar takes ---

    /// <summary>The Arch Demon's Dismember: a 4.2 s cast that left Damage Down on every one of 62 recorded hits.</summary>
    [Fact]
    public void DismemberIsTheFamiliars() => Assert.Contains(46879u, ArtemisCooldownRules.FamiliarTakesCastIds);

    /// <summary>Deadly Thrust is a tankbuster (60-81% of a familiar's HP on Third Degree): not on the list.</summary>
    [Fact]
    public void DeadlyThrustIsNot() => Assert.DoesNotContain(46906u, ArtemisCooldownRules.FamiliarTakesCastIds);

    [Fact]
    public void TheFamiliarIsSnarledIntoADebuffCast()
        => Assert.True(ArtemisCooldownRules.ShouldSnarlForDebuffCast(true, true, true, 0.8f, 0.35f, casterOnFamiliar: false));

    [Fact]
    public void NotAFallingOneAndNotOneAlreadyHoldingTheCaster()
    {
        Assert.False(ArtemisCooldownRules.ShouldSnarlForDebuffCast(true, true, true, 0.3f, 0.35f, casterOnFamiliar: false));
        Assert.False(ArtemisCooldownRules.ShouldSnarlForDebuffCast(true, true, true, 0.8f, 0.35f, casterOnFamiliar: true));
    }

    // --- two or more enemies ---

    [Fact]
    public void WithTwoOnTheBeastmasterTheFamiliarTakesOne()
        => Assert.True(ArtemisCooldownRules.ShouldSnarlToHoldOne(true, true, true, 0.9f, 0.35f, familiarHoldsOne: false, anotherOnBeastmaster: true));

    [Fact]
    public void OneIsEnoughForIt()
        => Assert.False(ArtemisCooldownRules.ShouldSnarlToHoldOne(true, true, true, 0.9f, 0.35f, familiarHoldsOne: true, anotherOnBeastmaster: true));

    [Fact]
    public void WithOneEnemyTheBeastmasterHoldsIt()
        => Assert.False(ArtemisCooldownRules.ShouldSnarlToHoldOne(true, true, true, 0.9f, 0.35f, familiarHoldsOne: false, anotherOnBeastmaster: false));

    // --- Cover after its job is done ---

    [Fact]
    public void ACoverWithNothingLeftToTakeIsEnded()
        => Assert.True(ArtemisCooldownRules.CoverNoLongerWanted(30f, float.MaxValue, debuffCastGoing: false, selfHpPct: 0.8f));

    [Fact]
    public void NotWhileAWispWalksInOrADebuffCastGoesOrTheBeastmasterIsCritical()
    {
        Assert.False(ArtemisCooldownRules.CoverNoLongerWanted(30f, 4f, false, 0.8f));
        Assert.False(ArtemisCooldownRules.CoverNoLongerWanted(30f, float.MaxValue, true, 0.8f));
        Assert.False(ArtemisCooldownRules.CoverNoLongerWanted(30f, float.MaxValue, false, 0.2f));
        Assert.False(ArtemisCooldownRules.CoverNoLongerWanted(0f, float.MaxValue, false, 0.8f));
    }

    // --- Poison, with an Antipoison serum in the bag ---

    private const int Offset = CrucibleAutoPilotRules.CrucibleItemIconOffset;

    /// <summary>Korha, 2026-11-15: Deadly Thrust took her from 50% to 22%, the Poison did the rest, the serum stayed in the bag.</summary>
    [Fact]
    public void PoisonedAndHurtTheSerumIsDrunkInAnyBattle()
        => Assert.True(CrucibleAutoPilotRules.ShouldCurePoison(legendary: false, useItems: true, poisoned: true, selfHpPct: 0.22f));

    [Fact]
    public void NotWhileHealthyNorUnpoisonedNorOnLegendary()
    {
        Assert.False(CrucibleAutoPilotRules.ShouldCurePoison(false, true, poisoned: true, selfHpPct: 0.9f));
        Assert.False(CrucibleAutoPilotRules.ShouldCurePoison(false, true, poisoned: false, selfHpPct: 0.2f));
        Assert.False(CrucibleAutoPilotRules.ShouldCurePoison(true, true, poisoned: true, selfHpPct: 0.2f));
        Assert.False(CrucibleAutoPilotRules.ShouldCurePoison(false, false, poisoned: true, selfHpPct: 0.2f));
    }

    [Fact]
    public void TheBetterSerumGoesFirst()
    {
        Assert.Equal(1, CrucibleAutoPilotRules.PickAntipoison([(uint)(86 + Offset), (uint)(87 + Offset)]));
        Assert.Equal(0, CrucibleAutoPilotRules.PickAntipoison([(uint)(86 + Offset), 0]));
        Assert.Equal(-1, CrucibleAutoPilotRules.PickAntipoison([(uint)(95 + Offset), 243137]));
    }

    [Fact]
    public void ThePoisonIsTheOneDeadlyThrustLeaves() => Assert.Equal(5140u, CrucibleAutoPilotRules.CruciblePoisonStatusId);

    // --- Pas de Seul: the beastmaster takes every hit until the succubi are dead ---

    /// <summary>
    /// Korha, 2026-11-15 17:29: the Behemoth tanked Pas de Seul, took the first Cold Caress, was retreated at 28% and
    /// its Meteor went on her alone 8 s before the succubi came. Now it is kept from the pull until they are dead.
    /// </summary>
    [Fact]
    public void AtPasDeSeulTheBeastIsKeptUntilTheSuccubiHaveComeAndDied()
    {
        Assert.True(ArtemisCooldownRules.KeepsBeastForSuccubi(3, pasDeSeulFight: true, succubiSeen: false, succubiUp: 0));
        Assert.True(ArtemisCooldownRules.KeepsBeastForSuccubi(3, true, succubiSeen: true, succubiUp: 2));
        Assert.False(ArtemisCooldownRules.KeepsBeastForSuccubi(3, true, succubiSeen: true, succubiUp: 0));
    }

    [Fact]
    public void NotInAnotherFightAndNotOffADegree()
    {
        Assert.False(ArtemisCooldownRules.KeepsBeastForSuccubi(3, pasDeSeulFight: false, false, 0));
        Assert.False(ArtemisCooldownRules.KeepsBeastForSuccubi(0, true, false, 0));
    }

    /// <summary>The Meteor waits for the succubi to come, then for them to close in, nine seconds at most.</summary>
    [Fact]
    public void TheMeteorWaitsForTheSuccubi()
    {
        bool Hold(bool seen, int up, bool bunched, double secondsUp, bool falling = false, int beast = Behemoth, int degree = 3) =>
            ArtemisCooldownRules.HoldMeteorForSuccubi(degree, beast, true, seen, up, bunched, secondsUp, falling);

        Assert.True(Hold(seen: false, up: 0, bunched: false, secondsUp: 0));      // not here yet
        Assert.True(Hold(seen: true, up: 3, bunched: false, secondsUp: 4));       // setting off
        Assert.False(Hold(seen: true, up: 3, bunched: true, secondsUp: 4));       // closed in
        Assert.False(Hold(seen: true, up: 3, bunched: false, secondsUp: 9));      // long enough
        Assert.False(Hold(seen: true, up: 0, bunched: false, secondsUp: 0));      // dead: Meteor goes on Pas de Seul
        Assert.False(Hold(seen: false, up: 0, bunched: false, secondsUp: 0, falling: true));
        Assert.False(Hold(seen: false, up: 0, bunched: false, secondsUp: 0, beast: Colibri));
        Assert.False(Hold(seen: false, up: 0, bunched: false, secondsUp: 0, degree: 0));
        Assert.False(ArtemisCooldownRules.HoldMeteorForSuccubi(3, Behemoth, pasDeSeulFight: false, false, 0, false, 0, false));
    }

    // --- a tankbuster being cast at the beastmaster: Tome of the Impervious (Blink) or Crucible Tannin ---

    private const uint DeadlyThrust = 46906, ColdCaress = 46935, BloodSword = 46934;
    private const int Tome = CrucibleAutoPilotRules.TomeOfTheImperviousRow, Tannin = CrucibleAutoPilotRules.CrucibleTanninRow;

    /// <summary>
    /// Korha, 2026-11-15: two Banemite fights with the same damage dealt. With a Blink up (Mirage Vest) the second
    /// Deadly Thrust did 0 and left no Poison — won at 55% HP. Without, 473 and the Poison killed her 15 s later.
    /// </summary>
    [Fact]
    public void DeadlyThrustIsAnsweredWithTheTome()
        => Assert.Equal(1, CrucibleAutoPilotRules.PickBusterGuard([(uint)(Tannin + Offset), (uint)(Tome + Offset)], DeadlyThrust));

    [Fact]
    public void WithNoTomeTheTanninGoes()
        => Assert.Equal(0, CrucibleAutoPilotRules.PickBusterGuard([(uint)(Tannin + Offset), 243137], DeadlyThrust));

    /// <summary>Cold Caress and Blood Sword are magic: Blink does nothing, the Tome stays in the bag.</summary>
    [Theory]
    [InlineData(ColdCaress)]
    [InlineData(BloodSword)]
    public void AMagicTankbusterGetsTheTanninNeverTheTome(uint cast)
    {
        Assert.Equal(-1, CrucibleAutoPilotRules.PickBusterGuard([(uint)(Tome + Offset)], cast));
        Assert.Equal(1, CrucibleAutoPilotRules.PickBusterGuard([(uint)(Tome + Offset), (uint)(Tannin + Offset)], cast));
    }

    [Fact]
    public void NothingWithoutACastOrWithoutEitherItem()
    {
        Assert.Equal(-1, CrucibleAutoPilotRules.PickBusterGuard([(uint)(Tome + Offset)], 0));
        Assert.Equal(-1, CrucibleAutoPilotRules.PickBusterGuard([243137, 0], DeadlyThrust));
    }

    [Fact]
    public void TheGuardNeedsACastItemsOnAndNotLegendary()
    {
        Assert.True(CrucibleAutoPilotRules.ShouldGuardBuster(legendary: false, useItems: true, DeadlyThrust));
        Assert.False(CrucibleAutoPilotRules.ShouldGuardBuster(false, true, 0));
        Assert.False(CrucibleAutoPilotRules.ShouldGuardBuster(false, false, DeadlyThrust));
        Assert.False(CrucibleAutoPilotRules.ShouldGuardBuster(true, true, DeadlyThrust));
    }

    /// <summary>
    /// Korha, 2026-11-15 18:22: the first Deadly Thrust was cast at her with the Ice Golem's Cover up. The Golem took
    /// it (450) and her Tannin went for nothing, gone 15 s before the second Thrust, which was hers.
    /// </summary>
    [Fact]
    public void ATankbusterTheBeastTakesIsNotHersToAnswer()
    {
        Assert.False(ArtemisCooldownRules.BusterIsTheBeastmasters(true, coverLeft: 8f, beastOut: true, beastOutIsKept: false));
        Assert.False(ArtemisCooldownRules.BusterIsTheBeastmasters(true, coverLeft: 0f, beastOut: true, beastOutIsKept: false)); // Snarl is coming
        Assert.False(ArtemisCooldownRules.BusterIsTheBeastmasters(true, coverLeft: 3f, beastOut: true, beastOutIsKept: true));
        Assert.False(ArtemisCooldownRules.BusterIsTheBeastmasters(false, 0f, false, true));
    }

    [Fact]
    public void WithAKeptBeastOutOrNoneTheTankbusterIsHers()
    {
        Assert.True(ArtemisCooldownRules.BusterIsTheBeastmasters(true, 0f, beastOut: true, beastOutIsKept: true));
        Assert.True(ArtemisCooldownRules.BusterIsTheBeastmasters(true, 0f, beastOut: false, beastOutIsKept: true));
    }

    [Fact]
    public void TheThreeFirstBoardTankbustersAreKnownAndOnlyDeadlyThrustIsPhysical()
    {
        Assert.Equal(new uint[] { DeadlyThrust, BloodSword, ColdCaress }, CrucibleAutoPilotRules.TankbusterCastIds.Order().ToArray());
        Assert.Equal(new[] { DeadlyThrust }, CrucibleAutoPilotRules.PhysicalBusterCastIds.ToArray());
    }

    /// <summary>The First Board's only shop is before Pas de Seul: the Tannin is kept for her, the Tome is sold as before.</summary>
    [Fact]
    public void AtTheShopTheTanninIsKeptAndTheTomeSold()
    {
        Assert.True(CrucibleAutoPilotRules.UsesItem(Tannin, board: 1));
        Assert.False(CrucibleAutoPilotRules.UsesItem(Tannin, board: 2));
        Assert.False(CrucibleAutoPilotRules.UsesItem(Tome, board: 1));
    }

    /// <summary>The cast hint goes stale after a second: the rotation stopped writing it.</summary>
    [Fact]
    public void TheCastHintIsOnlyTrustedWhileFresh()
    {
        var now = new System.DateTime(2026, 11, 15, 18, 0, 0, System.DateTimeKind.Utc);
        var (id, at) = (Daedalus.Services.Beastmaster.CrucibleHints.BusterCastOnMe, Daedalus.Services.Beastmaster.CrucibleHints.BusterCastUtc);
        try
        {
            Daedalus.Services.Beastmaster.CrucibleHints.BusterCastOnMe = DeadlyThrust;
            Daedalus.Services.Beastmaster.CrucibleHints.BusterCastUtc = now;
            Assert.Equal(DeadlyThrust, Daedalus.Services.Beastmaster.CrucibleHints.BusterCastNow(now.AddSeconds(0.5)));
            Assert.Equal(0u, Daedalus.Services.Beastmaster.CrucibleHints.BusterCastNow(now.AddSeconds(2)));
        }
        finally
        {
            Daedalus.Services.Beastmaster.CrucibleHints.BusterCastOnMe = id;
            Daedalus.Services.Beastmaster.CrucibleHints.BusterCastUtc = at;
        }
    }

    // --- the Crucible Antidote: cures the poison and heals a quarter ---

    private const int Antidote = CrucibleAutoPilotRules.CrucibleAntidoteRow;

    /// <summary>
    /// Korha, 2026-11-15 17:29: the last shop sold an Antidote for 70 tokens and was left with 328 unspent. Pas de
    /// Seul's second Cold Caress took her to 54 HP and the poison killed her three seconds later, the boss at 30%.
    /// </summary>
    [Fact]
    public void TheLastShopsAntidoteIsBoughtOnTheFirstBoard()
    {
        CrucibleShopOffer[] offers = [new(0, 76, 70, false), new(1, Antidote, 70, false), new(2, 137, 140, false)];
        Assert.Equal(new[] { 1 }, CrucibleAutoPilotRules.ShopBuysForAntidote(offers, 398, itemsHeld: 1, board: 1));
    }

    [Fact]
    public void NoAntidoteWhenGearAnswersPoisonOrItCannotBeBoughtOrOnAnotherBoard()
    {
        CrucibleShopOffer[] offers = [new(1, Antidote, 70, false)];
        Assert.Empty(CrucibleAutoPilotRules.ShopBuysForAntidote(offers, 398, 1, board: 1, coveredByGear: ["Poison"]));
        Assert.Empty(CrucibleAutoPilotRules.ShopBuysForAntidote(offers, 69, 1, board: 1));
        Assert.Empty(CrucibleAutoPilotRules.ShopBuysForAntidote(offers, 398, CrucibleAutoPilotRules.MaxCrucibleItemsHeld, board: 1));
        Assert.Empty(CrucibleAutoPilotRules.ShopBuysForAntidote([new(1, Antidote, 70, true)], 398, 1, board: 1));
        Assert.Empty(CrucibleAutoPilotRules.ShopBuysForAntidote(offers, 398, 1, board: 4)); // Toxicosis: an Antidote does not cure it
    }

    /// <summary>It is kept, not sold, on the First Board; elsewhere it is sold as before.</summary>
    [Fact]
    public void TheAntidoteIsKeptOnTheFirstBoardOnly()
    {
        Assert.True(CrucibleAutoPilotRules.UsesItem(Antidote, board: 1));
        Assert.False(CrucibleAutoPilotRules.UsesItem(Antidote, board: 2));
    }

    /// <summary>Poisoned, the Antidote goes before a serum: it cures and heals 25%.</summary>
    [Fact]
    public void PoisonedTheAntidoteGoesBeforeASerum()
        => Assert.Equal(1, CrucibleAutoPilotRules.PickAntipoison([(uint)(87 + Offset), (uint)(Antidote + Offset)]));

    // --- a tankbuster with a kept beast out: a beast that can be spent comes out for it ---

    /// <summary>
    /// Korha, 2026-11-15: at 52% she took the Banemite's second Deadly Thrust for the Behemoth's sake (453, then
    /// Poison) and died with the Banemite at 8%; the Ice Golem, needed in no later battle, sat on its horn.
    /// </summary>
    [Fact]
    public void HurtAndWithAKeptBeastOutASpareIsWantedForTheBuster()
        => Assert.True(ArtemisCooldownRules.WantsSpareForBuster(3, busterSoon: true, beastOutIsKept: true, selfHpPct: 0.52f));

    [Fact]
    public void AHealthyBeastmasterTakesItAndTheBeastStays()
        => Assert.False(ArtemisCooldownRules.WantsSpareForBuster(3, true, true, selfHpPct: 0.97f));

    [Fact]
    public void NoSpareWithoutABusterOrOffADegreeOrWhenTheBeastOutCanBeSpentItself()
    {
        Assert.False(ArtemisCooldownRules.WantsSpareForBuster(3, busterSoon: false, true, 0.5f));
        Assert.False(ArtemisCooldownRules.WantsSpareForBuster(0, true, true, 0.5f));
        Assert.False(ArtemisCooldownRules.WantsSpareForBuster(3, true, beastOutIsKept: false, 0.5f));
    }

    // --- the Geshunpest's Scouring Ash at the Banemite (2026-11-15) ---
    // Korha's eight runs: a Deadly Thrust is 430-473, and the Poison after it ticks 81-96 every 3 s for 18 s (~515):
    // together two thirds of 1,470 HP. fuminpapa's and marurun's Third Degree clears cleanse it with an Ashkin's card.

    private const BeastClassification Ash = BeastClassification.Ashkin, Beast = BeastClassification.Beastkin;

    [Fact]
    public void TheGeshunpestAndTheIceGolemTradedBattles()
    {
        Assert.Equal([Wespe, IceGolem, Coeurl], CrucibleDegreeTeams.For(3, 2));
        Assert.Equal([Wespe, Geshunpest, Behemoth], CrucibleDegreeTeams.For(3, 4));
    }

    [Fact]
    public void TheBanemitesCardBeastIsTheGeshunpestOnADegreeOnly()
    {
        Assert.Equal(Geshunpest, CrucibleDegreeTeams.CardBeast(3, 4));
        Assert.Equal(Geshunpest, CrucibleDegreeTeams.CardBeast(1, 4));
        Assert.Null(CrucibleDegreeTeams.CardBeast(0, 4));
        Assert.Null(CrucibleDegreeTeams.CardBeast(3, 2));
        Assert.Null(CrucibleDegreeTeams.CardBeast(3, 6));
    }

    [Fact]
    public void TheCardBeastsSummonGoesOnBorrow()
        => Assert.True(ArtemisCooldownRules.BorrowsInPlaceOfRelease(true, Geshunpest, Geshunpest, Ash, BSTActions.BeastMode.ActionId));

    [Fact]
    public void NotOnceItsCardIsOnTheButtonNorForAnotherBeastNorWithBorrowOff()
    {
        Assert.False(ArtemisCooldownRules.BorrowsInPlaceOfRelease(true, Geshunpest, Geshunpest, Ash, BSTActions.ScouringAshId));
        Assert.False(ArtemisCooldownRules.BorrowsInPlaceOfRelease(true, Geshunpest, Behemoth, Beast, BSTActions.BeastMode.ActionId));
        Assert.False(ArtemisCooldownRules.BorrowsInPlaceOfRelease(true, null, Geshunpest, Ash, BSTActions.BeastMode.ActionId));
        Assert.False(ArtemisCooldownRules.BorrowsInPlaceOfRelease(false, Geshunpest, Geshunpest, Ash, BSTActions.BeastMode.ActionId));
        Assert.False(ArtemisCooldownRules.BorrowsInPlaceOfRelease(true, Geshunpest, null, null, BSTActions.BeastMode.ActionId));
    }

    /// <summary>The Behemoth is out at the second Thrust, a tankbuster is coming, she is under the skin line: Ash stays.</summary>
    [Fact]
    public void NoSkinReplacesAHeldScouringAsh()
    {
        Assert.True(ArtemisCooldownRules.KeepsKinshipCard(true, BSTActions.ScouringAshId, Beast, 0.5f, hitComing: true));
        Assert.True(ArtemisCooldownRules.KeepsKinshipCard(true, BSTActions.ScouringAshId, BeastClassification.Vilekin, 0.3f));
    }

    [Fact]
    public void AnAshkinMayGiveTheSameCardBackAndOffABoardNothingIsKept()
    {
        Assert.False(ArtemisCooldownRules.KeepsKinshipCard(true, BSTActions.ScouringAshId, Ash, 0.9f));
        Assert.False(ArtemisCooldownRules.KeepsKinshipCard(false, BSTActions.ScouringAshId, Beast, 0.9f));
    }

    [Fact]
    public void ScouringAshWaitsForSomethingToCleanse()
    {
        Assert.Equal(BeastModeUse.None, ArtemisCooldownRules.DecideBeastMode(true, BSTActions.ScouringAshId, 0.5f, 1f, false));
        Assert.Equal(BeastModeUse.Self, ArtemisCooldownRules.DecideBeastMode(true, BSTActions.ScouringAshId, 0.5f, 1f, true));
    }

    /// <summary>Beastskin went out at 3-32 s in every recorded fight, mostly while a beast's Cover took the hits.</summary>
    [Fact]
    public void BeastskinWaitsOutACoverOnABoard()
    {
        Assert.Equal(BeastModeUse.None,
            ArtemisCooldownRules.DecideBeastMode(true, BSTActions.BeastskinId, 1f, 1f, false, coveredOnBoard: true));
        Assert.Equal(BeastModeUse.Self,
            ArtemisCooldownRules.DecideBeastMode(true, BSTActions.BeastskinId, 1f, 1f, false, coveredOnBoard: false));
    }

    [Theory]
    [InlineData(BeastClassification.Ashkin, BSTActions.ScouringAshId)]
    [InlineData(BeastClassification.Beastkin, BSTActions.BeastskinId)]
    [InlineData(BeastClassification.Wavekin, BSTActions.QuellingWaveId)]
    [InlineData(BeastClassification.Soulkin, BSTActions.SoulCrushId)]
    public void EachKinGivesItsCard(BeastClassification kin, uint card)
        => Assert.Equal(card, ArtemisCooldownRules.KinshipCard(kin));

    private static int Spare(int outSlot, bool[] usable, bool[] spare, bool canAct = true, double sinceTry = 60)
        => ArtemisFamiliarKeeper.PickSpareHorn(true, canAct, sinceTry, outSlot, usable, spare);

    /// <summary>Banemite: Wespe, Ice Golem, Behemoth on the horns, the Behemoth out; the Ice Golem's horn is back.</summary>
    [Fact]
    public void TheReadyHornOfABeastThatCanBeSpentIsBlown()
        => Assert.Equal(1, Spare(outSlot: 2, usable: [true, true, false], spare: [false, true, false]));

    [Fact]
    public void AKeptBeastIsNeverTheSpare()
        => Assert.Equal(ArtemisFamiliarKeeper.None, Spare(outSlot: 2, usable: [true, false, false], spare: [false, true, false]));

    [Fact]
    public void NotWhileMovingNorRightAfterAnotherHorn()
    {
        Assert.Equal(ArtemisFamiliarKeeper.None, Spare(2, [true, true, false], [false, true, false], canAct: false));
        Assert.Equal(ArtemisFamiliarKeeper.None, Spare(2, [true, true, false], [false, true, false], sinceTry: 1));
    }
}
