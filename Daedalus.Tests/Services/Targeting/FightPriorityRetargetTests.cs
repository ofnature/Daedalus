using Daedalus.Services.Targeting;
using Xunit;

namespace Daedalus.Tests.Services.Targeting;

/// <summary>
/// Layer 4: the fight's priority list (Minerva's module) moves the hard target, because the GCDs and auto-attacks go to
/// the hard target. Crucible 2026-10-25: risen Zombie Pieces hit the beastmaster for 4-17s while she stayed on the
/// Voidmancer, and the Younger Tablitaur died with the Elder on 72% (the survivor enrages) although the module named
/// the fuller bull.
/// </summary>
public class FightPriorityRetargetTests
{
    private static bool Follow(
        EnemyTargetingStrategy strategy = EnemyTargetingStrategy.LowestHp,
        bool inCombat = true,
        bool nonEnemy = false,
        bool grace = false)
        => CombatRetargetPolicy.ShouldFollowFightPriority(strategy, inCombat, nonEnemy, grace);

    [Fact]
    public void Follows_the_fight_on_an_automatic_strategy_in_combat()
    {
        Assert.True(Follow());
        Assert.True(Follow(EnemyTargetingStrategy.HighestHp));
        Assert.True(Follow(EnemyTargetingStrategy.Nearest));
        Assert.True(Follow(EnemyTargetingStrategy.TankAssist));
    }

    [Fact]
    public void Leaves_an_explicit_target_strategy_alone()
    {
        // CurrentTarget / FocusTarget: what to hit is the player's call, as FindEnemyByStrategy already treats it.
        Assert.False(Follow(EnemyTargetingStrategy.CurrentTarget));
        Assert.False(Follow(EnemyTargetingStrategy.FocusTarget));
    }

    [Fact]
    public void Never_out_of_combat_on_an_ally_or_during_the_manual_grace()
    {
        Assert.False(Follow(inCombat: false));
        Assert.False(Follow(nonEnemy: true));
        Assert.False(Follow(grace: true));
    }

    [Fact]
    public void Switches_to_the_first_named_enemy_in_reach()
    {
        // On the boss, a zombie is named: go.
        Assert.True(CombatRetargetPolicy.ShouldSwitchToPriority(currentHardTarget: 1, pick: 2));
        // No hard target at all: take the named one.
        Assert.True(CombatRetargetPolicy.ShouldSwitchToPriority(currentHardTarget: 0, pick: 2));
        // On a Great Wisp (also named, lower) when a Wisp heads the list: the Wisp.
        Assert.True(CombatRetargetPolicy.ShouldSwitchToPriority(currentHardTarget: 5, pick: 3));
    }

    [Fact]
    public void Stays_when_the_pick_is_the_target_and_does_nothing_without_one()
    {
        // Minerva lists the player's own target first among equal priorities, so on one of several equal adds the pick is
        // that add: no hopping.
        Assert.False(CombatRetargetPolicy.ShouldSwitchToPriority(currentHardTarget: 2, pick: 2));
        // Nothing in reach.
        Assert.False(CombatRetargetPolicy.ShouldSwitchToPriority(currentHardTarget: 1, pick: 0));
    }
}
