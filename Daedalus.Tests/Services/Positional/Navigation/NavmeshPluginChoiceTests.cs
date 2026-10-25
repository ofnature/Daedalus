using Daedalus.Config;
using Xunit;

namespace Daedalus.Tests.Services.Positional.Navigation;

/// <summary>
/// Settings ▸ General ▸ Boss handling ▸ Navmesh plugin. Ariadne and vnavmesh both take over movement;
/// with both loaded, Daedalus and Minerva each driving a different one left a walk "running" for 54 s
/// while the character never moved (roommate's Astrologian, 2026-10-04).
/// </summary>
public class NavmeshPluginChoiceTests
{
    /// <summary>Auto follows Minerva's order: Ariadne when loaded.</summary>
    [Fact]
    public void AutoPrefersAriadne() => Assert.Equal("Ariadne", NavmeshPluginChoice.Resolve(NavmeshPlugin.Auto, ariadneLoaded: true));

    [Fact]
    public void AutoFallsBackToVnavmesh() => Assert.Equal("vnavmesh", NavmeshPluginChoice.Resolve(NavmeshPlugin.Auto, ariadneLoaded: false));

    /// <summary>A named plugin is used even when absent — movement reports unavailable rather than going through the other.</summary>
    [Fact]
    public void ANamedPluginIsNeverSwapped()
    {
        Assert.Equal("Ariadne", NavmeshPluginChoice.Resolve(NavmeshPlugin.Ariadne, ariadneLoaded: false));
        Assert.Equal("vnavmesh", NavmeshPluginChoice.Resolve(NavmeshPlugin.Vnavmesh, ariadneLoaded: true));
    }

    [Fact]
    public void TheDefaultIsAuto() => Assert.Equal(NavmeshPlugin.Auto, new NavConfig().NavmeshPlugin);

    // --- the "is Ariadne loaded" throttle ---

    /// <summary>
    /// Field 2026-10-25: the check started from long.MinValue, whose difference overflowed negative, so it never ran
    /// and Auto never found Ariadne. The very first look must always happen.
    /// </summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(1_000_000_000L)]
    [InlineData(long.MaxValue)]
    public void TheFirstAriadneCheckAlwaysRuns(long now)
        => Assert.True(Daedalus.Services.Positional.Navigation.VNavService.AriadneCheckDue(now, lastChecked: null));

    [Fact]
    public void TheAriadneCheckRunsAgainAfterASecond()
    {
        Assert.False(Daedalus.Services.Positional.Navigation.VNavService.AriadneCheckDue(5_500, lastChecked: 5_000));
        Assert.True(Daedalus.Services.Positional.Navigation.VNavService.AriadneCheckDue(6_000, lastChecked: 5_000));
    }
}
