using Daedalus.Data;
using Daedalus.Rotation.HecateCore.Abilities;
using Xunit;

namespace Daedalus.Tests.Rotation.HecateCore.Modules;

/// <summary>
/// Quest-locked Blizzard IV in Umbral Ice: the raw level gates pushed Blizzard IV (dropped by the scheduler
/// as not learned) and returned, and the MP-full exit waited for 3 Umbral Hearts only Blizzard IV can
/// give — the ice phase never ended. Gates now use ActionAvailability.MeetsLevelAndLearned.
/// </summary>
public class DamageModuleIceUnlearnedTests
{
    [Fact]
    public void Lv70_Ui3_NoHearts_Blizzard4Unlearned_RefillsWithBlizzard()
    {
        var scheduler = HecateDamageScenario.Run(level: 70, inUmbralIce: true, currentMp: 3000, umbralHearts: 0,
            notLearned: new[] { BLMActions.Blizzard4.ActionId });
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Blizzard);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Blizzard4);
    }

    [Fact]
    public void Lv70_Ui3_FullMp_NoHearts_Blizzard4Unlearned_TransitionsToFire()
    {
        var scheduler = HecateDamageScenario.Run(level: 70, inUmbralIce: true, currentMp: 10000, umbralHearts: 0,
            notLearned: new[] { BLMActions.Blizzard4.ActionId });
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Fire3);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Blizzard4);
    }

    [Fact]
    public void Lv70_Ui3_NoHearts_Blizzard4Learned_CastsBlizzard4()
    {
        var scheduler = HecateDamageScenario.Run(level: 70, inUmbralIce: true, currentMp: 3000, umbralHearts: 0);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Blizzard4);
    }

    [Fact]
    public void Lv70_Ui3_FullMp_NoHearts_Blizzard4Learned_BuildsHeartsFirst()
    {
        var scheduler = HecateDamageScenario.Run(level: 70, inUmbralIce: true, currentMp: 10000, umbralHearts: 0);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Blizzard4);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Fire3);
    }
}
