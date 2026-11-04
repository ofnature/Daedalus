using System;
using System.Linq;
using Daedalus.Config;
using Daedalus.Data;
using Daedalus.Services.Eureka;
using Xunit;

namespace Daedalus.Tests.Services.Eureka;

/// <summary>Eureka: the zones, the Magia Board setting, Feint L, Bloodbath L, the level guard, and the Logos table.</summary>
public class EurekaRulesTests
{
    [Theory]
    [InlineData(732u, true)]  // Anemos
    [InlineData(763u, true)]  // Pagos
    [InlineData(795u, true)]  // Pyros
    [InlineData(827u, true)]  // Hydatos
    [InlineData(920u, false)] // Bozjan Southern Front
    [InlineData(0u, false)]
    public void OnlyTheFourZonesAreEureka(uint territory, bool eureka)
        => Assert.Equal(eureka, EurekaRules.IsEurekaTerritory(territory));

    [Fact]
    public void FixedModesAlwaysAskForTheirSetting()
    {
        Assert.Equal("attack", EurekaRules.WantedAuto(EurekaMagiaMode.Attack, isTank: true));
        Assert.Equal("defense", EurekaRules.WantedAuto(EurekaMagiaMode.Defense, isTank: false));
        Assert.Null(EurekaRules.WantedAuto(EurekaMagiaMode.Off, isTank: true));
    }

    /// <summary>By job: a tank is on defense whether or not anything is on it yet (Korha 2026-11-03).</summary>
    [Fact]
    public void ATankIsAlwaysOnDefense()
        => Assert.Equal("defense", EurekaRules.WantedAuto(EurekaMagiaMode.TankDefense, isTank: true));

    [Fact]
    public void EveryOtherJobIsOnAttack()
        => Assert.Equal("attack", EurekaRules.WantedAuto(EurekaMagiaMode.TankDefense, isTank: false));

    /// <summary>Feint L: Wind enemies only, and only when Evasion Down is missing or nearly out.</summary>
    [Fact]
    public void FeintGoesOnWindEnemiesMissingEvasionDown()
    {
        Assert.True(EurekaRules.WantsFeint(EurekaRules.Wind, null));
        Assert.True(EurekaRules.WantsFeint(EurekaRules.Wind, 2f));
        Assert.False(EurekaRules.WantsFeint(EurekaRules.Wind, 40f));
        Assert.False(EurekaRules.WantsFeint(1, null)); // Fire
        Assert.False(EurekaRules.WantsFeint(null, null)); // not read
    }

    [Fact]
    public void BloodbathOnlyWhenLowAndNotAlreadyUp()
    {
        Assert.True(EurekaRules.WantsBloodbath(0.4f, hasBloodbath: false));
        Assert.False(EurekaRules.WantsBloodbath(0.4f, hasBloodbath: true));
        Assert.False(EurekaRules.WantsBloodbath(0.6f, hasBloodbath: false));
    }

    [Theory]
    [InlineData(20, 23, 3, false)]
    [InlineData(20, 24, 3, true)]
    [InlineData(20, 21, 0, true)]
    [InlineData(20, 15, 3, false)]
    [InlineData(0, 40, 3, false)]  // our level unknown
    [InlineData(20, 0, 3, false)]  // theirs unknown (outside Eureka)
    public void TheLevelGuardSkipsOnlyEnemiesTooFarAbove(int mine, int theirs, int maxAbove, bool tooHigh)
        => Assert.Equal(tooHigh, EurekaRules.TooHighToPull(mine, theirs, maxAbove));

    [Fact]
    public void LogosIdsAreUniqueAndNeverClashWithLostActions()
    {
        var logos = LogosActionData.All.Select(d => d.ActionId).ToList();
        Assert.Equal(logos.Count, logos.Distinct().Count());
        Assert.Empty(logos.Intersect(BozjaActionData.All.Select(d => d.ActionId)));
    }

    /// <summary>
    /// Every Wisdom stands down for any Wisdom (only one can be up), so two slotted Wisdoms never take turns
    /// replacing each other.
    /// </summary>
    [Fact]
    public void AnyWisdomStandsDownForEveryWisdom()
    {
        var wisdoms = LogosActionData.All.Where(d => d.Name.StartsWith("Wisdom of the", StringComparison.Ordinal)).ToList();
        Assert.Equal(14, wisdoms.Count);
        foreach (var wisdom in wisdoms)
        {
            Assert.Equal(LostRole.Stance, wisdom.Role);
            Assert.Equal(LogosActionData.WisdomStatusIds, wisdom.Provides);
        }
    }

    [Theory]
    [InlineData("Feint L", true)]
    [InlineData("Raise L", true)]
    [InlineData("Cure L II", false)]
    [InlineData("Bloodbath L", false)]
    [InlineData("Spirit of the Remembered", false)]
    public void GcdFlagFollowsTheRecastGroup(string name, bool isGcd)
        => Assert.Equal(isGcd, LogosActionData.All.Single(d => d.Name == name).IsGcd);

    [Fact]
    public void SacrificeLIsOffUnlessChosen()
        => Assert.False(LogosActionData.All.Single(d => d.Name == "Sacrifice L").DefaultEnabled);
}
