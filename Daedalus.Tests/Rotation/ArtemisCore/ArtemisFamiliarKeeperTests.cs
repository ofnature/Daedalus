using Daedalus.Config.DPS;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.ArtemisCore.Helpers;
using Xunit;

namespace Daedalus.Tests.Rotation.ArtemisCore;

/// <summary>
/// A death leaves a Beastmaster with no familiar, and with it no Trick, no combos and no Tempered Release
/// until a horn is pressed again. The rotation now presses it.
/// </summary>
public class ArtemisFamiliarKeeperTests
{
    private const double Long = 3600d;
    private static readonly bool[] AllUsable = [true, true, true];

    private static int Pick(
        bool enabled = true, bool hasFamiliar = false, bool canAct = true,
        double gone = Long, double sinceTry = Long, int lastOut = 0, bool[]? usable = null)
        => ArtemisFamiliarKeeper.PickHorn(enabled, hasFamiliar, canAct, gone, sinceTry, lastOut, usable ?? AllUsable);

    /// <summary>After a death the same beast comes back, not whichever horn is first.</summary>
    [Fact]
    public void BringsBackTheBeastThatWasOut() => Assert.Equal(1, Pick(lastOut: 1));

    [Fact]
    public void NothingToDoWhileAFamiliarIsOut()
        => Assert.Equal(ArtemisFamiliarKeeper.None, Pick(hasFamiliar: true));

    [Fact]
    public void NotWhenTurnedOff() => Assert.Equal(ArtemisFamiliarKeeper.None, Pick(enabled: false));

    /// <summary>Dead, moving, mounted or mid-cast: the horn is a one-second cast and would only fail.</summary>
    [Fact]
    public void NotWhenTheHornCannotBeCast() => Assert.Equal(ArtemisFamiliarKeeper.None, Pick(canAct: false));

    /// <summary>A beast that was only just summoned has not appeared yet; that is not "gone".</summary>
    [Fact]
    public void WaitsOutABriefAbsence()
    {
        Assert.Equal(ArtemisFamiliarKeeper.None, Pick(gone: 0.5));
        Assert.Equal(0, Pick(gone: ArtemisFamiliarKeeper.GoneGraceSeconds));
    }

    [Fact]
    public void DoesNotRepeatATryEveryFrame()
    {
        Assert.Equal(ArtemisFamiliarKeeper.None, Pick(sinceTry: 1.0));
        Assert.Equal(0, Pick(sinceTry: ArtemisFamiliarKeeper.RetrySeconds));
    }

    /// <summary>
    /// In combat a horn's cooldown starts when its beast retreats, so after a Parting Blow the next horn in
    /// line goes out — wrapping round from the third to the first.
    /// </summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 0)]
    public void TakesTheNextHornWhenTheLastOneIsNotReady(int lastOut, int expected)
    {
        var usable = new[] { true, true, true };
        usable[lastOut] = false;
        Assert.Equal(expected, Pick(lastOut: lastOut, usable: usable));
    }

    /// <summary>After a plugin reload nothing is known about which horn was out: the first usable one.</summary>
    [Fact]
    public void WithNoMemoryOfTheLastHornTakesTheFirstUsable()
        => Assert.Equal(1, Pick(lastOut: ArtemisFamiliarKeeper.None, usable: [false, true, true]));

    /// <summary>An empty horn, one not learned yet, one on cooldown: none of them is pressed.</summary>
    [Fact]
    public void NoUsableHornMeansNoPress()
        => Assert.Equal(ArtemisFamiliarKeeper.None, Pick(usable: [false, false, false]));

    [Fact]
    public void TheHornsAreTheGames()
    {
        Assert.Equal(3, BSTActions.Battlehorns.Length);
        Assert.Equal((44881u, 1), (BSTActions.Battlehorns[0].ActionId, (int)BSTActions.Battlehorns[0].MinLevel));
        Assert.Equal((44892u, 10), (BSTActions.Battlehorns[1].ActionId, (int)BSTActions.Battlehorns[1].MinLevel));
        Assert.Equal((44894u, 20), (BSTActions.Battlehorns[2].ActionId, (int)BSTActions.Battlehorns[2].MinLevel));
        Assert.All(BSTActions.Battlehorns, horn =>
        {
            Assert.Equal(ActionTargetType.Self, horn.TargetType);
            Assert.Equal(1f, horn.CastTime); // a one-second cast, which is why it waits for you to stand still
        });
    }

    [Fact]
    public void OnByDefault() => Assert.True(new BeastmasterConfig().KeepFamiliarOut);
}
