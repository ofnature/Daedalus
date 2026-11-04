using Daedalus.Data;
using Daedalus.Rotation.ArtemisCore.Helpers;
using Xunit;

namespace Daedalus.Tests.Rotation.ArtemisCore;

/// <summary>
/// Soul Crush kept for interrupts and Quelling Wave pressed at buffs to strip (2026-11-02): the Second Master's Board
/// needs both — the Lamias' Raise (Game8, the Medusa replay) and the Drake's Blaze Spikes (fuurie's D3 clear).
/// </summary>
public class ArtemisKinshipJobTests
{
    private const BeastClassification Soul = BeastClassification.Soulkin;
    private const BeastClassification Wave = BeastClassification.Wavekin;
    private const BeastClassification Beast = BeastClassification.Beastkin;

    // ── what counts as a cast to interrupt ──────────────────────────────────────────────

    [Fact]
    public void AnInterruptibleCastOnABoardCountsWithoutTheEngine()
        => Assert.True(ArtemisCooldownRules.IsInterruptCandidate(false, onBoard: true, casting: true, interruptible: true, 5f));

    [Fact]
    public void OffABoardOnlyTheEngineDecides()
    {
        Assert.False(ArtemisCooldownRules.IsInterruptCandidate(false, onBoard: false, casting: true, interruptible: true, 5f));
        Assert.True(ArtemisCooldownRules.IsInterruptCandidate(true, onBoard: false, casting: true, interruptible: false, 5f));
    }

    [Fact]
    public void ACastAboutToFinishOrNotInterruptibleDoesNotCount()
    {
        Assert.False(ArtemisCooldownRules.IsInterruptCandidate(false, true, true, true, ArtemisCooldownRules.InterruptMinCastLeftSeconds));
        Assert.False(ArtemisCooldownRules.IsInterruptCandidate(false, true, true, interruptible: false, 5f));
        Assert.False(ArtemisCooldownRules.IsInterruptCandidate(true, true, casting: false, true, 5f));
    }

    // ── Borrow for the job ─────────────────────────────────────────────────────────────

    [Fact]
    public void ACastToInterruptWithASoulkinOutBorrowsAtOnce()
        => Assert.True(ArtemisCooldownRules.ShouldBorrowForJob(true, true, Soul, BSTActions.BeastskinId, interruptPending: true, false));

    [Fact]
    public void ABuffToStripWithAWavekinOutBorrowsAtOnce()
        => Assert.True(ArtemisCooldownRules.ShouldBorrowForJob(true, true, Wave, 0, false, dispelPending: true));

    [Fact]
    public void NoUrgentBorrowWhenTheButtonAlreadyHoldsIt()
    {
        Assert.False(ArtemisCooldownRules.ShouldBorrowForJob(true, true, Soul, BSTActions.SoulCrushId, true, false));
        Assert.False(ArtemisCooldownRules.ShouldBorrowForJob(true, true, Wave, BSTActions.QuellingWaveId, false, true));
    }

    [Fact]
    public void NoUrgentBorrowFromTheWrongKinOrWithBorrowOff()
    {
        Assert.False(ArtemisCooldownRules.ShouldBorrowForJob(true, true, Beast, 0, true, true));
        Assert.False(ArtemisCooldownRules.ShouldBorrowForJob(true, true, Wave, 0, interruptPending: true, false));
        Assert.False(ArtemisCooldownRules.ShouldBorrowForJob(false, true, Soul, 0, true, false));
        Assert.False(ArtemisCooldownRules.ShouldBorrowForJob(true, ready: false, Soul, 0, true, false));
        Assert.False(ArtemisCooldownRules.ShouldBorrowForJob(true, true, null, 0, true, true));
    }

    // ── keeping the card ───────────────────────────────────────────────────────────────

    [Fact]
    public void AnUnusedSoulCrushOrQuellingWaveIsNotBorrowedOverByAnotherKin()
    {
        Assert.True(ArtemisCooldownRules.KeepsKinshipCard(true, BSTActions.SoulCrushId, Beast, 0.9f));
        Assert.True(ArtemisCooldownRules.KeepsKinshipCard(true, BSTActions.QuellingWaveId, BeastClassification.Seedkin, 0.9f));
    }

    [Fact]
    public void TheCardGoesWhenLowOffABoardOrTheSameKinGivesItBack()
    {
        Assert.False(ArtemisCooldownRules.KeepsKinshipCard(true, BSTActions.SoulCrushId, BeastClassification.Seedkin, 0.4f));
        Assert.False(ArtemisCooldownRules.KeepsKinshipCard(false, BSTActions.SoulCrushId, Beast, 0.9f));
        Assert.False(ArtemisCooldownRules.KeepsKinshipCard(true, BSTActions.SoulCrushId, Soul, 0.9f));
        Assert.False(ArtemisCooldownRules.KeepsKinshipCard(true, BSTActions.BeastskinId, Beast, 0.9f));
    }

    [Fact]
    public void ADefensiveSkinWinsOverAHeldCardWhenAHitIsComing()
    {
        Assert.False(ArtemisCooldownRules.KeepsKinshipCard(true, BSTActions.SoulCrushId, Beast, 0.95f, hitComing: true));
        Assert.False(ArtemisCooldownRules.KeepsKinshipCard(true, BSTActions.QuellingWaveId, BeastClassification.Scalekin, 0.95f, hitComing: true));
    }

    [Fact]
    public void ADefensiveSkinWinsOverAHeldCardUnderTheSkinLine()
        => Assert.False(ArtemisCooldownRules.KeepsKinshipCard(true, BSTActions.SoulCrushId, BeastClassification.Vilekin, 0.7f));

    [Fact]
    public void ANonDefensiveKinStillWaitsWhenAHitIsComing()
        => Assert.True(ArtemisCooldownRules.KeepsKinshipCard(true, BSTActions.SoulCrushId, BeastClassification.Seedkin, 0.95f, hitComing: true));

    [Fact]
    public void SoulCrushIsSpentAsDamageOnlyWhenBorrowWouldGiveItBack()
    {
        Assert.True(ArtemisCooldownRules.HoldsSoulCrush(true, borrowReady: false, Soul));
        Assert.True(ArtemisCooldownRules.HoldsSoulCrush(true, borrowReady: true, Beast));
        Assert.False(ArtemisCooldownRules.HoldsSoulCrush(true, borrowReady: true, Soul));
        Assert.False(ArtemisCooldownRules.HoldsSoulCrush(false, false, null));
    }

    [Fact]
    public void AHeldSoulCrushIsNotPressedAsDamage()
    {
        Assert.Equal(BeastModeUse.None, ArtemisCooldownRules.DecideBeastMode(true, BSTActions.SoulCrushId, 1f, 1f, false, holdSoulCrush: true));
        Assert.Equal(BeastModeUse.Enemy, ArtemisCooldownRules.DecideBeastMode(true, BSTActions.SoulCrushId, 1f, 1f, false));
    }

    // ── whom to dispel ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TheHardTargetIsDispelledFirstElseTheNearest()
    {
        Assert.Equal(5UL, ArtemisCooldownRules.PickDispelTarget(5, [(4UL, 2f), (5UL, 20f)]));
        Assert.Equal(4UL, ArtemisCooldownRules.PickDispelTarget(9, [(6UL, 15f), (4UL, 2f)]));
        Assert.Equal(0UL, ArtemisCooldownRules.PickDispelTarget(9, []));
    }
}
