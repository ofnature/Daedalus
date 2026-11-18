using Daedalus.Data;
using Daedalus.Rotation.ArtemisCore.Helpers;
using Xunit;

namespace Daedalus.Tests.Rotation.ArtemisCore;

/// <summary>
/// Quelling Wave on the Ogre's small wisps (user, 2026-11-07; TPLemont's D3 guide): one Wave kills one small wisp on
/// Third Degree, so in the Ogre fight the Wave is borrowed from the Megalocrab and pressed at them before they reach
/// the ward. Korha's run that day chased them with the axes instead and died.
/// </summary>
public sealed class ArtemisQuellingWaveWispTests
{
    private const ulong Hard = 10, Near = 20, Far = 30;

    [Fact]
    public void TheOgreFightOnABoardWantsTheWave()
    {
        Assert.True(ArtemisCooldownRules.WantsQuellingWaveForWisps(onBoard: true, ogreFight: true));
        Assert.False(ArtemisCooldownRules.WantsQuellingWaveForWisps(onBoard: true, ogreFight: false));
        Assert.False(ArtemisCooldownRules.WantsQuellingWaveForWisps(onBoard: false, ogreFight: true));
    }

    [Fact]
    public void AMegalocrabOutInTheOgreFightIsBorrowedFromForTheWave()
        => Assert.True(ArtemisCooldownRules.ShouldBorrowForJob(
            enabled: true, ready: true, BeastClassification.Wavekin, BSTActions.VileskinId,
            interruptPending: false,
            dispelPending: ArtemisCooldownRules.WantsQuellingWaveForWisps(true, true)));

    [Fact]
    public void NoReBorrowWhileTheWaveIsAlreadyOnTheButton()
        => Assert.False(ArtemisCooldownRules.ShouldBorrowForJob(
            true, true, BeastClassification.Wavekin, BSTActions.QuellingWaveId, false, dispelPending: true));

    [Fact]
    public void AnotherKinOutIsNotBorrowedFromForTheWave()
        => Assert.False(ArtemisCooldownRules.ShouldBorrowForJob(
            true, true, BeastClassification.Vilekin, BSTActions.QuellingWaveId, false, dispelPending: true));

    [Fact]
    public void TheWaveStaysOnTheButtonAfterTheMegalocrabLeaves()
        => Assert.True(ArtemisCooldownRules.KeepsKinshipCard(
            onBoard: true, BSTActions.QuellingWaveId, BeastClassification.Beastkin, selfHpPct: 0.8f));

    [Fact]
    public void TheHardTargetIsTakenWhenItIsASmallWisp()
        => Assert.Equal(Hard, ArtemisCooldownRules.PickWispForQuellingWave(
            true, BSTActions.QuellingWaveId, Hard, [(Near, 2f), (Hard, 12f)]));

    [Fact]
    public void ElseTheNearestSmallWisp()
        => Assert.Equal(Near, ArtemisCooldownRules.PickWispForQuellingWave(
            true, BSTActions.QuellingWaveId, hardTargetId: 99, [(Far, 20f), (Near, 4f)]));

    [Fact]
    public void WithoutTheWaveOnTheButtonNothingIsPicked()
        => Assert.Equal(0UL, ArtemisCooldownRules.PickWispForQuellingWave(
            true, BSTActions.SoulCrushId, Hard, [(Hard, 2f)]));

    [Fact]
    public void OffABoardOrWithNoWispsNothingIsPicked()
    {
        Assert.Equal(0UL, ArtemisCooldownRules.PickWispForQuellingWave(false, BSTActions.QuellingWaveId, Hard, [(Hard, 2f)]));
        Assert.Equal(0UL, ArtemisCooldownRules.PickWispForQuellingWave(true, BSTActions.QuellingWaveId, Hard, []));
    }

    [Fact]
    public void OnADegreeTheOgreFightKeepsAFamiliarUntilTheWispsAreGone()
    {
        Assert.True(ArtemisCooldownRules.OgreKeepsFamiliarForWisps(ogreFight: true, degree: 3, wispPhaseOver: false));
        Assert.False(ArtemisCooldownRules.OgreKeepsFamiliarForWisps(true, 3, wispPhaseOver: true));
        Assert.False(ArtemisCooldownRules.OgreKeepsFamiliarForWisps(true, degree: 0, false));
        Assert.False(ArtemisCooldownRules.OgreKeepsFamiliarForWisps(ogreFight: false, 3, false));
    }

    [Fact]
    public void TheWespeStingWaitsOnlyWhenNoHornCouldFollowIt()
    {
        Assert.True(ArtemisCooldownRules.OgreHoldsStingForWisps(true, 3, false, replacementReady: false));
        Assert.False(ArtemisCooldownRules.OgreHoldsStingForWisps(true, 3, false, replacementReady: true));
        Assert.False(ArtemisCooldownRules.OgreHoldsStingForWisps(true, 3, wispPhaseOver: true, replacementReady: false));
    }

    [Fact]
    public void TheGreatWispIsBlownAsSoonAsTheBlowWouldFinishIt()
    {
        Assert.True(ArtemisCooldownRules.ShouldPartingBlowGreatWisp(true, true, true, greatWispHp: 600, hasVantage: false));
        Assert.False(ArtemisCooldownRules.ShouldPartingBlowGreatWisp(true, true, true, greatWispHp: 900, hasVantage: false));
        Assert.True(ArtemisCooldownRules.ShouldPartingBlowGreatWisp(true, true, true, greatWispHp: 900, hasVantage: true));
    }

    [Fact]
    public void ABlowThatCannotKillItIsNeverSpent()
    {
        // The familiar is worth more out: Minerva counts the blast down as a raidwide, so its Cover can take it.
        Assert.False(ArtemisCooldownRules.ShouldPartingBlowGreatWisp(true, true, true, greatWispHp: 3559, hasVantage: false));
        Assert.False(ArtemisCooldownRules.ShouldPartingBlowGreatWisp(true, true, true, greatWispHp: 1200, hasVantage: true));
    }

    [Fact]
    public void TheWispBlastsCountAsCoverableRaidwides()
    {
        Assert.Equal(5f, ArtemisCooldownRules.CoverableRaidwideSeconds(5f, 46919));
        Assert.Equal(5f, ArtemisCooldownRules.CoverableRaidwideSeconds(5f, 49728));
    }

    [Fact]
    public void NoBlowWithoutAFamiliarOrTheBlowReadyOrOffABoard()
    {
        Assert.False(ArtemisCooldownRules.ShouldPartingBlowGreatWisp(true, familiarOut: false, true, 100, false));
        Assert.False(ArtemisCooldownRules.ShouldPartingBlowGreatWisp(true, true, blowReady: false, 100, false));
        Assert.False(ArtemisCooldownRules.ShouldPartingBlowGreatWisp(onBoard: false, true, true, 100, false));
        Assert.False(ArtemisCooldownRules.ShouldPartingBlowGreatWisp(true, true, true, greatWispHp: 0, false));
    }
}
