using Daedalus.Data;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>Rotations read a copy of the settings: the Legendary goal and "You tank" must reach that copy.</summary>
[Collection("ExternalCombatOverrideState")]
public class CrucibleLegendaryConfigCopyTests
{
    [Fact]
    public void TheLegendaryGoalAndYouTankReachTheRotationCopy()
    {
        var saved = new Daedalus.Configuration();
        var copy = Daedalus.Config.ConfigurationCopier.CreateRotationCopy(saved);
        saved.Beastmaster.CrucibleGoal = CrucibleTeamGoal.Legendary;
        saved.Beastmaster.CrucibleLegendaryBeastmasterTanks = true;
        Daedalus.Config.ConfigurationCopier.CopyOnto(copy, saved);
        Assert.Equal(CrucibleTeamGoal.Legendary, copy.Beastmaster.CrucibleGoal);
        Assert.True(copy.Beastmaster.CrucibleLegendaryBeastmasterTanks);
    }
}
