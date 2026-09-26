using Daedalus.Rotation.Phantom;
using Xunit;

namespace Daedalus.Tests.Rotation.Phantom;

/// <summary>
/// A healer locked into an 8-second Occult Comet can't answer the Necromancer's Deep Freeze call
/// (Doom, 10s, cleared only at full HP). The cast is cancelled and no new long cast starts.
/// </summary>
public class PhantomHealCallPolicyTests
{
    [Theory]
    [InlineData(41623u, 8.0f, true)]   // Occult Comet
    [InlineData(49084u, 6.0f, true)]   // Megaflare (Phantom Summoner)
    [InlineData(49080u, 4.0f, true)]   // Hellfire
    [InlineData(49071u, 2.3f, false)]  // Occult Holy: short, lands before the heal would be ready
    [InlineData(49068u, 8.0f, false)]  // Occult Cure III is a heal — never the obstacle
    [InlineData(24287u, 8.0f, false)]  // not a phantom action (a job spell is the job's business)
    public void WhatCountsAsALongPhantomCast(uint actionId, float cast, bool isLong)
        => Assert.Equal(isLong, PhantomHealCallPolicy.IsLongNonHealPhantomCast(actionId, cast));

    [Theory]
    [InlineData(true, true, true, 5f, true)]    // the case: healer, call, Comet with 5s to go
    [InlineData(false, true, true, 5f, false)]  // not a healer: its cast isn't the problem
    [InlineData(true, false, true, 5f, false)]  // nobody needs it
    [InlineData(true, true, false, 5f, false)]  // a short or healing cast
    [InlineData(true, true, true, 0.3f, false)] // about to land anyway
    public void Cancel(bool healer, bool call, bool longCast, float remaining, bool cancel)
        => Assert.Equal(cancel, PhantomHealCallPolicy.ShouldCancel(healer, call, longCast, remaining));

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, true, false, false)]
    public void HoldNewLongCasts(bool healer, bool call, bool longCast, bool hold)
        => Assert.Equal(hold, PhantomHealCallPolicy.ShouldHoldLongCast(healer, call, longCast));
}
