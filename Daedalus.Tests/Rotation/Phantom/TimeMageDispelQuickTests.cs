using Daedalus.Config;
using Daedalus.Data;
using Daedalus.Rotation.Phantom;
using Xunit;

namespace Daedalus.Tests.Rotation.Phantom;

/// <summary>Time Mage's Occult Dispel (Lv.4) and Occult Quick (Lv.5), RSR parity.</summary>
public class TimeMageDispelQuickTests
{
    private static PhantomConfig Cfg() => new();

    [Theory]
    [InlineData(true, true, false, false, true)]   // buff on the target: strip it
    [InlineData(true, false, false, false, false)] // nothing to strip
    [InlineData(true, true, true, false, false)]   // just tried: give it a moment
    [InlineData(true, true, false, true, false)]   // had no effect on this enemy before
    [InlineData(false, true, false, false, false)] // out of combat
    public void Dispel(bool inCombat, bool hasBuff, bool recently, bool resisted, bool fire)
        => Assert.Equal(fire, PhantomBandRules.ShouldDispel(Cfg(), inCombat, hasBuff, recently, resisted));

    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, false)]  // another instant-cast buff already up
    [InlineData(true, false, true, false)]  // Red Mage burst
    [InlineData(false, false, false, false)]
    public void Quick(bool inCombat, bool instantCast, bool rdmBurst, bool fire)
        => Assert.Equal(fire, PhantomBandRules.ShouldQuick(Cfg(), inCombat, instantCast, rdmBurst));

    [Fact]
    public void Toggles_TurnThemOff()
    {
        var cfg = Cfg();
        Assert.True(cfg.TimeMageUseDispel);
        Assert.True(cfg.TimeMageUseQuick);
        cfg.TimeMageUseDispel = false;
        cfg.TimeMageUseQuick = false;
        Assert.False(PhantomBandRules.ShouldDispel(cfg, true, true, false, false));
        Assert.False(PhantomBandRules.ShouldQuick(cfg, true, false, false));
    }

    /// <summary>RSR's PhantomDispellable list.</summary>
    [Theory]
    [InlineData(1161u)] // Damage Up
    [InlineData(61u)]   // Damage Up
    [InlineData(4355u)] // Dark Defenses
    [InlineData(2556u)] // Magic Damage Up
    [InlineData(1706u)] // Evasion Up
    public void DispellableList(uint statusId)
        => Assert.Contains(statusId, PhantomActions.DispellableStatusIds);

    /// <summary>Quick never stacks on itself or on Swiftcast/Dualcast/Triplecast/Lost Chainspell.</summary>
    [Theory]
    [InlineData(4260u)] // Occult Quick
    [InlineData(167u)]  // Swiftcast
    [InlineData(1249u)] // Dualcast
    [InlineData(2560u)] // Lost Chainspell
    public void InstantCastList(uint statusId)
        => Assert.Contains(statusId, PhantomActions.InstantCastStatusIds);
}
