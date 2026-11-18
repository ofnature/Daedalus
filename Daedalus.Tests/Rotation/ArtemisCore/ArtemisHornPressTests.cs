using Daedalus.Rotation.ArtemisCore.Helpers;
using Xunit;

namespace Daedalus.Tests.Rotation.ArtemisCore;

/// <summary>
/// When a Battlehorn may be pressed, and what a refusal costs. From the debug logs to 2026-11-08: 62 horns refused by
/// the game, nearly all pressed while a weaponskill sat queued in the last half second of the GCD or a few hundredths
/// of a second after another press; each refusal then cost fifteen seconds, and five times a second refusal followed.
/// Minerva's recordings show the result: 8% of 4,596 swaps left more than ten seconds with no beast out.
/// </summary>
public class ArtemisHornPressTests
{
    private static bool CanPress(float animationLock = 0f, double sinceLastAction = 5, float gcdRemaining = 1.5f)
        => ArtemisFamiliarKeeper.CanPressHornNow(animationLock, sinceLastAction, gcdRemaining);

    [Fact]
    public void AHornGoesOutInTheMiddleOfAGcd() => Assert.True(CanPress());

    [Fact]
    public void AndWithNoGcdRunningAtAll() => Assert.True(CanPress(gcdRemaining: 0f, sinceLastAction: 60));

    /// <summary>"gcd 0.02-0.46, seen y": the next weaponskill is already queued, and the game refuses the horn.</summary>
    [Theory]
    [InlineData(0.46f)]
    [InlineData(0.18f)]
    [InlineData(0.02f)]
    public void NotWhileTheNextWeaponskillIsQueued(float gcdRemaining) => Assert.False(CanPress(gcdRemaining: gcdRemaining));

    /// <summary>"gcd 0.00, sinceSubmit 0.03": a weaponskill went out a moment ago and has not landed yet.</summary>
    [Theory]
    [InlineData(0.03)]
    [InlineData(0.45)]
    [InlineData(0.69)]
    public void NotRightAfterAnotherPress(double sinceLastAction)
        => Assert.False(CanPress(sinceLastAction: sinceLastAction, gcdRemaining: 0f));

    [Fact]
    public void NotWhileAnimationLocked() => Assert.False(CanPress(animationLock: 0.4f));

    /// <summary>One refusal is a collision: try again after the usual gap. Two in a row is something else: back off.</summary>
    [Fact]
    public void OneRefusalDoesNotCostFifteenSeconds()
    {
        Assert.Equal(ArtemisFamiliarKeeper.RetrySeconds, ArtemisFamiliarKeeper.RefusedBackoffSeconds(1));
        Assert.Equal(ArtemisFamiliarKeeper.RefusedRetrySeconds, ArtemisFamiliarKeeper.RefusedBackoffSeconds(2));
        Assert.Equal(ArtemisFamiliarKeeper.RefusedRetrySeconds, ArtemisFamiliarKeeper.RefusedBackoffSeconds(5));
    }

    private static string? Wait(
        bool dead = false, bool moving = false, bool mounted = false, bool casting = false, bool bestiary = false,
        bool pressable = true, bool anyReady = true)
        => ArtemisFamiliarKeeper.HornWait(dead, moving, mounted, casting, bestiary, pressable, anyReady);

    [Fact]
    public void NothingHoldsAHornThatCanGo() => Assert.Null(Wait());

    /// <summary>The log names one reason, the one that matters first: with no horn ready, moving is beside the point.</summary>
    [Fact]
    public void TheLogNamesTheFirstReason()
    {
        Assert.Equal("no horn is ready", Wait(moving: true, anyReady: false));
        Assert.Equal("you are moving (the horn is a cast)", Wait(moving: true, pressable: false));
        Assert.Equal("another press is still going out", Wait(pressable: false));
        Assert.Equal("you are down", Wait(dead: true, anyReady: false));
    }

    // --- the boss pull waits for the first beast and the pre-fight Borrow ---

    private static bool Holds(bool onBoard = true, bool degreeOrLegendary = true, bool keep = true, bool settled = false, double held = 2)
        => ArtemisFamiliarKeeper.HoldsThePull(onBoard, degreeOrLegendary, keep, settled, held);

    /// <summary>
    /// Korha, First Board Third Degree, 2026-11-15: the pull opened about five seconds after the horns were set, the toon
    /// already walking to the boss; the Borrow never had a still second and every fight began with no beast out.
    /// </summary>
    [Fact]
    public void ADegreePullWaitsForTheBeastAndTheBorrow() => Assert.True(Holds());

    [Fact]
    public void ItOpensTheMomentTheyAreReady() => Assert.False(Holds(settled: true));

    [Fact]
    public void AndAfterFifteenSecondsWhateverHasHappened()
    {
        Assert.True(Holds(held: ArtemisFamiliarKeeper.PullHoldSeconds - 0.1));
        Assert.False(Holds(held: ArtemisFamiliarKeeper.PullHoldSeconds));
    }

    /// <summary>A levelling loop is hundreds of fights: no wait there, nor off a board, nor with the keeper off.</summary>
    [Fact]
    public void OnlyOnADegreeOrLegendaryBoardWithTheKeeperOn()
    {
        Assert.False(Holds(degreeOrLegendary: false));
        Assert.False(Holds(onBoard: false));
        Assert.False(Holds(keep: false));
    }
}
