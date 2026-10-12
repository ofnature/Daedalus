using System.Linq;
using Daedalus.Data;
using Daedalus.Rotation.Phantom;
using Xunit;

namespace Daedalus.Tests.Rotation.Phantom;

/// <summary>
/// Phantom Dancer's last two actions. From the game's own tooltips (read 2026-10-11): Mesmerize puts Mesmerized on
/// the target for 100s — 10% less damage dealt, 5% more taken — on a 90s recast, and does not stack with Cannoneer's
/// Silver Sickness; Steadfast Stance is a 10%-max-HP barrier that, cast on a party member, rushes you to them.
/// </summary>
public class PhantomDancerTests
{
    [Fact]
    public void Mesmerize_GoesOnATargetThatLacksIt()
        => Assert.True(PhantomBandRules.ShouldMesmerize(inCombat: true, targetHasMesmerized: false,
            targetHasSilverSickness: false, targetTtkSeconds: 120f));

    [Fact]
    public void Mesmerize_NotWhileItIsStillUp()
        => Assert.False(PhantomBandRules.ShouldMesmerize(true, targetHasMesmerized: true, false, 120f));

    /// <summary>The tooltip: "Effect cannot be stacked with Phantom Cannoneer's Silver Sickness."</summary>
    [Fact]
    public void Mesmerize_NotOnTopOfSilverSickness()
        => Assert.False(PhantomBandRules.ShouldMesmerize(true, false, targetHasSilverSickness: true, 120f));

    [Fact]
    public void Mesmerize_NotOnSomethingAboutToDie()
        => Assert.False(PhantomBandRules.ShouldMesmerize(true, false, false,
            PhantomBandRules.MesmerizeMinTtkSeconds - 1f));

    /// <summary>No estimate yet (a fresh pull, a boss) reads as long-lived.</summary>
    [Fact]
    public void Mesmerize_UnknownTimeToKillCountsAsLong()
        => Assert.True(PhantomBandRules.ShouldMesmerize(true, false, false, float.MaxValue));

    [Fact]
    public void Mesmerize_NotOutOfCombat()
        => Assert.False(PhantomBandRules.ShouldMesmerize(inCombat: false, false, false, 120f));

    [Fact]
    public void TheStatusIdsAreTheGames()
    {
        Assert.Equal(4800u, PhantomActions.StatusIds.SteadfastStance);
        Assert.Equal(4802u, PhantomActions.StatusIds.Mesmerized);
        Assert.Equal(4264u, PhantomActions.StatusIds.SilverSickness);
    }

    [Fact]
    public void BothActionsAreInTheCatalogAtTheirLevels()
    {
        var stance = PhantomActions.All.Single(a => a.ActionId == 46604);
        var mesmerize = PhantomActions.All.Single(a => a.ActionId == 46605);
        Assert.Equal((PhantomJob.Dancer, 3), (stance.Job, (int)stance.RequiredLevel));
        Assert.Equal((PhantomJob.Dancer, 4), (mesmerize.Job, (int)mesmerize.RequiredLevel));
    }
}
