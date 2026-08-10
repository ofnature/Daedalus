using Daedalus.Services.Rescue;
using Xunit;

namespace Daedalus.Tests.Services.Rescue;

/// <summary>
/// Healer-side fire gating (docs/rescue-plan.md Phase 0). The healer asserts only what it can
/// know locally — every gate here is healer-side state; the target's danger is taken on the
/// sender's word (own-hints rule) and only its freshness is checked.
/// </summary>
public sealed class RescuePolicyTests
{
    /// <summary>A situation that fires — individual tests break one gate at a time.</summary>
    private static RescueSituation Firing() => new(
        AutoRescueEnabled: true,
        SelfAlive: true,
        RescueLearned: true,
        RescueReady: true,
        RequestAgeSeconds: 0.1f,
        ActivationRemainingSeconds: 1.4f,
        SecondsSinceClaimByOther: float.MaxValue,
        ElectionSatisfied: true,
        TargetAlive: true,
        TargetInLocalParty: true,
        TargetDistanceYalms: 18f,
        TargetKnockbackImmune: false,
        SelfPositionSafe: true,
        SelfActivationInSeconds: float.MaxValue,
        HeightDeltaYalms: 0.5f,
        SecondsSinceTargetLastPulled: float.MaxValue);

    [Fact]
    public void AllGatesPass_Fires()
    {
        var (fire, reason) = RescuePolicy.Decide(Firing());

        Assert.True(fire, reason);
    }

    [Fact]
    public void StaleRequest_Holds_ToonEscapedOrDied()
    {
        var s = Firing() with { RequestAgeSeconds = RescuePolicy.RequestTtlSeconds + 0.1f };

        var (fire, reason) = RescuePolicy.Decide(s);

        Assert.False(fire);
        Assert.Contains("stale", reason);
    }

    [Fact]
    public void ClaimByAnotherHealer_StandsDown()
    {
        var s = Firing() with { SecondsSinceClaimByOther = RescuePolicy.ClaimHoldOffSeconds - 0.5f };

        Assert.False(RescuePolicy.Decide(s).Fire);

        // …and an old claim no longer suppresses.
        var expired = Firing() with { SecondsSinceClaimByOther = RescuePolicy.ClaimHoldOffSeconds + 0.5f };
        Assert.True(RescuePolicy.Decide(expired).Fire);
    }

    [Fact]
    public void TooLate_ThePullWouldLandAfterTheHit()
    {
        var s = Firing() with { ActivationRemainingSeconds = RescuePolicy.AbortSeconds };

        var (fire, reason) = RescuePolicy.Decide(s);

        Assert.False(fire);
        Assert.Contains("too late", reason);
    }

    [Fact]
    public void UnsafeDestination_Holds_BothForms()
    {
        // Standing in the bad ourselves…
        Assert.False(RescuePolicy.Decide(Firing() with { SelfPositionSafe = false }).Fire);

        // …or on "safety" that activates too soon — not a destination either way.
        var soon = Firing() with { SelfActivationInSeconds = RescuePolicy.DestSafetyActivationSeconds - 0.5f };
        Assert.False(RescuePolicy.Decide(soon).Fire);
    }

    [Fact]
    public void RangeAndHeight_GuardThePullGeometry()
    {
        Assert.False(RescuePolicy.Decide(Firing() with { TargetDistanceYalms = RescuePolicy.MaxPullRangeYalms + 1f }).Fire);
        Assert.True(RescuePolicy.Decide(Firing() with { TargetDistanceYalms = RescuePolicy.MaxPullRangeYalms }).Fire);

        Assert.False(RescuePolicy.Decide(Firing() with { HeightDeltaYalms = RescuePolicy.MaxHeightDeltaYalms + 0.5f }).Fire);
    }

    [Fact]
    public void KnockbackImmuneTarget_IsNeverPulled()
    {
        // Surecast/Arm's Length make Rescue a no-op — the 120s cooldown must not burn on it.
        var (fire, reason) = RescuePolicy.Decide(Firing() with { TargetKnockbackImmune = true });

        Assert.False(fire);
        Assert.Contains("knockback-immune", reason);
    }

    [Fact]
    public void RecentlyPulledTarget_IsNotChainYanked()
    {
        var s = Firing() with { SecondsSinceTargetLastPulled = RescuePolicy.PerTargetRepullCooldownSeconds - 1f };

        Assert.False(RescuePolicy.Decide(s).Fire);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("dead")]
    [InlineData("unlearned")]
    [InlineData("cooldown")]
    [InlineData("election")]
    [InlineData("targetdead")]
    [InlineData("notparty")]
    public void EachStructuralGate_Holds(string gate)
    {
        var s = gate switch
        {
            "disabled" => Firing() with { AutoRescueEnabled = false },
            "dead" => Firing() with { SelfAlive = false },
            "unlearned" => Firing() with { RescueLearned = false },
            "cooldown" => Firing() with { RescueReady = false },
            "election" => Firing() with { ElectionSatisfied = false },
            "targetdead" => Firing() with { TargetAlive = false },
            _ => Firing() with { TargetInLocalParty = false },
        };

        Assert.False(RescuePolicy.Decide(s).Fire);
    }
}
