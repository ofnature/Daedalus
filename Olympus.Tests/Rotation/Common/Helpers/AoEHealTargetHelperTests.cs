using Olympus.Config;
using Olympus.Rotation.Common.Helpers;
using Xunit;

namespace Olympus.Tests.Rotation.Common.Helpers;

public sealed class AoEHealTargetHelperTests
{
    private static HealingConfig AutoOnConfig() => new()
    {
        AutoAdjustAoEHealMinTargetsByPartySize = true,
        AoEHealMinTargets = 3,
    };

    [Theory]
    [InlineData(1, 2)]
    [InlineData(4, 2)]
    public void GetEffectiveMinTargets_DungeonOrTrustParty_UsesTwo(int partySize, int expected)
    {
        Assert.Equal(expected, AoEHealTargetHelper.GetEffectiveMinTargets(AutoOnConfig(), partySize));
    }

    [Theory]
    [InlineData(8, 3)]
    [InlineData(12, 3)]
    public void GetEffectiveMinTargets_RaidParty_UsesThree(int partySize, int expected)
    {
        Assert.Equal(expected, AoEHealTargetHelper.GetEffectiveMinTargets(AutoOnConfig(), partySize));
    }

    [Fact]
    public void GetEffectiveMinTargets_MidSizeParty_UsesManualConfig()
    {
        var config = AutoOnConfig();
        config.AoEHealMinTargets = 4;

        Assert.Equal(4, AoEHealTargetHelper.GetEffectiveMinTargets(config, partySize: 6));
    }

    [Fact]
    public void GetEffectiveMinTargets_AutoOff_UsesManualConfig()
    {
        var config = new HealingConfig
        {
            AutoAdjustAoEHealMinTargetsByPartySize = false,
            AoEHealMinTargets = 3,
        };

        Assert.Equal(3, AoEHealTargetHelper.GetEffectiveMinTargets(config, partySize: 4));
    }
}
