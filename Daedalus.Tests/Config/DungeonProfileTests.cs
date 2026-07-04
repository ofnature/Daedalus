using Daedalus.Config;
using Xunit;

namespace Daedalus.Tests.Config;

public class DungeonProfileTests
{
    [Fact]
    public void DungeonProfile_HealerAoEThresholds_MatchPotencyBreakevens()
    {
        var config = new Daedalus.Configuration();
        ConfigurationPresets.ApplyDungeonDutyProfile(config);

        // SCH: Art of War 2×140 = 280 < Broil 310 → AoE only at 3+ (field regression: SCH
        // parsed last in dungeons partly from 2-target Art of War spam).
        Assert.Equal(3, config.Scholar.AoEDamageMinTargets);

        // SGE: Dyskrasia 2×170 = 340 < Dosis 380 → 3+.
        Assert.Equal(3, config.Sage.AoEDamageMinTargets);

        // AST: Gravity 2×140 = 280 > Fall Malefic 270 → 2 is a win, keep it.
        Assert.Equal(2, config.Astrologian.AoEDamageMinTargets);

        // WHM: Holy is 300 vs Glare 340 at 2 targets, but the stun utility on trash keeps it at 2.
        Assert.Equal(2, config.Damage.AoEDamageMinTargets);
    }

    [Fact]
    public void DungeonProfile_ScholarAggressiveDps()
    {
        var config = new Daedalus.Configuration();
        ConfigurationPresets.ApplyDungeonDutyProfile(config);

        Assert.Equal(AetherflowUsageStrategy.AggressiveDps, config.Scholar.AetherflowStrategy);
        Assert.Equal(0, config.Scholar.AetherflowReserve);
        Assert.True(config.Scholar.EnableEnergyDrain);
    }
}
