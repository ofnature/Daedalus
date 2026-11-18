using System.Linq;
using Daedalus.Data;
using Daedalus.Rotation.ArtemisCore.Helpers;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>
/// The second Third Degree plan (user, 2026-11-16: "mock up a version using his setup ... I just want to have an
/// option"): TPLemont's First Board guide (docs/crucible-research/tplemont-d3-guides.md) beside fuurie's, picked in
/// the Crucible Team window. Every lookup names its plan here: <see cref="CrucibleDegreeTeams.Active"/> is a
/// production static and no test assigns it.
/// </summary>
public class CrucibleDegreePlanTests
{
    private const CrucibleDegreePlan Video = CrucibleDegreePlan.TpLemont, Sting = CrucibleDegreePlan.Fuurie;

    // Bestiary numbers.
    private const int OpoOpo = 5, Megalocrab = 9, Wespe = 10, Geshunpest = 13, Puk = 14, Mantis = 16, FlyingTrap = 20, Ziz = 21,
        Uragnite = 27, Spriggan = 29, Hydra = 43, Damselfly = 44, Behemoth = 50;

    [Fact]
    public void TheStingPlanIsTheDefault()
        => Assert.Equal(Sting, CrucibleDegreeTeams.Active);

    [Theory]
    [InlineData(1, new[] { Mantis, OpoOpo, Spriggan })]
    [InlineData(3, new[] { Megalocrab, Damselfly, Mantis })]
    [InlineData(4, new[] { Uragnite, FlyingTrap, Ziz })]
    [InlineData(5, new[] { Damselfly, Megalocrab, Uragnite })]
    [InlineData(6, new[] { Puk, Spriggan, Hydra })]
    public void TheVideosTeamsInItsHornOrder(int battle, int[] team)
        => Assert.Equal(team, CrucibleDegreeTeams.For(3, battle, Video));

    [Fact]
    public void TheVideoPlanHasNoWespeAndNoArchDemonTeam()
    {
        Assert.Null(CrucibleDegreeTeams.For(3, 2, Video));
        foreach (var battle in new[] { 1, 3, 4, 5, 6 })
            Assert.DoesNotContain(Wespe, CrucibleDegreeTeams.For(3, battle, Video)!);
    }

    /// <summary>The First Board holds ten: the video's roster is exactly its five teams' beasts.</summary>
    [Fact]
    public void TheVideosRosterIsItsTenBeasts()
    {
        var roster = CrucibleDegreeTeams.Roster(3, 1, _ => true, Video);
        Assert.Equal(10, roster.Count);
        Assert.Equal(10, roster.Distinct().Count());
        foreach (var battle in new[] { 1, 3, 4, 5, 6 })
            Assert.All(CrucibleDegreeTeams.For(3, battle, Video)!, no => Assert.Contains(no, roster));
    }

    [Fact]
    public void TheVideoPlanTakesThePiscodemonsSideOfTheFork()
        => Assert.Equal(3, CrucibleDegreeTeams.PlannedBattle(3, [2, 3], Video));

    [Fact]
    public void TheStingPlanLeavesTheForkToTheWalker()
    {
        Assert.Null(CrucibleDegreeTeams.PlannedBattle(3, [2, 3], Sting)); // a team for both: the Arch Demon wins on its own ranking
        Assert.Null(CrucibleDegreeTeams.PlannedBattle(0, [2, 3], Video)); // Standard: no plan
        Assert.Null(CrucibleDegreeTeams.PlannedBattle(3, [4], Video));    // no fork
    }

    /// <summary>The video: "Trap and Ziz are both sacrificed here"; the Uragnite comes back for the Ogre.</summary>
    [Fact]
    public void AtTheBanemiteTheTrapAndTheZizCanBeSpentAndTheUragniteIsKept()
    {
        var later = CrucibleDegreeTeams.NeededLater(3, 1, 4, Video);
        Assert.DoesNotContain(FlyingTrap, later);
        Assert.DoesNotContain(Ziz, later);
        Assert.Contains(Uragnite, later);
    }

    [Fact]
    public void AtTheBonePairTheMantisAndTheSprigganAreKeptAndTheOpoOpoSpent()
    {
        var later = CrucibleDegreeTeams.NeededLater(3, 1, 1, Video);
        Assert.Contains(Mantis, later);   // the Piscodemon
        Assert.Contains(Spriggan, later); // Pas de Seul
        Assert.DoesNotContain(OpoOpo, later);
    }

    [Fact]
    public void AtTheOgreOnlyPasDeSeulsBeastsAreKept()
        => Assert.Equal([Puk, Spriggan, Hydra], CrucibleDegreeTeams.NeededLater(3, 1, 5, Video));

    /// <summary>A fork walked the wrong way round ends at the Arch Demon: the later battles' beasts are still kept.</summary>
    [Fact]
    public void AtTheArchDemonAfterAllTheLaterBeastsAreStillKept()
        => Assert.Contains(Uragnite, CrucibleDegreeTeams.NeededLater(3, 1, 2, Video));

    [Theory]
    [InlineData(1, Spriggan)]   // Soul Crush
    [InlineData(3, Megalocrab)] // Quelling Wave, from the first horn
    [InlineData(4, Uragnite)]   // Quelling Wave, from the first horn
    [InlineData(5, Uragnite)]   // Quelling Wave for the wisps
    [InlineData(6, Puk)]        // Scaleskin, from the first horn
    public void TheVideoPlanNamesTheBeastBorrowedFromBeforeEachPull(int battle, int beast)
        => Assert.Equal(beast, CrucibleDegreeTeams.PreBorrowBeast(3, battle, Video));

    [Fact]
    public void TheStingPlanKeepsTheKinRuleAndItsScouringAsh()
    {
        Assert.Null(CrucibleDegreeTeams.PreBorrowBeast(3, 4, Sting));
        Assert.Equal(Geshunpest, CrucibleDegreeTeams.CardBeast(3, 4, Sting));
        Assert.Null(CrucibleDegreeTeams.CardBeast(3, 4, Video));
        Assert.Null(CrucibleDegreeTeams.BusterBeast(3, 4, Sting));
        Assert.False(CrucibleDegreeTeams.LeavesAtOnce(3, 4, Behemoth, Sting));
    }

    [Fact]
    public void KeptBeastsThatAreDoneHereLeaveAtOnce()
    {
        Assert.True(CrucibleDegreeTeams.LeavesAtOnce(3, 4, Uragnite, Video));
        Assert.True(CrucibleDegreeTeams.LeavesAtOnce(3, 3, Megalocrab, Video));
        Assert.True(CrucibleDegreeTeams.LeavesAtOnce(3, 3, Damselfly, Video));
        Assert.True(CrucibleDegreeTeams.LeavesAtOnce(3, 1, Mantis, Video));
        Assert.False(CrucibleDegreeTeams.LeavesAtOnce(3, 4, Ziz, Video));
        Assert.False(CrucibleDegreeTeams.LeavesAtOnce(3, 5, Uragnite, Video)); // the Ogre: it fights there
        Assert.False(CrucibleDegreeTeams.LeavesAtOnce(0, 4, Uragnite, Video));
    }

    [Fact]
    public void ABeastLeavesOnceAFightAndOnlyWithAnotherHornReady()
    {
        Assert.True(ArtemisCooldownRules.LeavesNow(onBoard: true, planSaysLeave: true, alreadyLeftThisFight: false, replacementReady: true));
        Assert.False(ArtemisCooldownRules.LeavesNow(true, true, alreadyLeftThisFight: true, true)); // the Uragnite, back for the end
        Assert.False(ArtemisCooldownRules.LeavesNow(true, true, false, replacementReady: false));
        Assert.False(ArtemisCooldownRules.LeavesNow(true, planSaysLeave: false, false, true));
        Assert.False(ArtemisCooldownRules.LeavesNow(onBoard: false, true, false, true));
    }

    /// <summary>The video: "keep it alive until the next tankbuster, then Ziz Snarls into it".</summary>
    [Fact]
    public void TheZizIsNotRetreatedUntilTheSecondDeadlyThrustHasLanded()
    {
        Assert.Equal((Ziz, 2), CrucibleDegreeTeams.BusterBeast(3, 4, Video));
        Assert.True(ArtemisCooldownRules.HoldsBusterBeast(Ziz, Ziz, bustersCast: 1, bustersWanted: 2, busterCasting: false));
        Assert.True(ArtemisCooldownRules.HoldsBusterBeast(Ziz, Ziz, 2, 2, busterCasting: true));   // the second, still being cast
        Assert.False(ArtemisCooldownRules.HoldsBusterBeast(Ziz, Ziz, 2, 2, busterCasting: false)); // landed: it may go
        Assert.False(ArtemisCooldownRules.HoldsBusterBeast(Ziz, FlyingTrap, 0, 2, false));
        Assert.False(ArtemisCooldownRules.HoldsBusterBeast(null, Ziz, 0, 0, false));
    }

    [Fact]
    public void TheBeastmasterTakesTheEnemyOffALowTankbusterBeast()
        => Assert.True(ArtemisCooldownRules.ChallengesForBusterBeast(
            true, true, heldForBuster: true, enemyOnFamiliar: true, familiarHpPct: 0.3f, fallingLine: 0.35f, selfHpPct: 0.8f,
            secondsUntilTankbuster: 40f));

    [Fact]
    public void NotWhileItIsHealthyNorWithTheTankbusterComingNorWhenSheIsCritical()
    {
        Assert.False(ArtemisCooldownRules.ChallengesForBusterBeast(true, true, true, true, familiarHpPct: 0.6f, 0.35f, 0.8f, 40f));
        Assert.False(ArtemisCooldownRules.ChallengesForBusterBeast(true, true, true, true, 0.3f, 0.35f, 0.8f, secondsUntilTankbuster: 5f));
        Assert.False(ArtemisCooldownRules.ChallengesForBusterBeast(true, true, true, true, 0.3f, 0.35f, selfHpPct: 0.25f, 40f));
        Assert.False(ArtemisCooldownRules.ChallengesForBusterBeast(true, true, heldForBuster: false, true, 0.3f, 0.35f, 0.8f, 40f));
        Assert.False(ArtemisCooldownRules.ChallengesForBusterBeast(true, true, true, enemyOnFamiliar: false, 0.3f, 0.35f, 0.8f, 40f));
    }

    /// <summary>The video: "Ziz Tempered Release petrifies the adds" — held for the Mitelings like the Behemoth's Meteor.</summary>
    [Fact]
    public void TheZizsReleaseWaitsForTheMitelingsLikeTheMeteor()
    {
        Assert.True(ArtemisCooldownRules.ReleaseIsForAdds(Ziz));
        Assert.True(ArtemisCooldownRules.ReleaseIsForAdds(Behemoth));
        Assert.False(ArtemisCooldownRules.ReleaseIsForAdds(FlyingTrap));
        Assert.True(ArtemisCooldownRules.HoldMeteorForMitelings(
            onBoard: true, Ziz, banemiteHpPct: 0.9f, mitelingsUp: 0, mitelingsBunched: false, secondsSinceMitelingsUp: 0,
            secondsIn: 30, familiarFalling: false));
    }

    /// <summary>Borrowed from the first horn itself, that beast is not called again: a second summon wipes its Kinship.</summary>
    [Fact]
    public void ACardFromTheFirstHornLeavesItOut()
    {
        const int first = 0;
        Assert.Equal(ArtemisFamiliarKeeper.PreBorrowStep.Borrow,
            ArtemisFamiliarKeeper.NextPreBorrowStep(true, true, preSlot: first, outSlot: first, cardHeld: false, borrowReady: true));
        Assert.Equal(ArtemisFamiliarKeeper.PreBorrowStep.None,
            ArtemisFamiliarKeeper.NextPreBorrowStep(true, true, preSlot: first, outSlot: first, cardHeld: true, borrowReady: true));
        Assert.Equal(ArtemisFamiliarKeeper.PreBorrowStep.SummonBeast,
            ArtemisFamiliarKeeper.NextPreBorrowStep(true, true, preSlot: first, outSlot: ArtemisFamiliarKeeper.None, cardHeld: false, borrowReady: true));
        // From a later horn, as before: the first horn is called back.
        Assert.Equal(ArtemisFamiliarKeeper.PreBorrowStep.SummonOpener,
            ArtemisFamiliarKeeper.NextPreBorrowStep(true, true, preSlot: 2, outSlot: 2, cardHeld: true, borrowReady: true));
    }
}
