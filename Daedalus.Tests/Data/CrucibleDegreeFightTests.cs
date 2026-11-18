using System.Linq;
using Daedalus.Data;
using Daedalus.Rotation.ArtemisCore.Helpers;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>
/// Third Degree fights (user, 2026-11-06): the Wespe stings in every fight and at once, the Bone Knight's guard plan
/// (the Wespe holds him, Challenge turns him back, sting from behind), the documented teams' weakness coverage, and food
/// before a run.
/// </summary>
public class CrucibleDegreeFightTests
{
    // --- Final Sting on a degree ---

    [Fact]
    public void OnStandardTheFirstBoardStingsOnlyAtTheOgreAndTheBoss()
    {
        Assert.False(ArtemisCooldownRules.WespeStingFight(CrucibleAutoPilotRules.FirstBoardIndex, false, false));
        Assert.True(ArtemisCooldownRules.WespeStingFight(CrucibleAutoPilotRules.FirstBoardIndex, true, false));
        Assert.True(ArtemisCooldownRules.WespeStingFight(CrucibleAutoPilotRules.FirstBoardIndex, false, true));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void OnADegreeEveryFirstBoardFightStings(int degree)
        => Assert.True(ArtemisCooldownRules.WespeStingFight(CrucibleAutoPilotRules.FirstBoardIndex, false, false, degree));

    [Fact]
    public void OnlyADegreeStingsAtOnce()
    {
        Assert.False(ArtemisCooldownRules.StingsAtOnce(0));
        Assert.True(ArtemisCooldownRules.StingsAtOnce(1));
        Assert.True(ArtemisCooldownRules.StingsAtOnce(3));
    }

    // --- The Bone Knight fight: the Wespe stings at once and comes back for him ---

    /// <summary>Its horn back, the Wespe is brought out again only once his Forward Guard has been seen.</summary>
    [Fact]
    public void TheWespeComesBackOnceHisGuardHasBeenSeen()
    {
        Assert.False(ArtemisCooldownRules.CyclesToWespeInKnightFight(knightFight: true, guardSeen: false));
        Assert.True(ArtemisCooldownRules.CyclesToWespeInKnightFight(knightFight: true, guardSeen: true));
        Assert.True(ArtemisCooldownRules.CyclesToWespeInKnightFight(knightFight: false, guardSeen: false));
    }

    // --- Weakness coverage ---

    /// <summary>The First Board's lead enemies' weaknesses (XBMBattleDetail, docs/crucible-research/client-enemy-moves.md).</summary>
    [Theory]
    [InlineData(1, CrucibleElement.Blunt)]     // bone knight + bishop
    [InlineData(2, CrucibleElement.Lightning)] // arch demon
    [InlineData(3, CrucibleElement.Slashing)]  // piscodemon
    // The Banemite (Ice) has no row: its Ice Golem went to the Arch Demon for the Geshunpest's Scouring Ash (2026-11-15).
    [InlineData(5, CrucibleElement.Wind)]      // ogre
    [InlineData(6, CrucibleElement.Piercing)]  // Pas de Seul
    public void EveryDocumentedTeamHitsItsEnemysWeakness(int battle, CrucibleElement weakness)
        => Assert.Contains(CrucibleDegreeTeams.For(3, battle)!, no => BstFamiliarAttacks.Of(no) == weakness);

    // --- Food before a run ---

    /// <summary>Korha, 2026-11-15: eight runs begun with Start run, none Well Fed — the meal was a step of the loop's walk only.</summary>
    [Fact]
    public void FoodIsEatenStandingInTheLobbyWithNothingOpen()
        => Assert.True(CrucibleLoopRules.EatsInLobbyNow(enabled: true, entering: false, entryScreenOpen: false, yalmsToLauda: 5f));

    [Fact]
    public void NotWhileLaudasScreensAreOpenNorWhileEnteringNorAwayFromHer()
    {
        Assert.False(CrucibleLoopRules.EatsInLobbyNow(true, entering: false, entryScreenOpen: true, 5f));
        Assert.False(CrucibleLoopRules.EatsInLobbyNow(true, entering: true, entryScreenOpen: false, 5f));
        Assert.False(CrucibleLoopRules.EatsInLobbyNow(true, false, false, yalmsToLauda: null));
        Assert.False(CrucibleLoopRules.EatsInLobbyNow(true, false, false, CrucibleLoopRules.LobbyRange + 1f));
        Assert.False(CrucibleLoopRules.EatsInLobbyNow(enabled: false, false, false, 5f));
    }

    [Fact]
    public void TheHqMealIsEatenFirstAndAgainWhenItIsTheOnlyOne()
    {
        Assert.True(CrucibleLoopRules.EatsHq(hqHeld: 138, nqHeld: 0, tryNumber: 1));
        Assert.True(CrucibleLoopRules.EatsHq(hqHeld: 138, nqHeld: 0, tryNumber: 2));
        Assert.False(CrucibleLoopRules.EatsHq(hqHeld: 3, nqHeld: 3, tryNumber: 2));
        Assert.False(CrucibleLoopRules.EatsHq(hqHeld: 0, nqHeld: 3, tryNumber: 1));
    }

    [Fact]
    public void NoFoodChosenEatsNothing()
        => Assert.Equal(CrucibleLoopRules.FoodStep.None, CrucibleLoopRules.NextFood(0, 0, 3, 3, 0));

    [Fact]
    public void WellFedLongEnoughEatsNothing()
        => Assert.Equal(CrucibleLoopRules.FoodStep.None,
            CrucibleLoopRules.NextFood(4650, CrucibleLoopRules.FoodRefreshBelowSeconds, 3, 3, 0));

    [Fact]
    public void LowOrMissingWellFedEatsHqFirst()
    {
        Assert.Equal(CrucibleLoopRules.FoodStep.EatHq, CrucibleLoopRules.NextFood(4650, 0, 1, 5, 0));
        Assert.Equal(CrucibleLoopRules.FoodStep.EatNq, CrucibleLoopRules.NextFood(4650, 300, 0, 5, 0));
    }

    [Fact]
    public void NoneInTheBagsGoesInWithout()
        => Assert.Equal(CrucibleLoopRules.FoodStep.NoneHeld, CrucibleLoopRules.NextFood(4650, 0, 0, 0, 0));

    [Fact]
    public void GivesUpAfterTheTries()
        => Assert.Equal(CrucibleLoopRules.FoodStep.None,
            CrucibleLoopRules.NextFood(4650, 0, 1, 1, CrucibleLoopRules.FoodTriesPerRun));

    // --- The Behemoth's Meteor on the Banemite's Mitelings ---

    private static bool Hold(int? beast = ArtemisCooldownRules.BehemothBestiaryNo, float? banemite = 0.6f, int up = 0,
        bool bunched = false, double sinceUp = 0, double secondsIn = 40, bool falling = false, bool onBoard = true)
        => ArtemisCooldownRules.HoldMeteorForMitelings(onBoard, beast, banemite, up, bunched, sinceUp, secondsIn, falling);

    [Theory]
    [InlineData(0, 56)]
    [InlineData(40, 16)]
    [InlineData(56, 57)]
    [InlineData(100, 13)]
    [InlineData(113, 57)]
    public void TheMitelingsComeOnATimer(double secondsIn, double toNext)
        => Assert.Equal(toNext, ArtemisCooldownRules.SecondsToNextMitelingWave(secondsIn), 3);

    [Fact]
    public void MeteorWaitsForAWaveDueSoon()
    {
        Assert.True(Hold(secondsIn: 5));    // the first wave is always waited for
        Assert.False(Hold(secondsIn: 70));  // the second is 43 s off: Meteor goes at the Banemite
        Assert.True(Hold(secondsIn: 90));   // the second wave in 23 s
    }

    [Fact]
    public void MeteorGoesOnceTheMitelingsHaveClosedIn()
    {
        Assert.True(Hold(up: 3, sinceUp: 2, secondsIn: 58));
        Assert.False(Hold(up: 3, bunched: true, sinceUp: 2, secondsIn: 58));
        Assert.False(Hold(up: 3, sinceUp: ArtemisCooldownRules.MeteorWaitsForMitelingsSeconds, secondsIn: 65)); // waited long enough
    }

    /// <summary>
    /// A Miteling stands still for 4.9 s after it appears and has gathered by 6-7 s: at 6 s (the old limit) Meteor went
    /// out with them 10 to 21 yalms away (user, 2026-11-15).
    /// </summary>
    [Fact]
    public void MeteorStillWaitsWhileTheMitelingsAreWalkingIn()
    {
        Assert.True(Hold(up: 3, sinceUp: 6, secondsIn: 62));
        Assert.True(Hold(up: 3, sinceUp: 8.5, secondsIn: 64.5));
        Assert.Equal(10f, ArtemisCooldownRules.MitelingsBunchedYalms);
    }

    [Fact]
    public void MeteorFinishesANearlyDeadBanemite()
        => Assert.False(Hold(banemite: 0.15f, secondsIn: 50));

    [Fact]
    public void OnlyTheBehemothOnABoardInTheBanemiteFightHolds()
    {
        Assert.False(Hold(beast: ArtemisCooldownRules.MantisBestiaryNo, secondsIn: 50));
        Assert.False(Hold(banemite: null, secondsIn: 50));
        Assert.False(Hold(onBoard: false, secondsIn: 50));
    }

    [Fact]
    public void AFallingBehemothReleasesBeforeItRetreats()
        => Assert.False(Hold(falling: true, secondsIn: 50));

    // --- Ymir Plan A ---

    private const int WespeNo = 10, IceGolemNo = ArtemisCooldownRules.IceGolemBestiaryNo, DiremiteNo = ArtemisCooldownRules.DiremiteBestiaryNo;

    [Fact]
    public void TheWespeStingsTheShell()
    {
        Assert.Equal(ArtemisCooldownRules.YmirRelease.AtShell, ArtemisCooldownRules.YmirReleasePlan(WespeNo, shelled: true));
        Assert.Equal(ArtemisCooldownRules.YmirRelease.Normal, ArtemisCooldownRules.YmirReleasePlan(WespeNo, shelled: false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheIceGolemsReleaseIsNeverUsed(bool shelled)
        => Assert.Equal(ArtemisCooldownRules.YmirRelease.Never, ArtemisCooldownRules.YmirReleasePlan(IceGolemNo, shelled));

    [Fact]
    public void TheDiremiteBindsTheYmirOnceItsShellBreaks()
    {
        Assert.Equal(ArtemisCooldownRules.YmirRelease.Hold, ArtemisCooldownRules.YmirReleasePlan(DiremiteNo, shelled: true));
        Assert.Equal(ArtemisCooldownRules.YmirRelease.AtYmir, ArtemisCooldownRules.YmirReleasePlan(DiremiteNo, shelled: false));
        Assert.Equal(ArtemisCooldownRules.YmirRelease.Normal, ArtemisCooldownRules.YmirReleasePlan(ArtemisCooldownRules.MantisBestiaryNo, false));
    }

    [Fact]
    public void TheIceGolemPartingBlowsTheYmirWhenTheShellBreaks()
    {
        Assert.False(ArtemisCooldownRules.YmirPartingBlowNow(IceGolemNo, shelled: true));
        Assert.True(ArtemisCooldownRules.YmirPartingBlowNow(IceGolemNo, shelled: false));
        Assert.False(ArtemisCooldownRules.YmirPartingBlowNow(DiremiteNo, shelled: false));
        Assert.True(ArtemisCooldownRules.YmirHoldsPartingBlow(IceGolemNo, shelled: true, falling: false));
        Assert.False(ArtemisCooldownRules.YmirHoldsPartingBlow(IceGolemNo, shelled: true, falling: true)); // it still retreats
    }

    [Fact]
    public void NoInstinctChainOnTheShell()
    {
        Assert.True(ArtemisCooldownRules.HoldsInstinctsAtYmirShell(ymirFight: true, shelled: true));
        Assert.False(ArtemisCooldownRules.HoldsInstinctsAtYmirShell(ymirFight: true, shelled: false));
        Assert.False(ArtemisCooldownRules.HoldsInstinctsAtYmirShell(ymirFight: false, shelled: true));
    }

    [Fact]
    public void TheFamiliarHoldsTheSahagin()
    {
        Assert.True(ArtemisCooldownRules.SnarlsTheSahagin(true, familiarOut: true, sahaginOnFamiliar: false, snarlReady: true));
        Assert.False(ArtemisCooldownRules.SnarlsTheSahagin(true, familiarOut: true, sahaginOnFamiliar: true, snarlReady: true));
        Assert.False(ArtemisCooldownRules.SnarlsTheSahagin(true, familiarOut: false, sahaginOnFamiliar: false, snarlReady: true));
        Assert.False(ArtemisCooldownRules.SnarlsTheSahagin(false, familiarOut: true, sahaginOnFamiliar: false, snarlReady: true));
    }

    [Fact]
    public void TheYmirTeamIsPlanA()
        => Assert.Equal([WespeNo, IceGolemNo, DiremiteNo], CrucibleDegreeTeams.For(3, 15));
}
