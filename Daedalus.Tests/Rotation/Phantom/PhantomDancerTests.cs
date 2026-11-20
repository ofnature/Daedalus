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

    /// <summary>
    /// Field 2026-11-19 (Korha, Warrior, Arbatel): every step went out with no target, the game said
    /// "Invalid target", and the refused step held the GCD for 13-25s after each Dance.
    /// </summary>
    [Fact]
    public void AStep_NeedsALivingEnemy()
        => Assert.False(PhantomBandRules.ShouldDanceStep(hasProc: true, enemyAlive: false));

    [Fact]
    public void AStep_GoesOutAtTheEnemyOnceItsProcIsUp()
        => Assert.True(PhantomBandRules.ShouldDanceStep(hasProc: true, enemyAlive: true));

    [Fact]
    public void AStep_NotWithoutItsProc()
        => Assert.False(PhantomBandRules.ShouldDanceStep(hasProc: false, enemyAlive: true));

    [Fact]
    public void Dance_OpensOnlyWithAnEnemyToSpendTheStepOn()
    {
        Assert.True(PhantomBandRules.ShouldOpenDance(anyProc: false, enemyAlive: true));
        Assert.False(PhantomBandRules.ShouldOpenDance(anyProc: false, enemyAlive: false));
    }

    [Fact]
    public void Dance_NotWhileAStepIsStillWaiting()
        => Assert.False(PhantomBandRules.ShouldOpenDance(anyProc: true, enemyAlive: true));

    [Fact]
    public void TheFourStepsAreTheCatalogsProcActions()
    {
        var catalog = PhantomActions.All
            .Where(a => a.Job == PhantomJob.Dancer && a.RequiresProc)
            .Select(a => a.ActionId)
            .OrderBy(id => id);

        Assert.Equal(catalog, PhantomBandRules.DanceSteps.Select(s => s.ActionId).OrderBy(id => id));
        Assert.Equal(4, PhantomBandRules.DanceSteps.Select(s => s.ProcStatusId).Distinct().Count());
    }

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
