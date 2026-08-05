using Daedalus.Services.Targeting;
using Xunit;

namespace Daedalus.Tests.Services.Targeting;

/// <summary>
/// The auto-retarget exists to recover from "the thing I was hitting died". It must never take the
/// target away from a player who is deliberately holding something else.
///
/// <para>
/// Field 2026-08-04: a Sage on a CONTROLLER could not select anything but the boss while in combat.
/// Cycling onto a party member set a hard target that is not an <c>IBattleNpc</c>, the damage
/// targeting classified that as "hard target invalid", and the retarget snapped straight back to
/// the boss on the next frame — so a healer could not manually target anyone to heal them.
/// </para>
/// </summary>
public class CombatRetargetSeizeTests
{
    /// <summary>The recovery case this feature exists for: enemy died, nothing else held.</summary>
    private static bool Seize(
        bool scenario = true,
        bool validEnemy = false,
        bool nonEnemy = false,
        bool grace = false)
        => CombatRetargetPolicy.ShouldSeizeHardTarget(scenario, validEnemy, nonEnemy, grace);

    [Fact]
    public void Seizes_when_the_enemy_died_and_nothing_else_is_held()
    {
        Assert.True(Seize());
    }

    [Fact]
    public void Never_seizes_an_ally_target()
    {
        // A party member is not an IBattleNpc, so the damage code calls it "invalid" — but the
        // player picked it on purpose, and for a healer it is the entire job.
        Assert.False(Seize(nonEnemy: true));
    }

    [Fact]
    public void Never_seizes_right_after_the_player_changed_target()
    {
        // Controller cycling and mouse clicks both land here via ManualControlGrace.
        Assert.False(Seize(grace: true));
    }

    [Fact]
    public void Leaves_a_valid_enemy_target_alone()
    {
        Assert.False(Seize(validEnemy: true));
    }

    [Fact]
    public void Does_nothing_outside_the_retarget_scenario()
    {
        // Not in combat, or the target is fine, or there is nothing else to hit.
        Assert.False(Seize(scenario: false));
        Assert.False(Seize(scenario: false, nonEnemy: true));
        Assert.False(Seize(scenario: false, grace: true));
    }

    [Fact]
    public void Player_intent_wins_over_every_combination()
    {
        // Whatever else is true, holding a non-enemy or having just retargeted stops the seize.
        foreach (var validEnemy in new[] { true, false })
        {
            Assert.False(Seize(validEnemy: validEnemy, nonEnemy: true));
            Assert.False(Seize(validEnemy: validEnemy, grace: true));
            Assert.False(Seize(validEnemy: validEnemy, nonEnemy: true, grace: true));
        }
    }

    [Fact]
    public void Recovery_resumes_once_the_grace_expires_and_the_ally_is_dropped()
    {
        // The guards are transient, not a permanent opt-out: drop back to no target with the
        // grace expired and the feature works again.
        Assert.True(Seize(nonEnemy: false, grace: false));
    }
}
