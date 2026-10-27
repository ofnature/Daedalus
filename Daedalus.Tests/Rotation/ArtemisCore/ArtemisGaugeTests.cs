using Daedalus.Data;
using Daedalus.Rotation.ArtemisCore.Helpers;
using Daedalus.Services.Beastmaster;
using Xunit;

namespace Daedalus.Tests.Rotation.ArtemisCore;

/// <summary>
/// The Beastmaster job gauge became readable with ClientStructs 78975d83 (Dalamud 15.0.3.7, patch 7.57).
/// These are the rules that read it in place of the inference Artemis used before: "an axe is refused" for
/// out of TP, our own tally for the diamonds, a timer for the familiar's Trick.
/// </summary>
public class ArtemisGaugeTests
{
    private static ArtemisGauge Gauge(
        int tp = 0, int petTp = 0, InstinctAffinity lit = InstinctAffinity.None, int chain = 0,
        int mastered = 0, int natural = 0)
        => new(tp, petTp, 0, 0, 0, lit, chain, 0, 0, natural, mastered);

    // ── Rally ────────────────────────────────────────────────────────────────────────────

    /// <summary>At 50, three diamonds is exactly 250 TP — the amount that upgrades the axes.</summary>
    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void Rally_AtFiftyWaitsForAllThreeDiamonds(int mastered, bool expected)
        => Assert.Equal(expected, ArtemisCooldownRules.ShouldRallyFromGauge(true, true, tp: 0, mastered, level: 50, hasFamiliar: true, halfLit: true));

    /// <summary>
    /// The in-game tutorial's order: three diamonds → an intentional combo (lights half the Compass) → Rally → the
    /// other half within 7 s. Rallied with nothing lit, the 250-TP axe would be spent lighting the half, with nothing
    /// left to finish it — so at 50 Rally waits for the half.
    /// </summary>
    [Fact]
    public void Rally_AtFiftyWaitsForTheHalfTheIntentionalComboLights()
    {
        Assert.False(ArtemisCooldownRules.ShouldRallyFromGauge(true, true, 0, 3, 50, hasFamiliar: true, halfLit: false));
        Assert.True(ArtemisCooldownRules.ShouldRallyFromGauge(true, true, 0, 3, 50, hasFamiliar: true, halfLit: true));
    }

    /// <summary>Below 50 there is no infinitive combo to open, so the half does not matter.</summary>
    [Fact]
    public void Rally_BelowFiftyIgnoresTheHalf()
        => Assert.True(ArtemisCooldownRules.ShouldRallyFromGauge(true, true, 0, 2, 40, hasFamiliar: true, halfLit: false));

    // ── the infinitive combo's 7 s window ────────────────────────────────────────────────

    [Fact]
    public void Window_OpensWithAHalfAndThreeDiamondsAndRallyReady()
        => Assert.True(ArtemisCooldownRules.InfinitiveWindowOpen(50, Gauge(tp: 0, lit: InstinctAffinity.Moonstalker, mastered: 3), rallyReady: true));

    /// <summary>After Rally: the half still lit and 250 TP — the finisher is next, nothing else goes out.</summary>
    [Fact]
    public void Window_StaysOpenWith250TpAfterRally()
        => Assert.True(ArtemisCooldownRules.InfinitiveWindowOpen(50, Gauge(tp: 250, lit: InstinctAffinity.Sunstrider), rallyReady: false));

    /// <summary>A half lit by an ordinary intentional combo (diamonds still short) is not a window: play on.</summary>
    [Fact]
    public void Window_NotWithoutTheDiamondsOrTheTp()
        => Assert.False(ArtemisCooldownRules.InfinitiveWindowOpen(50, Gauge(tp: 0, lit: InstinctAffinity.Moonstalker, mastered: 2), rallyReady: true));

    /// <summary>Rally on cooldown with three diamonds: there is no 250 coming, so nothing is held for it.</summary>
    [Fact]
    public void Window_NotWhenRallyCannotFire()
        => Assert.False(ArtemisCooldownRules.InfinitiveWindowOpen(50, Gauge(tp: 0, lit: InstinctAffinity.Moonstalker, mastered: 3), rallyReady: false));

    [Fact]
    public void Window_NeedsAHalfAndLevel50()
    {
        Assert.False(ArtemisCooldownRules.InfinitiveWindowOpen(50, Gauge(tp: 250, lit: InstinctAffinity.Rampant), rallyReady: true));
        Assert.False(ArtemisCooldownRules.InfinitiveWindowOpen(49, Gauge(tp: 250, lit: InstinctAffinity.Moonstalker), rallyReady: true));
    }

    /// <summary>Below 50 there is no upgrade to reach, so two diamonds — a full-strength axe — is enough.</summary>
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void Rally_BelowFiftyTakesTwo(int mastered, bool expected)
        => Assert.Equal(expected, ArtemisCooldownRules.ShouldRallyFromGauge(true, true, 0, mastered, level: 40, hasFamiliar: true));

    /// <summary>With 100 TP an axe can still go out, and it spends the whole bar: refill after, not before.</summary>
    [Theory]
    [InlineData(99, true)]
    [InlineData(100, false)]
    [InlineData(250, false)]
    public void Rally_OnlyWhenNoAxeCanBeAfforded(int tp, bool expected)
        => Assert.Equal(expected, ArtemisCooldownRules.ShouldRallyFromGauge(true, true, tp, 3, 50, true, halfLit: true));

    /// <summary>No familiar means no Trick combos and no diamonds coming: take the flat 40.</summary>
    [Fact]
    public void Rally_WithNoFamiliarDoesNotWaitForDiamonds()
        => Assert.True(ArtemisCooldownRules.ShouldRallyFromGauge(true, true, 0, masteredInstinct: 0, 50, hasFamiliar: false));

    [Fact]
    public void Rally_NotWhenOffOrOnCooldown()
    {
        Assert.False(ArtemisCooldownRules.ShouldRallyFromGauge(enabled: false, true, 0, 3, 50, true));
        Assert.False(ArtemisCooldownRules.ShouldRallyFromGauge(true, ready: false, 0, 3, 50, true));
    }

    // ── Rallying Cheer ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(40, true, true)]
    [InlineData(100, true, false)] // the familiar can already afford a Trick
    [InlineData(40, false, false)] // nobody to cheer on
    public void RallyingCheer_WhenTheFamiliarCannotAffordATrick(int petTp, bool hasFamiliar, bool expected)
        => Assert.Equal(expected, ArtemisCooldownRules.ShouldRallyingCheerFromGauge(true, true, hasFamiliar, petTp));

    // ── the level 50 finisher ────────────────────────────────────────────────────────────

    /// <summary>Sun half lit: finish with a Moonstalker skill. Calamity, through Gale Axe — the one that does not rush.</summary>
    [Fact]
    public void Finisher_SunLitTakesTheMoonSkillThatDoesNotDash()
    {
        var axe = ArtemisCooldownRules.FinisherAxe(50, Gauge(tp: 250, lit: InstinctAffinity.Sunstrider));
        Assert.Equal(InstinctAffinity.Volant, axe);
        Assert.Same(BSTActions.GaleAxe, BSTActions.InstinctualFor(axe));
    }

    /// <summary>Moon half lit: finish with a Sunstrider skill. Risen Fall, through Spinning Axe.</summary>
    [Fact]
    public void Finisher_MoonLitTakesTheSunSkillThatDoesNotDash()
    {
        var axe = ArtemisCooldownRules.FinisherAxe(50, Gauge(tp: 250, lit: InstinctAffinity.Moonstalker));
        Assert.Equal(InstinctAffinity.Eldritch, axe);
        Assert.Same(BSTActions.SpinningAxe, BSTActions.InstinctualFor(axe));
    }

    /// <summary>The upgraded skills cost a flat 250; one point short and the button is still the plain axe.</summary>
    [Fact]
    public void Finisher_NeedsTheFullBar()
        => Assert.Equal(InstinctAffinity.None,
            ArtemisCooldownRules.FinisherAxe(50, Gauge(tp: 249, lit: InstinctAffinity.Sunstrider)));

    [Fact]
    public void Finisher_NeedsLevelFifty()
        => Assert.Equal(InstinctAffinity.None,
            ArtemisCooldownRules.FinisherAxe(49, Gauge(tp: 250, lit: InstinctAffinity.Sunstrider)));

    /// <summary>A Heart, or nothing, is not half a Universality: leave it to the Trick pairing.</summary>
    [Theory]
    [InlineData(InstinctAffinity.None)]
    [InlineData(InstinctAffinity.Durant)]
    public void Finisher_NeedsAHalfLit(InstinctAffinity lit)
        => Assert.Equal(InstinctAffinity.None, ArtemisCooldownRules.FinisherAxe(50, Gauge(tp: 250, lit: lit)));

    /// <summary>The finisher the rule picks really is the opposite half, per the game's own action data.</summary>
    [Fact]
    public void TheUpgradedSkillsCarryTheHalvesTheGameSays()
    {
        Assert.Equal(InstinctAffinity.Sunstrider, BSTActions.AffinityOf(44930));  // Brutal Rage
        Assert.Equal(InstinctAffinity.Moonstalker, BSTActions.AffinityOf(44931)); // Hawkish Talons
        Assert.Equal(InstinctAffinity.Sunstrider, BSTActions.AffinityOf(44932));  // Risen Fall
        Assert.Equal(InstinctAffinity.Moonstalker, BSTActions.AffinityOf(44933)); // Calamity
    }

    // ── answering the familiar's Trick ───────────────────────────────────────────────────

    /// <summary>The answer is the clockwise neighbour of what is actually lit.</summary>
    [Theory]
    [InlineData(InstinctAffinity.Volant, InstinctAffinity.Rampant)]
    [InlineData(InstinctAffinity.Rampant, InstinctAffinity.Durant)]
    [InlineData(InstinctAffinity.Durant, InstinctAffinity.Eldritch)]
    [InlineData(InstinctAffinity.Eldritch, InstinctAffinity.Volant)]
    public void Answer_IsClockwiseOfTheLitHeart(InstinctAffinity lit, InstinctAffinity expected)
        => Assert.Equal(expected, ArtemisCooldownRules.AnswerFromGauge(Gauge(lit: lit)));

    [Theory]
    [InlineData(InstinctAffinity.None)]
    [InlineData(InstinctAffinity.Sunstrider)]
    public void Answer_NoneWithoutAHeart(InstinctAffinity lit)
        => Assert.Equal(InstinctAffinity.None, ArtemisCooldownRules.AnswerFromGauge(Gauge(lit: lit)));

    /// <summary>The familiar's skill landing shows as the chain growing or the lit Heart changing.</summary>
    [Fact]
    public void TheFamiliarsTrickIsSeenLanding()
    {
        var atTrick = Gauge(lit: InstinctAffinity.Rampant, chain: 2);
        Assert.False(ArtemisCooldownRules.GaugeMovedSinceTrick(atTrick, atTrick));
        Assert.True(ArtemisCooldownRules.GaugeMovedSinceTrick(atTrick, Gauge(lit: InstinctAffinity.Rampant, chain: 3)));
        Assert.True(ArtemisCooldownRules.GaugeMovedSinceTrick(atTrick, Gauge(lit: InstinctAffinity.Durant, chain: 2)));
    }

    /// <summary>TP moving is not the Compass moving: our own combo steps add TP every GCD.</summary>
    [Fact]
    public void TpAloneIsNotTheTrickLanding()
        => Assert.False(ArtemisCooldownRules.GaugeMovedSinceTrick(Gauge(tp: 40, chain: 1), Gauge(tp: 68, chain: 1)));

    // ── the reader ───────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData((byte)0, InstinctAffinity.None)]
    [InlineData((byte)1, InstinctAffinity.Volant)]
    [InlineData((byte)4, InstinctAffinity.Eldritch)]
    [InlineData((byte)5, InstinctAffinity.Sunstrider)]
    [InlineData((byte)6, InstinctAffinity.Moonstalker)]
    [InlineData((byte)7, InstinctAffinity.None)]   // a value neither side knows is not an affinity
    [InlineData((byte)255, InstinctAffinity.None)]
    public void TheGamesAffinityNumbersMapStraightAcross(byte raw, InstinctAffinity expected)
        => Assert.Equal(expected, BeastmasterGaugeReader.ToAffinity(raw));

    [Fact]
    public void HeartsAndHalvesAreToldApart()
    {
        Assert.True(Gauge(lit: InstinctAffinity.Durant).HasHeart);
        Assert.False(Gauge(lit: InstinctAffinity.Durant).HasHalf);
        Assert.True(Gauge(lit: InstinctAffinity.Moonstalker).HasHalf);
        Assert.False(Gauge(lit: InstinctAffinity.Moonstalker).HasHeart);
        Assert.False(Gauge().HasHeart);
    }

    // ── edges ────────────────────────────────────────────────────────────────────────────

    /// <summary>The diamond count steps from two to three exactly at level 50.</summary>
    [Fact]
    public void Rally_TheDiamondCountStepsUpAtFifty()
    {
        Assert.Equal(ArtemisCooldownRules.RallyMinMastered, ArtemisCooldownRules.RallyMinMasteredFor(49));
        Assert.Equal(ArtemisCooldownRules.MaxMastered, ArtemisCooldownRules.RallyMinMasteredFor(50));
        Assert.Equal(ArtemisCooldownRules.MaxMastered, ArtemisCooldownRules.RallyMinMasteredFor(100));
    }

    /// <summary>At 49 two diamonds and no half are enough; one level up the same gauge waits.</summary>
    [Fact]
    public void Rally_FortyNineAndFiftyDisagreeOnTheSameGauge()
    {
        Assert.True(ArtemisCooldownRules.ShouldRallyFromGauge(true, true, 0, 2, 49, hasFamiliar: true, halfLit: false));
        Assert.False(ArtemisCooldownRules.ShouldRallyFromGauge(true, true, 0, 2, 50, hasFamiliar: true, halfLit: false));
    }

    /// <summary>One TP short of 250 with no Rally to make it up: the window stays shut.</summary>
    [Fact]
    public void Window_OneTpShortWithoutRallyIsShut()
        => Assert.False(ArtemisCooldownRules.InfinitiveWindowOpen(50, Gauge(tp: 249, lit: InstinctAffinity.Sunstrider, mastered: 3), rallyReady: false));

    /// <summary>A Heart is not half a Universality, even with three diamonds and Rally ready.</summary>
    [Fact]
    public void Window_NotOnAHeart()
        => Assert.False(ArtemisCooldownRules.InfinitiveWindowOpen(50, Gauge(tp: 0, lit: InstinctAffinity.Volant, mastered: 3), rallyReady: true));

    /// <summary>Every Heart, not just one, leaves no finisher.</summary>
    [Theory]
    [InlineData(InstinctAffinity.Volant)]
    [InlineData(InstinctAffinity.Rampant)]
    [InlineData(InstinctAffinity.Eldritch)]
    public void Finisher_NoneOnAnyHeart(InstinctAffinity lit)
        => Assert.Equal(InstinctAffinity.None, ArtemisCooldownRules.FinisherAxe(50, Gauge(tp: 250, lit: lit)));
}
