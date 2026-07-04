using Daedalus.Config;
using Daedalus.Services.Combat;
using Daedalus.Services.Targeting;
using Moq;
using Xunit;

namespace Daedalus.Tests.Services.Targeting;

public class DotTtkGateTests
{
    private const ulong TargetId = 0x1234;

    private static Mock<ITimeToKillService> TtkReturning(float seconds)
    {
        var mock = new Mock<ITimeToKillService>();
        mock.Setup(m => m.GetTtkSeconds(TargetId)).Returns(seconds);
        return mock;
    }

    [Fact]
    public void ShouldSkip_NoService_FailsOpen()
    {
        var config = new TargetingConfig();
        Assert.False(DotTtkGate.ShouldSkip(null, config, TargetId));
    }

    [Fact]
    public void ShouldSkip_CheckDisabled_FailsOpen()
    {
        var config = new TargetingConfig { EnableDotTimeToKillCheck = false };
        Assert.False(DotTtkGate.ShouldSkip(TtkReturning(1f).Object, config, TargetId));
    }

    [Fact]
    public void ShouldSkip_TargetDyingWithinThreshold_Skips()
    {
        var config = new TargetingConfig(); // default threshold 10s
        Assert.True(DotTtkGate.ShouldSkip(TtkReturning(3f).Object, config, TargetId));
    }

    [Fact]
    public void ShouldSkip_TargetLivesPastThreshold_Allows()
    {
        var config = new TargetingConfig();
        Assert.False(DotTtkGate.ShouldSkip(TtkReturning(45f).Object, config, TargetId));
    }

    [Fact]
    public void ShouldSkip_UnknownTtk_FailsOpen()
    {
        // Fresh pulls / non-declining HP report float.MaxValue — opener DoTs must never be skipped.
        var config = new TargetingConfig();
        Assert.False(DotTtkGate.ShouldSkip(TtkReturning(float.MaxValue).Object, config, TargetId));
    }

    [Fact]
    public void DotTtkThreshold_ClampedTo0To30()
    {
        var config = new TargetingConfig { DotTimeToKillThresholdSeconds = 99f };
        Assert.Equal(30f, config.DotTimeToKillThresholdSeconds);
        config.DotTimeToKillThresholdSeconds = -5f;
        Assert.Equal(0f, config.DotTimeToKillThresholdSeconds);
    }

    [Fact]
    public void ScholarSeraph_DefaultsToSaveForDamage()
    {
        // Field regression: OnCooldown default burned Seraph 5s into a pull at full party HP.
        var config = new ScholarConfig();
        Assert.Equal(SeraphUsageStrategy.SaveForDamage, config.SeraphStrategy);
    }
}
