using Daedalus.Config;
using Daedalus.Rotation.AstraeaCore.Context;
using Daedalus.Rotation.AstraeaCore.Helpers;
using Daedalus.Rotation.AstraeaCore.Modules;
using Daedalus.Rotation.AstraeaCore.Modules.Healing;
using Moq;
using Xunit;

namespace Daedalus.Tests.Rotation.AstraeaCore;

/// <summary>
/// The healing lockout holds the routine GCD heals while Macrocosmos or a mature Earthly Star is up.
/// Field 2026-10-04 (Holminster Switch): it returned before EVERY healing handler — including the
/// Stellar Detonation and Microcosmos handlers that spend those very states — and it also fired on
/// Divining, an Oracle-ready buff that lasts 30 s and has nothing to do with healing.
/// </summary>
public class HealingLockoutTests
{
    private static IAstraeaContext Context(bool divining = false, bool macro = false, bool star = false, bool enabled = true)
    {
        var config = new Configuration();
        config.Astrologian.EnableHealingLockout = enabled;
        var mock = new Mock<IAstraeaContext>();
        mock.Setup(c => c.Configuration).Returns(config);
        mock.Setup(c => c.HasDivining).Returns(divining);
        mock.Setup(c => c.HasMacrocosmos).Returns(macro);
        mock.Setup(c => c.IsStarMature).Returns(star);
        return mock.Object;
    }

    [Fact]
    public void DiviningAloneIsNotALockout() => Assert.False(AstraeaCardHelper.HasHealingLockout(Context(divining: true)));

    [Fact]
    public void MacrocosmosLocksOut() => Assert.True(AstraeaCardHelper.HasHealingLockout(Context(macro: true)));

    [Fact]
    public void AMatureStarLocksOut() => Assert.True(AstraeaCardHelper.HasHealingLockout(Context(star: true)));

    [Fact]
    public void TheSettingTurnsItOff() => Assert.False(AstraeaCardHelper.HasHealingLockout(Context(macro: true, enabled: false)));

    /// <summary>Held under the lockout: the routine GCD heals.</summary>
    [Fact]
    public void TheGcdHealsAreHeld()
    {
        Assert.False(HealingModule.RunsUnder(new AspectedBeneficHandler(), lockout: true));
        Assert.False(HealingModule.RunsUnder(new SingleTargetHandler(), lockout: true));
        Assert.False(HealingModule.RunsUnder(new AoEHealingHandler(), lockout: true));
    }

    /// <summary>Never held: the releases the lockout waits for, and the oGCD and emergency heals.</summary>
    [Fact]
    public void TheReleasesAndOgcdHealsStillRun()
    {
        Assert.True(HealingModule.RunsUnder(new EarthlyStarDetonationHandler(), lockout: true));
        Assert.True(HealingModule.RunsUnder(new MicrocosmosHandler(), lockout: true));
        Assert.True(HealingModule.RunsUnder(new EssentialDignityHandler(), lockout: true));
        Assert.True(HealingModule.RunsUnder(new DoomTopOffHandler(), lockout: true));
        Assert.True(HealingModule.RunsUnder(new EsunaHandler(), lockout: true));
    }

    [Fact]
    public void WithoutALockoutEverythingRuns()
    {
        Assert.True(HealingModule.RunsUnder(new AspectedBeneficHandler(), lockout: false));
        Assert.True(HealingModule.RunsUnder(new SingleTargetHandler(), lockout: false));
    }
}
