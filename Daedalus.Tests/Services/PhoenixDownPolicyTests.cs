using Daedalus.Services.Consumables;
using Xunit;

namespace Daedalus.Tests.Services;

public class PhoenixDownPolicyTests
{
    /// <summary>A situation that passes every gate — each test breaks exactly one thing.</summary>
    private static PhoenixDownSituation Firing() => new(
        Enabled: true,
        InCombat: true,
        SelfAlive: true,
        SelfCasting: false,
        SelfIsTank: false,
        SelfIsDesignatedOffTank: false,
        LivingOthers: 2,
        HealersPresent: true,
        AllHealersDead: true,
        TargetFound: true,
        TargetDistanceYalms: 10f,
        ItemCount: 3,
        SecondsSinceOwnUse: 999,
        SecondsSinceOwnAttempt: 999,
        SecondsSinceForeignClaim: 999,
        IsMoving: false);

    [Fact]
    public void Fires_when_every_gate_passes()
    {
        var (fire, _) = PhoenixDownPolicy.Decide(Firing());
        Assert.True(fire);
    }

    [Fact]
    public void Disabled_never_fires()
    {
        var (fire, reason) = PhoenixDownPolicy.Decide(Firing() with { Enabled = false });
        Assert.False(fire);
        Assert.Contains("disabled", reason);
    }

    [Fact]
    public void Dead_self_never_fires()
    {
        var (fire, _) = PhoenixDownPolicy.Decide(Firing() with { SelfAlive = false });
        Assert.False(fire);
    }

    [Fact]
    public void Out_of_combat_defers_to_normal_raises()
    {
        var (fire, _) = PhoenixDownPolicy.Decide(Firing() with { InCombat = false });
        Assert.False(fire);
    }

    [Fact]
    public void No_healers_in_party_never_fires()
    {
        var (fire, reason) = PhoenixDownPolicy.Decide(
            Firing() with { HealersPresent = false, AllHealersDead = false });
        Assert.False(fire);
        Assert.Contains("no healers", reason);
    }

    [Fact]
    public void A_living_healer_holds_it()
    {
        var (fire, reason) = PhoenixDownPolicy.Decide(Firing() with { AllHealersDead = false });
        Assert.False(fire);
        Assert.Contains("healer lives", reason);
    }

    [Fact]
    public void Tank_holds_while_anyone_else_lives()
    {
        // User rule 2026-08-03: the MT never plants an 8s hardcast mid-fight.
        var (fire, reason) = PhoenixDownPolicy.Decide(Firing() with { SelfIsTank = true });
        Assert.False(fire);
        Assert.Contains("tank holds", reason);
    }

    [Fact]
    public void Tank_fires_as_the_last_one_alive()
    {
        var (fire, _) = PhoenixDownPolicy.Decide(
            Firing() with { SelfIsTank = true, LivingOthers = 0 });
        Assert.True(fire);
    }

    [Fact]
    public void Designated_off_tank_is_exempt_from_the_tank_hold()
    {
        var (fire, _) = PhoenixDownPolicy.Decide(
            Firing() with { SelfIsTank = true, SelfIsDesignatedOffTank = true });
        Assert.True(fire);
    }

    [Fact]
    public void Empty_inventory_never_fires()
    {
        var (fire, reason) = PhoenixDownPolicy.Decide(Firing() with { ItemCount = 0 });
        Assert.False(fire);
        Assert.Contains("inventory", reason);
    }

    [Fact]
    public void Own_recast_holds_for_360s()
    {
        var (fire, _) = PhoenixDownPolicy.Decide(Firing() with { SecondsSinceOwnUse = 300 });
        Assert.False(fire);

        var (fireAfter, _) = PhoenixDownPolicy.Decide(Firing() with { SecondsSinceOwnUse = 361 });
        Assert.True(fireAfter);
    }

    [Fact]
    public void Refused_use_backs_off_before_retrying()
    {
        var (fire, _) = PhoenixDownPolicy.Decide(Firing() with { SecondsSinceOwnAttempt = 5 });
        Assert.False(fire);
    }

    [Fact]
    public void Foreign_claim_holds_off_so_only_one_toon_burns_an_item()
    {
        var (fire, reason) = PhoenixDownPolicy.Decide(Firing() with { SecondsSinceForeignClaim = 3 });
        Assert.False(fire);
        Assert.Contains("another toon", reason);

        var (fireAfter, _) = PhoenixDownPolicy.Decide(
            Firing() with { SecondsSinceForeignClaim = PhoenixDownPolicy.ClaimHoldOffSeconds + 1 });
        Assert.True(fireAfter);
    }

    [Fact]
    public void No_raisable_target_never_fires()
    {
        // All dead healers already have a raise pending (status 148) — don't waste the item.
        var (fire, _) = PhoenixDownPolicy.Decide(Firing() with { TargetFound = false });
        Assert.False(fire);
    }

    [Fact]
    public void Out_of_range_target_reports_the_distance()
    {
        var (fire, reason) = PhoenixDownPolicy.Decide(
            Firing() with { TargetDistanceYalms = 22f });
        Assert.False(fire);
        Assert.Contains("22", reason);
        Assert.Contains("out of", reason);
    }

    [Fact]
    public void Boundary_range_still_fires()
    {
        var (fire, _) = PhoenixDownPolicy.Decide(
            Firing() with { TargetDistanceYalms = PhoenixDownPolicy.RangeYalms });
        Assert.True(fire);
    }

    [Fact]
    public void Already_casting_holds()
    {
        var (fire, _) = PhoenixDownPolicy.Decide(Firing() with { SelfCasting = true });
        Assert.False(fire);
    }

    [Fact]
    public void Moving_holds_the_hardcast()
    {
        var (fire, reason) = PhoenixDownPolicy.Decide(Firing() with { IsMoving = true });
        Assert.False(fire);
        Assert.Contains("moving", reason);
    }
}
