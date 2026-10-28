using Daedalus.Services;
using Xunit;

namespace Daedalus.Tests.Rotation.IrisCore.Helpers;

/// <summary>
/// PCT portrait flags come from the gauge CreatureFlags byte. ClientStructs JobGaugeEnums.CreatureFlags:
/// Pom = 1, Wings = 2, Claw = 4, MooglePortait = 16, MadeenPortrait = 32 (PictomancerGauge.MooglePortraitReady /
/// MadeenPortraitReady). The old masks (0x02 / 0x04) read Wings/Claw instead: after Fanged Muse (flags 32)
/// Madeen read false and was never cast; after Clawed Muse with Mog unused (23) Madeen read true.
/// </summary>
public class IrisPortraitFlagsTests
{
    [Theory]
    [InlineData(19)] // Pom + Wings + Mog portrait (after Winged Muse)
    [InlineData(23)] // Pom + Wings + Claw + Mog portrait (Mog unused after Clawed Muse)
    [InlineData(16)]
    public void MogOnly(byte flags)
    {
        Assert.True(SafeGameAccess.IsPctMogPortraitFlagSet(flags));
        Assert.False(SafeGameAccess.IsPctMadeenPortraitFlagSet(flags));
    }

    [Theory]
    [InlineData(32)] // after Fanged Muse
    [InlineData(33)] // Madeen up, Pom drawn
    public void MadeenOnly(byte flags)
    {
        Assert.False(SafeGameAccess.IsPctMogPortraitFlagSet(flags));
        Assert.True(SafeGameAccess.IsPctMadeenPortraitFlagSet(flags));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)] // Pom + Wings drawn, no portrait
    [InlineData(7)] // Pom + Wings + Claw drawn, no portrait
    public void Neither(byte flags)
    {
        Assert.False(SafeGameAccess.IsPctMogPortraitFlagSet(flags));
        Assert.False(SafeGameAccess.IsPctMadeenPortraitFlagSet(flags));
    }
}
