using System;
using Daedalus.Data;
using Daedalus.Rotation.ArtemisCore.Helpers;
using Xunit;

namespace Daedalus.Tests.Rotation.ArtemisCore;

/// <summary>
/// Chain building from TPLemontFFXIV's combo guide (2026-11-07): intentional and instinctual links alternate, a held
/// link must not let a live chain lapse, and the level 50 infinity line runs pet → you → Cheer → pet → Rally → finisher.
/// </summary>
public sealed class ArtemisComboChainRulesTests
{
    private static (ArtemisInstinctTracker tracker, Action<float> advance) Clocked()
    {
        var now = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
        var tracker = new ArtemisInstinctTracker { UtcNow = () => now };
        return (tracker, seconds => now = now.AddSeconds(seconds));
    }

    private static ArtemisGauge Gauge(int mastered, int natural, InstinctAffinity lit = InstinctAffinity.None)
        => new(Tp: 0, PetTp: 0, PetTpAtLastUse: 0, ActiveBattlehorn: 0, ComboState: 0, Affinity: lit, ChainCount: 0,
            KinshipBattlehorn: 0, KinshipClassification: 0, NaturalInstinct: natural, MasteredInstinct: mastered);

    // --- Two intentional combos can't come back to back ---

    [Fact]
    public void AfterAnIntentionalLinkTheNextCannotBeIntentional()
    {
        var (tracker, _) = Clocked();
        tracker.Record(InstinctAffinity.Volant);
        tracker.Record(InstinctAffinity.Rampant); // clockwise: intentional

        Assert.True(tracker.LastWasIntentional);
        Assert.Equal(InstinctAffinity.None, tracker.PreferredNext);
        Assert.False(tracker.WouldBeIntentional(InstinctAffinity.Durant));
        Assert.True(tracker.WouldCombo(InstinctAffinity.Durant)); // still extends the chain
    }

    [Fact]
    public void AnInstinctualLinkOpensTheNextIntentionalAgain()
    {
        var (tracker, _) = Clocked();
        tracker.Record(InstinctAffinity.Volant);
        tracker.Record(InstinctAffinity.Rampant); // intentional
        tracker.Record(InstinctAffinity.Rampant); // instinctual

        Assert.False(tracker.LastWasIntentional);
        Assert.Equal(InstinctAffinity.Durant, tracker.PreferredNext);
        Assert.True(tracker.WouldBeIntentional(InstinctAffinity.Durant));
    }

    [Fact]
    public void TheFamiliarsUnknownLinkIsNotCountedIntentional()
    {
        var (tracker, _) = Clocked();
        tracker.Record(InstinctAffinity.Volant);
        tracker.Record(InstinctAffinity.Rampant);
        tracker.RecordUnknown();

        Assert.False(tracker.LastWasIntentional);
    }

    [Fact]
    public void SecondsLeftCountsDownTheWindow()
    {
        var (tracker, advance) = Clocked();
        Assert.Equal(0f, tracker.SecondsLeft);

        tracker.Record(InstinctAffinity.Durant);
        advance(5f);
        Assert.Equal(ArtemisInstinctTracker.WindowSeconds - 5f, tracker.SecondsLeft, 3);

        advance(3f);
        Assert.Equal(0f, tracker.SecondsLeft);
    }

    [Theory]
    [InlineData(InstinctAffinity.Volant)]
    [InlineData(InstinctAffinity.Rampant)]
    [InlineData(InstinctAffinity.Durant)]
    [InlineData(InstinctAffinity.Eldritch)]
    public void PreviousInCycleUndoesNextInCycle(InstinctAffinity heart)
        => Assert.Equal(heart, BSTActions.PreviousInCycle(BSTActions.NextInCycle(heart)));

    // --- A held link must not let a live chain lapse ---

    [Fact]
    public void ALiveChainIsSavedOnlyAtTheLastMoment()
    {
        Assert.False(ArtemisCooldownRules.SavesChain(true, 5f));
        Assert.True(ArtemisCooldownRules.SavesChain(true, ArtemisCooldownRules.ChainSaveSeconds));
        Assert.True(ArtemisCooldownRules.SavesChain(true, 0.5f));
        Assert.False(ArtemisCooldownRules.SavesChain(false, 0.5f));
    }

    [Fact]
    public void WithAHeartLitTheExtensionIsItsClockwiseNeighbour()
        => Assert.Equal(InstinctAffinity.Durant,
            ArtemisCooldownRules.ExtensionAffinity(InstinctAffinity.Rampant, InstinctAffinity.Volant, _ => true));

    [Fact]
    public void AfterAnIntentionalLinkTheExtensionSetsUpTheFamiliarsTrick()
        // No Heart lit (a half is): the link is instinctual anyway, so take the colour just before the Trick's.
        => Assert.Equal(InstinctAffinity.Durant,
            ArtemisCooldownRules.ExtensionAffinity(InstinctAffinity.None, InstinctAffinity.Eldritch, _ => true));

    [Fact]
    public void TheExtensionFallsBackToAnyReadyAxe()
        => Assert.Equal(InstinctAffinity.Eldritch,
            ArtemisCooldownRules.ExtensionAffinity(InstinctAffinity.Rampant, InstinctAffinity.Volant,
                a => a == InstinctAffinity.Eldritch));

    [Fact]
    public void NoReadyAxeNoExtension()
        => Assert.Equal(InstinctAffinity.None,
            ArtemisCooldownRules.ExtensionAffinity(InstinctAffinity.Rampant, InstinctAffinity.Volant, _ => false));

    // --- The familiar's Trick on its own ---

    [Fact]
    public void ATrickOnTheHeartBeforeItsColourIsIntentional()
    {
        Assert.True(ArtemisCooldownRules.TrickWouldBeIntentional(InstinctAffinity.Durant, InstinctAffinity.Eldritch));
        Assert.False(ArtemisCooldownRules.TrickWouldBeIntentional(InstinctAffinity.Eldritch, InstinctAffinity.Eldritch));
        Assert.False(ArtemisCooldownRules.TrickWouldBeIntentional(InstinctAffinity.None, InstinctAffinity.Eldritch));
        Assert.False(ArtemisCooldownRules.TrickWouldBeIntentional(InstinctAffinity.Durant, InstinctAffinity.None));
    }

    // --- The level 50 infinity line ---

    [Fact]
    public void TheInfinityLineNeedsTwoDiamondsAPetChargeRallyAndCheer()
    {
        Assert.True(ArtemisCooldownRules.UsesInfinityLine(50, Gauge(mastered: 2, natural: 1), rallyReady: true, cheerReady: true));
        Assert.False(ArtemisCooldownRules.UsesInfinityLine(49, Gauge(mastered: 2, natural: 1), true, true));
        Assert.False(ArtemisCooldownRules.UsesInfinityLine(50, Gauge(mastered: 3, natural: 1), true, true)); // answer already given
        Assert.False(ArtemisCooldownRules.UsesInfinityLine(50, Gauge(mastered: 2, natural: 0), true, true)); // Cheer can't pay a Trick
        Assert.False(ArtemisCooldownRules.UsesInfinityLine(50, Gauge(mastered: 2, natural: 1), rallyReady: false, cheerReady: true));
        Assert.False(ArtemisCooldownRules.UsesInfinityLine(50, Gauge(mastered: 2, natural: 1), rallyReady: true, cheerReady: false));
    }

    [Fact]
    public void OnTheLineCheerWaitsForYourAnswer()
    {
        // Familiar Eldritch: your answer is Durant. Before it Cheer is kept; once Durant is lit it pays for the Trick.
        Assert.True(ArtemisCooldownRules.HoldsCheerForInfinityLine(true, InstinctAffinity.None, InstinctAffinity.Eldritch));
        Assert.True(ArtemisCooldownRules.HoldsCheerForInfinityLine(true, InstinctAffinity.Eldritch, InstinctAffinity.Eldritch));
        Assert.False(ArtemisCooldownRules.HoldsCheerForInfinityLine(true, InstinctAffinity.Durant, InstinctAffinity.Eldritch));
        Assert.False(ArtemisCooldownRules.HoldsCheerForInfinityLine(false, InstinctAffinity.None, InstinctAffinity.Eldritch));
    }
}
