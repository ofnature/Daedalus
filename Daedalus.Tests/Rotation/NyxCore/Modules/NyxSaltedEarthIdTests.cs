using Daedalus.Data;
using Xunit;

namespace Daedalus.Tests.Rotation.NyxCore.Modules;

/// <summary>
/// Regression: Salted Earth's action ID was wrong (7394, a Stormblood-range id belonging to another job),
/// so the game refused every cast with status 574 ("wrong job") and it never fired. Verified correct IDs
/// via XIVAPI: Salted Earth = 3639 (HW Lv52), Salt and Darkness = 25755 (EW Lv86).
/// </summary>
public sealed class NyxSaltedEarthIdTests
{
    [Fact]
    public void SaltedEarth_HasCorrectActionId()
    {
        Assert.Equal(3639u, DRKActions.SaltedEarth.ActionId);
    }

    [Fact]
    public void SaltAndDarkness_HasCorrectActionId()
    {
        Assert.Equal(25755u, DRKActions.SaltAndDarkness.ActionId);
    }
}
