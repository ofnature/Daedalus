using System;
using Daedalus.Services.Consumables;
using Xunit;

namespace Daedalus.Tests.Services;

/// <summary>
/// Following a Phoenix Down attempt by its cast bar, not by the game's answer.
/// <para>
/// Field 2026-09-25, Southern Front: two toons both logged "refused by the game", and in the same
/// instant the game showed both casts starting ("refused" at 17:53:56.410, "You ready a tuft of phoenix
/// down" at .415). Believing the call, neither sent its claim, so both cast on the same corpse.
/// </para>
/// </summary>
public sealed class PhoenixDownCastTrackerTests
{
    private DateTime _now = new(2026, 9, 25, 17, 53, 56, DateTimeKind.Utc);
    private readonly PhoenixDownCastTracker _tracker;

    public PhoenixDownCastTrackerTests() => _tracker = new PhoenixDownCastTracker(() => _now);

    private void Advance(double s) => _now = _now.AddSeconds(s);

    /// <summary>
    /// THE regression. The cast bar appearing is what counts — that is the moment to claim, whatever
    /// the use-item call said.
    /// </summary>
    [Fact]
    public void TheCastBarAppearing_IsTheStart()
    {
        _tracker.BeginAttempt("Xia Discord");
        Advance(0.005);

        Assert.Equal(PhoenixDownAttemptOutcome.Started, _tracker.Observe(true, 0.005f, 8f));
        Assert.Equal("Xia Discord", _tracker.TargetName);
    }

    /// <summary>Started is reported once, so the claim goes out once, not every frame of the cast.</summary>
    [Fact]
    public void StartedIsReportedOnce()
    {
        _tracker.BeginAttempt("Xia Discord");
        _tracker.Observe(true, 0.1f, 8f);

        for (var t = 0.2f; t < 7f; t += 0.5f)
            Assert.Equal(PhoenixDownAttemptOutcome.None, _tracker.Observe(true, t, 8f));
    }

    /// <summary>A cast that runs to the end used the item — that, and only that, starts the recast.</summary>
    [Fact]
    public void ACastThatRunsToTheEnd_Completed()
    {
        _tracker.BeginAttempt("Xia Discord");
        _tracker.Observe(true, 0.1f, 8f);
        _tracker.Observe(true, 7.98f, 8f);

        Assert.Equal(PhoenixDownAttemptOutcome.Completed, _tracker.Observe(false, 0f, 0f));
        Assert.False(_tracker.InFlight);
    }

    /// <summary>
    /// The field case: cancelled about a second from the end, which spends nothing. Treating it as used
    /// would lock the item's recast and block every retry for the rest of the fight.
    /// </summary>
    [Fact]
    public void ACastThatStopsEarly_WasCancelled()
    {
        _tracker.BeginAttempt("Xia Discord");
        _tracker.Observe(true, 0.1f, 8f);
        _tracker.Observe(true, 6.5f, 8f);

        Assert.Equal(PhoenixDownAttemptOutcome.Cancelled, _tracker.Observe(false, 0f, 0f));
    }

    [Fact]
    public void NoCastBarWithinTheWindow_WasRefused()
    {
        _tracker.BeginAttempt("Xia Discord");
        Advance(PhoenixDownCastTracker.ConfirmWindowSeconds - 0.1);
        Assert.Equal(PhoenixDownAttemptOutcome.None, _tracker.Observe(false, 0f, 0f));

        Advance(0.2);
        Assert.Equal(PhoenixDownAttemptOutcome.Refused, _tracker.Observe(false, 0f, 0f));
        Assert.False(_tracker.InFlight);
    }

    [Fact]
    public void WithNoAttemptThereIsNothingToReport()
        => Assert.Equal(PhoenixDownAttemptOutcome.None, _tracker.Observe(true, 3f, 8f));
}

/// <summary>
/// Who goes first when several toons decide in the same second. Every toon checks once a second and
/// they all see the last healer fall on the same tick, so they decide together.
/// </summary>
public sealed class PhoenixDownStaggerTests
{
    private static readonly uint[] Party = [3001, 1002, 2003];   // living non-tanks, any order

    [Theory]
    [InlineData(1002u, 0)]
    [InlineData(2003u, 1)]
    [InlineData(3001u, 2)]
    public void TheOrderIsByEntityId_SoEveryToonAgrees(uint self, int expectedRank)
        => Assert.Equal(expectedRank, PhoenixDownStagger.RankOf(self, selfIsTank: false, Party));

    /// <summary>
    /// A designated off-tank isn't in anyone else's list, so it goes after all of them rather than
    /// sharing a slot with someone who also thinks it is their turn.
    /// </summary>
    /// <remarks>
    /// The tank's id is deliberately the SMALLEST: with a large one it sorts last anyway, and the test
    /// passed with the tank rule deleted (caught by mutation testing).
    /// </remarks>
    [Fact]
    public void ATankThatMayCast_GoesLast()
        => Assert.Equal(Party.Length, PhoenixDownStagger.RankOf(1, selfIsTank: true, Party));

    [Theory]
    [InlineData(0, 0.0, true)]    // first in line goes at once
    [InlineData(1, 0.0, false)]
    [InlineData(1, PhoenixDownStagger.StaggerSeconds - 0.1, false)]
    [InlineData(1, PhoenixDownStagger.StaggerSeconds, true)]
    [InlineData(2, PhoenixDownStagger.StaggerSeconds * 2, true)]
    public void EachTurnWaitsForTheOnesBefore(int rank, double sinceDown, bool mayFire)
        => Assert.Equal(mayFire, PhoenixDownStagger.MayFire(rank, sinceDown));

    /// <summary>The spacing has to comfortably outlast a same-machine claim, which arrives in milliseconds.</summary>
    [Fact]
    public void TheSpacingIsWorthASecondAtLeast()
        => Assert.InRange(PhoenixDownStagger.StaggerSeconds, 1.0, 3.0);
}
