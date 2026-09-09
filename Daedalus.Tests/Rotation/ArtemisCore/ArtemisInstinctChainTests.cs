using System;
using Daedalus.Data;
using Daedalus.Rotation.ArtemisCore.Helpers;
using Xunit;

namespace Daedalus.Tests.Rotation.ArtemisCore;

/// <summary>
/// The Inner Compass, modelled on the in-game gauge description read 2026-09-08.
/// </summary>
public sealed class ArtemisInstinctChainTests
{
    private static (ArtemisInstinctTracker tracker, Action<float> advance) Clocked()
    {
        var now = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
        var tracker = new ArtemisInstinctTracker();
        tracker.UtcNow = () => now;
        return (tracker, seconds => now = now.AddSeconds(seconds));
    }

    /// <summary>
    /// "If the second instinctual skill performed runs clockwise on the Inner Compass, an
    /// intentional combo will be completed" — Volant, Rampant, Durant, Eldritch, back to Volant.
    /// </summary>
    [Theory]
    [InlineData(InstinctAffinity.Volant, InstinctAffinity.Rampant, IntentionalCombo.Sunstrider)]
    [InlineData(InstinctAffinity.Rampant, InstinctAffinity.Durant, IntentionalCombo.Moonstalker)]
    [InlineData(InstinctAffinity.Durant, InstinctAffinity.Eldritch, IntentionalCombo.Sunstrider)]
    [InlineData(InstinctAffinity.Eldritch, InstinctAffinity.Volant, IntentionalCombo.Moonstalker)]
    public void ClockwiseCompletesTheNamedIntentionalCombo(
        InstinctAffinity first, InstinctAffinity second, IntentionalCombo expected)
    {
        var (tracker, _) = Clocked();
        tracker.Record(first);

        Assert.True(tracker.WouldBeIntentional(second));
        Assert.Equal(second, tracker.PreferredNext);
        Assert.Equal(expected, tracker.ComboFrom(second));
    }

    /// <summary>Anticlockwise still combos — weakly — but is not intentional.</summary>
    [Fact]
    public void AnticlockwiseCombosWithoutBeingIntentional()
    {
        var (tracker, _) = Clocked();
        tracker.Record(InstinctAffinity.Rampant);

        Assert.True(tracker.WouldCombo(InstinctAffinity.Volant));
        Assert.False(tracker.WouldBeIntentional(InstinctAffinity.Volant));
        Assert.Equal(IntentionalCombo.None, tracker.ComboFrom(InstinctAffinity.Volant));
    }

    /// <summary>
    /// "A higher number increases the potency of extra damage dealt by combos." The chain length is
    /// a rotational resource, not decoration — it survives each link and dies with the window.
    /// </summary>
    [Fact]
    public void ChainLengthAccumulatesAndDiesWithTheWindow()
    {
        var (tracker, advance) = Clocked();
        Assert.Equal(0, tracker.ChainLength);

        tracker.Record(InstinctAffinity.Volant);
        Assert.Equal(1, tracker.ChainLength);

        advance(3f);
        tracker.Record(InstinctAffinity.Rampant);
        Assert.Equal(2, tracker.ChainLength);

        advance(ArtemisInstinctTracker.WindowSeconds + 0.1f);
        Assert.False(tracker.IsWindowOpen);
        Assert.Equal(0, tracker.ChainLength);
    }

    /// <summary>
    /// "If either the beastmaster OR FAMILIAR executes another instinctual skill within seven
    /// seconds" — Trick advances the chain. Which Heart it grants depends on the beast, so the
    /// tracker keeps the chain alive WITHOUT claiming to know what comes next; guessing an affinity
    /// here would send the picker after a combo that cannot complete.
    /// </summary>
    [Fact]
    public void TheFamiliarAdvancesTheChainWithoutNamingAnAffinity()
    {
        var (tracker, advance) = Clocked();
        tracker.Record(InstinctAffinity.Volant);

        advance(2f);
        tracker.RecordUnknown();

        Assert.True(tracker.IsWindowOpen);
        Assert.Equal(2, tracker.ChainLength);
        Assert.Equal(InstinctAffinity.None, tracker.LastAffinity);
        Assert.Equal(InstinctAffinity.None, tracker.PreferredNext);

        // Any affinity still continues the chain; none is "intentional" from an unknown link.
        Assert.True(tracker.WouldCombo(InstinctAffinity.Durant));
        Assert.False(tracker.WouldBeIntentional(InstinctAffinity.Durant));
    }

    /// <summary>
    /// Tier 2: the upgraded skills carry Sunstrider and Moonstalker as affinities in their own
    /// right, and either direction completes Universality.
    /// </summary>
    [Theory]
    [InlineData(InstinctAffinity.Sunstrider, InstinctAffinity.Moonstalker)]
    [InlineData(InstinctAffinity.Moonstalker, InstinctAffinity.Sunstrider)]
    public void TheUpgradedPairCompletesUniversalityEitherWay(
        InstinctAffinity first, InstinctAffinity second)
    {
        var (tracker, _) = Clocked();
        tracker.Record(first);

        Assert.Equal(second, tracker.PreferredNext);
        Assert.Equal(IntentionalCombo.Universality, tracker.ComboFrom(second));
        Assert.Equal(InstinctTier.Upgraded, BSTActions.TierOf(first));
    }

    [Fact]
    public void ResetDropsEverything()
    {
        var (tracker, _) = Clocked();
        tracker.Record(InstinctAffinity.Durant);
        tracker.Reset();

        Assert.False(tracker.IsWindowOpen);
        Assert.Equal(0, tracker.ChainLength);
        Assert.Equal(InstinctAffinity.None, tracker.LastAffinity);
    }
}
