using Daedalus.Rotation.NyxCore.Modules;
using Xunit;

namespace Daedalus.Tests.Rotation.NyxCore.Modules;

/// <summary>
/// Proactive TBN banking: while actively tanking with MP to spare and no Dark Arts banked, TBN is popped
/// so the shield breaks → Dark Arts → free Edge/Flood (MP-neutral DPS gain). Gated so the shield reliably
/// breaks and Darkside MP isn't starved.
/// </summary>
public sealed class NyxTbnBankingTests
{
    [Fact]
    public void Banks_WhenTankingWithSpareMp()
    {
        Assert.True(MitigationModule.ShouldBankTbn(enabled: true, hasDarkArts: false, damageRate: 1200f, currentMp: 7000));
    }

    [Fact]
    public void DoesNotBank_WhenNotTakingDamage()
    {
        // No incoming damage → the shield wouldn't break, so banking would just waste 3000 MP.
        Assert.False(MitigationModule.ShouldBankTbn(enabled: true, hasDarkArts: false, damageRate: 0f, currentMp: 9000));
    }

    [Fact]
    public void DoesNotBank_WhenDarkArtsAlreadyBanked()
    {
        // Can't hold two Dark Arts — banking again would waste the free Edge.
        Assert.False(MitigationModule.ShouldBankTbn(enabled: true, hasDarkArts: true, damageRate: 1200f, currentMp: 9000));
    }

    [Fact]
    public void DoesNotBank_BelowMpFloor()
    {
        // Below the floor, the post-TBN MP can't maintain Darkside until the free Edge.
        Assert.False(MitigationModule.ShouldBankTbn(enabled: true, hasDarkArts: false, damageRate: 1200f, currentMp: 5000));
    }

    [Fact]
    public void DoesNotBank_WhenDisabled()
    {
        Assert.False(MitigationModule.ShouldBankTbn(enabled: false, hasDarkArts: false, damageRate: 1200f, currentMp: 9000));
    }
}
