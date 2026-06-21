using Moq;
using Olympus.Config;
using Olympus.Services.Content;
using Xunit;

namespace Olympus.Tests.Services.Content;

public sealed class DutyContentClassifierTests
{
    [Theory]
    [InlineData(DutyContentType.Dungeon, false, 4, false, EffectiveDutyProfile.Dungeon)]
    [InlineData(DutyContentType.Trial, false, 8, false, EffectiveDutyProfile.Trial)]
    [InlineData(DutyContentType.Raid, false, 8, false, EffectiveDutyProfile.Raid)]
    [InlineData(DutyContentType.Raid, true, 8, false, EffectiveDutyProfile.HighEndRaid)]
    [InlineData(DutyContentType.Trial, true, 8, false, EffectiveDutyProfile.HighEndRaid)]
    [InlineData(DutyContentType.Unknown, false, 0, true, EffectiveDutyProfile.Dungeon)]
    [InlineData(DutyContentType.Unknown, false, 8, false, EffectiveDutyProfile.Raid)]
    [InlineData(DutyContentType.OpenWorld, false, 0, false, EffectiveDutyProfile.None)]
    public void Resolve_MapsSignalsToProfile(
        DutyContentType dutyType,
        bool isHighEnd,
        int partySize,
        bool trustOrEmpty,
        EffectiveDutyProfile expected)
    {
        var profile = DutyContentClassifier.Resolve(dutyType, isHighEnd, partySize, trustOrEmpty);
        Assert.Equal(expected, profile);
    }

    [Theory]
    [InlineData(2u, DutyContentType.Dungeon)]
    [InlineData(4u, DutyContentType.Trial)]
    [InlineData(5u, DutyContentType.Raid)]
    [InlineData(0u, DutyContentType.OpenWorld)]
    [InlineData(99u, DutyContentType.Unknown)]
    public void FromContentTypeRowId_MapsLuminaRows(uint rowId, DutyContentType expected)
    {
        Assert.Equal(expected, DutyContentClassifier.FromContentTypeRowId(rowId));
    }
}

public sealed class DutyConfigurationServiceTests
{
    [Fact]
    public void Refresh_AppliesDungeonOverlayWithoutMutatingSavedConfig()
    {
        var saved = new Configuration
        {
            EnableAutoDutyConfig = true,
            Astrologian = { DivinationOnBurst = true, CardsUnderDivinationOnly = true },
            Healing = { EnableCoHealerAwareness = true, AoEHealMinTargets = 3 },
            Damage = { AoEDamageMinTargets = 3 },
        };

        var dutyContent = new Mock<IDutyContentService>();
        dutyContent.Setup(x => x.EffectiveProfile).Returns(EffectiveDutyProfile.Dungeon);

        var service = new DutyConfigurationService(saved, dutyContent.Object);
        service.Refresh();

        Assert.True(saved.Astrologian.DivinationOnBurst);
        Assert.True(saved.Healing.EnableCoHealerAwareness);
        Assert.False(service.RotationConfiguration.Astrologian.DivinationOnBurst);
        Assert.False(service.RotationConfiguration.Astrologian.CardsUnderDivinationOnly);
        Assert.False(service.RotationConfiguration.Healing.EnableCoHealerAwareness);
        Assert.Equal(2, service.RotationConfiguration.Healing.AoEHealMinTargets);
        Assert.Equal(2, service.RotationConfiguration.Damage.AoEDamageMinTargets);
    }

    [Fact]
    public void Refresh_DoesNothingWhenAutoDutyDisabled()
    {
        var saved = new Configuration
        {
            EnableAutoDutyConfig = false,
            Healing = { EnableCoHealerAwareness = true, AoEHealMinTargets = 3 },
        };

        var dutyContent = new Mock<IDutyContentService>();
        dutyContent.Setup(x => x.EffectiveProfile).Returns(EffectiveDutyProfile.Dungeon);

        var service = new DutyConfigurationService(saved, dutyContent.Object);
        service.Refresh();

        Assert.True(service.RotationConfiguration.Healing.EnableCoHealerAwareness);
        Assert.Equal(3, service.RotationConfiguration.Healing.AoEHealMinTargets);
    }

    [Fact]
    public void ApplyDutyProfile_RaidOverlayAdjustsSharedHealerSettings()
    {
        var config = new Configuration();
        ConfigurationPresets.ApplyDutyProfile(config, EffectiveDutyProfile.Raid);

        Assert.True(config.Healing.EnableCoHealerAwareness);
        Assert.True(config.Healing.EnablePreemptiveHealing);
        Assert.Equal(3, config.Damage.AoEDamageMinTargets);
    }
}
