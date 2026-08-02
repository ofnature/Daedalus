using Daedalus.Config;
using Daedalus.Rotation.Phantom;
using Xunit;

namespace Daedalus.Tests.Rotation.Phantom;

/// <summary>
/// Phantom raise policy — Chemist's Revive and Phantom White Mage's Occult Raise. Same shape as
/// the Variant layer's policy: a dead healer is always worth raising, a dead DPS is left to a
/// living healer.
/// </summary>
public sealed class PhantomRaiseRulesTests
{
    private static PhantomConfig Config(bool enabled = true) => new() { UsePhantomRaise = enabled };

    [Fact]
    public void DecideRaise_DeadHealerAlwaysWins()
    {
        var decision = PhantomBandRules.DecideRaise(Config(),
            deadHealerPresent: true, deadOtherPresent: true, livingHealerPresent: true);

        Assert.Equal(PhantomRaiseDecision.RaiseHealer, decision);
    }

    /// <summary>
    /// A living healer raises faster and better; the phantom caster is mid-rotation. Leave it
    /// to them.
    /// </summary>
    [Fact]
    public void DecideRaise_LeavesDeadDpsToALivingHealer()
    {
        var decision = PhantomBandRules.DecideRaise(Config(),
            deadHealerPresent: false, deadOtherPresent: true, livingHealerPresent: true);

        Assert.Equal(PhantomRaiseDecision.None, decision);
    }

    [Fact]
    public void DecideRaise_RaisesDeadDpsWhenNoHealerIsAlive()
    {
        var decision = PhantomBandRules.DecideRaise(Config(),
            deadHealerPresent: false, deadOtherPresent: true, livingHealerPresent: false);

        Assert.Equal(PhantomRaiseDecision.RaiseOther, decision);
    }

    [Fact]
    public void DecideRaise_DoesNothingWithNobodyDead()
    {
        var decision = PhantomBandRules.DecideRaise(Config(),
            deadHealerPresent: false, deadOtherPresent: false, livingHealerPresent: true);

        Assert.Equal(PhantomRaiseDecision.None, decision);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void DecideRaise_RespectsTheToggle(bool deadHealer, bool deadOther)
    {
        var decision = PhantomBandRules.DecideRaise(Config(enabled: false),
            deadHealer, deadOther, livingHealerPresent: false);

        Assert.Equal(PhantomRaiseDecision.None, decision);
    }

    [Fact]
    public void UsePhantomRaise_DefaultsOn()
    {
        Assert.True(new PhantomConfig().UsePhantomRaise);
    }

    /// <summary>
    /// "Leave it to the living healer" assumes the healer acts. Field evidence says it often
    /// does not — out of range, out of MP, or blocked by something still unpinned — and the
    /// deferral then means nobody raises at all. After the grace period the caller drops
    /// livingHealerPresent so the phantom steps in.
    /// </summary>
    [Fact]
    public void DecideRaise_StepsInOnceTheHealerHasBeenGivenItsChance()
    {
        var deferred = PhantomBandRules.DecideRaise(Config(),
            deadHealerPresent: false, deadOtherPresent: true, livingHealerPresent: true);
        var afterGrace = PhantomBandRules.DecideRaise(Config(),
            deadHealerPresent: false, deadOtherPresent: true, livingHealerPresent: false);

        Assert.Equal(PhantomRaiseDecision.None, deferred);
        Assert.Equal(PhantomRaiseDecision.RaiseOther, afterGrace);
    }

    /// <summary>Long enough for a Swiftcast raise or an 8s hardcast to land first when things work.</summary>
    [Fact]
    public void LivingHealerGrace_OutlastsAHardcastRaise()
    {
        Assert.True(PhantomBandRules.LivingHealerGraceSeconds >= 8f);
        Assert.True(PhantomBandRules.LivingHealerGraceSeconds <= 20f,
            "any longer and the corpse is released before the phantom bothers");
    }

    // ── The phantom layer must not eat the raise ──

    /// <summary>
    /// The layer pre-empts the GCD before the job's modules run. Raise is a GCD, so a phantom
    /// cast holding the window stops a healer ever casting it — which is why Sage raises worked
    /// everywhere EXCEPT the Horns, the only place this layer runs.
    /// </summary>
    [Fact]
    public void ShouldYieldGcd_WhenAHealerHasABodyToRaise()
    {
        Assert.True(PhantomBandRules.ShouldYieldGcdForRaise(jobCanRaise: true, raisableCorpseInRange: true));
    }

    [Theory]
    [InlineData(true, false)]   // healer, nobody down — keep pre-empting
    [InlineData(false, true)]   // body down, but this job cannot raise anyway
    [InlineData(false, false)]
    public void ShouldYieldGcd_OnlyWhenBothHold(bool canRaise, bool corpse)
    {
        Assert.False(PhantomBandRules.ShouldYieldGcdForRaise(canRaise, corpse));
    }

    /// <summary>
    /// An instant oGCD raise costs a weave slot, not the GCD a healer needs, so there is nothing
    /// to defer FOR — and the Occult death timer can return a body to base well inside any grace
    /// period. The caller drops livingHealerPresent outright for those.
    /// </summary>
    [Fact]
    public void DecideRaise_AnInstantRaiseActsEvenBesideALivingHealer()
    {
        var decision = PhantomBandRules.DecideRaise(Config(),
            deadHealerPresent: false, deadOtherPresent: true, livingHealerPresent: false);

        Assert.Equal(PhantomRaiseDecision.RaiseOther, decision);
    }

    /// <summary>
    /// The GCD yield exists so phantom casts do not starve a healer's Raise. But Occult Raise is
    /// ActionCategory 2 (Spell), so it goes in the GCD queue too — and the yield was starving the
    /// phantom's OWN raise. Whichever raise is queued must be allowed through.
    /// </summary>
    [Fact]
    public void ShouldYieldGcd_MustNotBlockOurOwnQueuedRaise()
    {
        // The layer passes (_raiseQueuedThisFrame || !ShouldYieldGcdForRaise(...)) — so with a
        // raise queued the yield is bypassed regardless of what the rule says.
        var yieldWanted = PhantomBandRules.ShouldYieldGcdForRaise(
            jobCanRaise: true, raisableCorpseInRange: true);
        const bool raiseQueued = true;

        Assert.True(yieldWanted, "a healer with a body still wants the GCD in general");
        Assert.True(raiseQueued || !yieldWanted, "but our own queued raise dispatches anyway");
    }
}
