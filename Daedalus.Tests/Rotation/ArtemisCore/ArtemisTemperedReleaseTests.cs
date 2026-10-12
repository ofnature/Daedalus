using System.Linq;
using Daedalus.Config.DPS;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.ArtemisCore.Abilities;
using Daedalus.Rotation.ArtemisCore.Modules;
using Xunit;

namespace Daedalus.Tests.Rotation.ArtemisCore;

/// <summary>
/// Tempered Release orders the familiar's signature move. The rotation never pressed it, so Meteor, King's
/// Will and the rest only went out by hand. Action data is the game's own (Action sheet, read 2026-10-11).
/// </summary>
public class ArtemisTemperedReleaseTests
{
    private const int Behemoth = 50;

    /// <summary>No recent try.</summary>
    private const double Long = 3600d;

    [Fact]
    public void FiresWhenTheGameSaysItIsReady()
        => Assert.True(DamageModule.ShouldTemperedRelease(
            enabled: true, ready: true, Behemoth, partingBlowEnabled: false, selfHpPct: 1f, Long));

    /// <summary>Ready covers level, combat and One with Nature — the once-per-summon token — in one answer.</summary>
    [Fact]
    public void NotWhenTheGameSaysItIsNot()
        => Assert.False(DamageModule.ShouldTemperedRelease(true, ready: false, Behemoth, false, 1f, Long));

    [Fact]
    public void NotWhenTurnedOff()
        => Assert.False(DamageModule.ShouldTemperedRelease(enabled: false, true, Behemoth, false, 1f, Long));

    /// <summary>Final Sting sends the familiar away: the same trade as Parting Blow, so the same setting.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void WespeFollowsThePartingBlowSetting(bool partingBlowEnabled, bool expected)
        => Assert.Equal(expected, DamageModule.ShouldTemperedRelease(
            true, true, DamageModule.WespeBestiaryNo, partingBlowEnabled, 1f, Long));

    /// <summary>Hypodermic Hustle puts a DoT on the beastmaster.</summary>
    [Theory]
    [InlineData(0.90f, true)]
    [InlineData(0.50f, true)]
    [InlineData(0.30f, false)]
    public void SabotenderWaitsForHealthyHp(float selfHp, bool expected)
        => Assert.Equal(expected, DamageModule.ShouldTemperedRelease(
            true, true, DamageModule.SabotenderBestiaryNo, false, selfHp, Long));

    /// <summary>Low HP only matters for the one beast whose move costs HP.</summary>
    [Fact]
    public void OtherBeastsIgnoreOwnHp()
        => Assert.True(DamageModule.ShouldTemperedRelease(true, true, Behemoth, false, selfHpPct: 0.10f, Long));

    [Fact]
    public void AnUnidentifiedFamiliarStillGetsTheOrder()
        => Assert.True(DamageModule.ShouldTemperedRelease(true, true, bestiaryNo: null, false, 1f, Long));

    [Fact]
    public void TheTwoExceptionsAreTheBeastsTheyClaimToBe()
    {
        Assert.Equal("Wespe", BstFamiliars.All.Single(b => b.BestiaryNo == DamageModule.WespeBestiaryNo).Name);
        Assert.Equal("Sabotender",
            BstFamiliars.All.Single(b => b.BestiaryNo == DamageModule.SabotenderBestiaryNo).Name);
    }

    /// <summary>
    /// Field 2026-10-11: the game said "ready", refused the press with "Invalid target.", and the rule offered
    /// it again every frame — 30 log lines a second. A refusal now buys a pause before the next try.
    /// </summary>
    [Theory]
    [InlineData(0.03, false)]
    [InlineData(1.0, false)]
    [InlineData(2.0, true)]
    public void ARefusedPressIsNotRepeatedEveryFrame(double secondsSinceLastTry, bool expected)
        => Assert.Equal(expected, DamageModule.ShouldTemperedRelease(
            true, true, Behemoth, false, 1f, secondsSinceLastTry));

    /// <summary>
    /// Sent at the enemy, as the hotbar button is. Sent at the player the game answered "Invalid target."
    /// for a Ziz, whose release is a cone attack; the buff-type releases fall back to the player on their own.
    /// </summary>
    [Fact]
    public void TheOrderGoesToTheEnemyNotToTheBeastmaster()
    {
        Assert.Equal(ActionTargetType.SingleEnemy, BSTActions.TemperedRelease.TargetType);
        Assert.Equal(30f, BSTActions.TemperedRelease.Range);
    }

    [Fact]
    public void TheActionIsTheGames()
    {
        Assert.Equal(44890u, BSTActions.TemperedRelease.ActionId);
        Assert.Equal(18, BSTActions.TemperedRelease.MinLevel);
        Assert.Same(BSTActions.TemperedRelease, ArtemisAbilities.TemperedRelease.Action);
    }

    [Fact]
    public void OnByDefault() => Assert.True(new BeastmasterConfig().EnableTemperedRelease);
}
