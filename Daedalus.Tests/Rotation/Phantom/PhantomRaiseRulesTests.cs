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
}
