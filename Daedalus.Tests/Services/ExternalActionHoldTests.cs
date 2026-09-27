using Daedalus.Services;

namespace Daedalus.Tests.Services;

/// <summary>
/// The short owned hold another plugin can put on every action — Odysseus, using a quest item on a
/// mob that has to be left alive below half health.
/// </summary>
public class ExternalActionHoldTests
{
    private static readonly DateTime T0 = new(2026, 9, 26, 22, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Nothing_is_held_until_someone_asks()
    {
        var hold = new ExternalActionHold();
        Assert.False(hold.IsHeld(T0));
        Assert.Null(hold.HeldBy(T0));
    }

    [Fact]
    public void A_hold_is_in_force_until_it_is_released()
    {
        var hold = new ExternalActionHold();
        Assert.True(hold.Set("odysseus", true, T0));
        Assert.True(hold.IsHeld(T0.AddSeconds(1)));
        Assert.Equal("odysseus", hold.HeldBy(T0.AddSeconds(1)));

        Assert.True(hold.Set("odysseus", false, T0.AddSeconds(2)));
        Assert.False(hold.IsHeld(T0.AddSeconds(2)));
    }

    /// <summary>
    /// The safety of it: a holder that crashed or unloaded mid-fight cannot leave the character
    /// standing idle. Without being re-asserted, the hold lapses on its own.
    /// </summary>
    [Fact]
    public void A_hold_nobody_renews_lapses_by_itself()
    {
        var hold = new ExternalActionHold();
        hold.Set("odysseus", true, T0);

        Assert.True(hold.IsHeld(T0 + ExternalActionHold.Lease - TimeSpan.FromMilliseconds(1)));
        Assert.False(hold.IsHeld(T0 + ExternalActionHold.Lease));
    }

    [Fact]
    public void Renewing_extends_the_lease()
    {
        var hold = new ExternalActionHold();
        hold.Set("odysseus", true, T0);
        hold.Set("odysseus", true, T0.AddSeconds(2));   // re-asserted each tick while it is wanted

        Assert.True(hold.IsHeld(T0.AddSeconds(4)));    // past the first lease, inside the renewed one
    }

    /// <summary>Owned, like Minerva's preset slot: one plugin cannot take or release another's hold.</summary>
    [Fact]
    public void Another_plugin_can_neither_take_nor_release_a_hold_it_does_not_own()
    {
        var hold = new ExternalActionHold();
        hold.Set("odysseus", true, T0);

        Assert.False(hold.Set("henchman", true, T0.AddSeconds(1)));
        Assert.False(hold.Set("henchman", false, T0.AddSeconds(1)));
        Assert.Equal("odysseus", hold.HeldBy(T0.AddSeconds(1)));
    }

    [Fact]
    public void Once_a_hold_has_lapsed_another_plugin_may_take_it()
    {
        var hold = new ExternalActionHold();
        hold.Set("odysseus", true, T0);

        Assert.True(hold.Set("henchman", true, T0 + ExternalActionHold.Lease));
        Assert.Equal("henchman", hold.HeldBy(T0 + ExternalActionHold.Lease));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_hold_needs_an_owner(string owner)
    {
        var hold = new ExternalActionHold();
        Assert.False(hold.Set(owner, true, T0));
        Assert.False(hold.IsHeld(T0));
    }

    [Fact]
    public void Releasing_when_nothing_is_held_is_not_an_error()
    {
        var hold = new ExternalActionHold();
        Assert.True(hold.Set("odysseus", false, T0));
    }
}
