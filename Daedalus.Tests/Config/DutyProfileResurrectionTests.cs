using Daedalus;
using Daedalus.Config;
using Daedalus.Services.Content;
using Xunit;

namespace Daedalus.Tests.Config;

/// <summary>
/// Duty profiles run on every zone change and settings save, on top of the saved config, into the
/// snapshot the rotations read. Anything they set silently overrules the user, because the settings
/// UI keeps showing the saved value.
///
/// <para>
/// Field 2026-08-04: a Sage with "Allow Hardcast Raise" ticked reported the debug readout saying
/// "No Swiftcast (hardcast disabled)" — the raid/trial profile forced it false behind the toggle's
/// back, so the raise waited for a Swiftcast it was never allowed to skip.
/// </para>
/// </summary>
public class DutyProfileResurrectionTests
{
    public static TheoryData<EffectiveDutyProfile> AllProfiles => new()
    {
        EffectiveDutyProfile.Dungeon,
        EffectiveDutyProfile.Trial,
        EffectiveDutyProfile.Raid,
        EffectiveDutyProfile.HighEndRaid,
    };

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void No_duty_profile_overrides_the_hardcast_raise_toggle(EffectiveDutyProfile profile)
    {
        foreach (var userChoice in new[] { true, false })
        {
            var config = new Configuration();
            config.Resurrection.AllowHardcastRaise = userChoice;

            ConfigurationPresets.ApplyDutyProfile(config, profile);

            Assert.Equal(userChoice, config.Resurrection.AllowHardcastRaise);
        }
    }

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void No_duty_profile_overrides_the_raise_mode(EffectiveDutyProfile profile)
    {
        foreach (var mode in new[]
                 {
                     RaiseExecutionMode.RaiseFirst,
                     RaiseExecutionMode.Balanced,
                     RaiseExecutionMode.HealFirst,
                 })
        {
            var config = new Configuration();
            config.Resurrection.RaiseMode = mode;

            ConfigurationPresets.ApplyDutyProfile(config, profile);

            Assert.Equal(mode, config.Resurrection.RaiseMode);
        }
    }

    [Fact]
    public void The_rotation_snapshot_sees_the_users_hardcast_choice_in_a_raid()
    {
        // End to end through the service that actually feeds the rotations: the user ticks the
        // toggle, the zone says raid, and the copy the Sage reads must still say true.
        var saved = new Configuration { EnableAutoDutyConfig = true };
        saved.Resurrection.AllowHardcastRaise = true;

        var duty = new StubDutyContentService(EffectiveDutyProfile.Raid);
        var service = new DutyConfigurationService(saved, duty);

        service.Refresh();

        Assert.True(service.RotationConfiguration.Resurrection.AllowHardcastRaise);
    }

    [Fact]
    public void Duty_profiles_still_tune_what_they_are_meant_to()
    {
        // Guard against "fixing" this by gutting the profiles: the healing/damage tuning that
        // justifies their existence must survive.
        var dungeon = new Configuration();
        ConfigurationPresets.ApplyDutyProfile(dungeon, EffectiveDutyProfile.Dungeon);
        Assert.False(dungeon.Healing.EnableCoHealerAwareness);
        Assert.Equal(2, dungeon.Damage.AoEDamageMinTargets);

        var raid = new Configuration();
        ConfigurationPresets.ApplyDutyProfile(raid, EffectiveDutyProfile.Raid);
        Assert.True(raid.Healing.EnableCoHealerAwareness);
        Assert.Equal(3, raid.Damage.AoEDamageMinTargets);
    }

    private sealed class StubDutyContentService : IDutyContentService
    {
        public StubDutyContentService(EffectiveDutyProfile profile) => EffectiveProfile = profile;

        public DutyContentType CurrentDuty => DutyContentType.Raid;
        public EffectiveDutyProfile EffectiveProfile { get; }
        public string DutyLabel => "Test";
        public uint CurrentTerritoryType => 0;
        public string CurrentDutyName => "Test";

        public void OnTerritoryChanged(ushort territoryType, bool isHighEndZone, int partyMemberCount) { }
    }
}
