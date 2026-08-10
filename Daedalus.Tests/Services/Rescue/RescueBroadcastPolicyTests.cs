using Daedalus.Services.Rescue;
using Xunit;

namespace Daedalus.Tests.Services.Rescue;

/// <summary>
/// Sender-side "I won't make it" gating (docs/rescue-plan.md Phase 0). The core rule under
/// test: the toon panics only when it is STILL unsafe inside the panic window — a deliberate
/// soaker's own hints read safe, so it never reaches the unsafe gates at all.
/// </summary>
public sealed class RescueBroadcastPolicyTests
{
    /// <summary>A situation that fires — individual tests break one gate at a time.</summary>
    private static RescueBroadcastSituation Firing() => new(
        BroadcastEnabled: true,
        SelfAlive: true,
        InCombat: true,
        BossModAvailable: true,
        PositionSafe: false,
        UnsafeStreakFrames: RescueBroadcastPolicy.MinUnsafeSamples,
        ActivationInSeconds: 1.5f,
        DashActive: false);

    [Fact]
    public void StillUnsafeInsideThePanicWindow_Broadcasts()
    {
        var (broadcast, reason) = RescueBroadcastPolicy.Decide(Firing());

        Assert.True(broadcast, reason);
    }

    [Fact]
    public void SafePosition_NeverBroadcasts_EvenWithActivationImminent()
    {
        // The soaker case: own hints say safe (assigned towers are not forbidden to the
        // assignee), so no amount of imminent activation may trigger a broadcast.
        var s = Firing() with { PositionSafe = true, ActivationInSeconds = 0.5f };

        var (broadcast, reason) = RescueBroadcastPolicy.Decide(s);

        Assert.False(broadcast);
        Assert.Equal("position safe", reason);
    }

    [Fact]
    public void UnsafeButTimeRemains_Holds()
    {
        var s = Firing() with { ActivationInSeconds = RescueBroadcastPolicy.PanicSeconds + 0.1f };

        var (broadcast, reason) = RescueBroadcastPolicy.Decide(s);

        Assert.False(broadcast);
        Assert.Contains("still time", reason);
    }

    [Fact]
    public void SingleUnsafeFrame_IsDebounced()
    {
        var s = Firing() with { UnsafeStreakFrames = RescueBroadcastPolicy.MinUnsafeSamples - 1 };

        var (broadcast, reason) = RescueBroadcastPolicy.Decide(s);

        Assert.False(broadcast);
        Assert.Contains("debouncing", reason);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("dead")]
    [InlineData("ooc")]
    [InlineData("nobmr")]
    [InlineData("dash")]
    public void EachStructuralGate_Holds(string gate)
    {
        var s = gate switch
        {
            "disabled" => Firing() with { BroadcastEnabled = false },
            "dead" => Firing() with { SelfAlive = false },
            "ooc" => Firing() with { InCombat = false },
            "nobmr" => Firing() with { BossModAvailable = false },
            _ => Firing() with { DashActive = true },
        };

        Assert.False(RescueBroadcastPolicy.Decide(s).Broadcast);
    }

    [Fact]
    public void PanicWindow_ExceedsTheEndToEndBudget()
    {
        // Signal → pull is ~150–400ms; the panic window must leave the healer room to act,
        // and the healer-side abort floor must sit inside it.
        Assert.True(RescueBroadcastPolicy.PanicSeconds > RescuePolicy.AbortSeconds + 0.4f);
        Assert.True(RescuePolicy.RequestTtlSeconds > RescueBroadcastPolicy.RebroadcastIntervalSeconds * 2,
            "one dropped datagram must not expire a live danger");
    }
}
