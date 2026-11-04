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

    /// <summary>A boss on 8% is finished before its adds (Voidmancer 100k vs a 10k Zombie Piece).</summary>
    [Fact]
    public void A_nearly_dead_boss_is_finished_before_its_adds()
    {
        Assert.True(CombatRetargetPolicy.ShouldFinishHeldTarget(0.08f, heldMaxHp: 100_000, pickMaxHp: 10_000));
        Assert.True(CombatRetargetPolicy.ShouldFinishHeldTarget(0.10f, 100_000, 10_000));
    }

    /// <summary>Above the line the adds still come first.</summary>
    [Fact]
    public void A_boss_with_more_left_still_yields_to_the_adds()
        => Assert.False(CombatRetargetPolicy.ShouldFinishHeldTarget(0.25f, 100_000, 10_000));

    /// <summary>
    /// Two bosses of a size (the Tablitaur bulls) are never held: the fight's "fuller one first" balancing must keep
    /// running, or the survivor enrages.
    /// </summary>
    [Fact]
    public void A_low_partner_of_equal_size_is_not_held()
        => Assert.False(CombatRetargetPolicy.ShouldFinishHeldTarget(0.05f, heldMaxHp: 60_000, pickMaxHp: 58_000));

    /// <summary>A dead or unread target holds nothing.</summary>
    [Fact]
    public void A_dead_target_is_not_held()
        => Assert.False(CombatRetargetPolicy.ShouldFinishHeldTarget(0f, 100_000, 10_000));

    /// <summary>Bound by a Crawling Piece (Bind 5710 until it dies): its source is the enemy to kill.</summary>
    [Fact]
    public void The_crawler_that_binds_you_is_the_target()
    {
        Assert.Equal(0x4000F42EUL, CombatRetargetPolicy.SourceToKill([(2593u, 0x40000001UL), (5710u, 0x4000F42EUL)]));
    }

    /// <summary>Other statuses, or a bind with no source, point at nobody.</summary>
    [Fact]
    public void Other_statuses_name_no_target()
    {
        Assert.Equal(0UL, CombatRetargetPolicy.SourceToKill([(2518u, 0x40000002UL)])); // another Bind: not "until it dies"
        Assert.Equal(0UL, CombatRetargetPolicy.SourceToKill([(5710u, 0xE0000000UL)]));
        Assert.Equal(0UL, CombatRetargetPolicy.SourceToKill([]));
    }

    // --- the Siren's crawler on Legendary (user 2026-10-30): hold off until it has grabbed the familiar ---

    private const uint Crawler = 14586;

    /// <summary>Replay 2026-10-30: it went for Diremite and Korha walked up and struck it — Bind and Damage Down on Korha.</summary>
    [Fact]
    public void ACrawlerGoingForTheFamiliarIsLeftAlone() =>
        Assert.True(CombatRetargetPolicy.HoldOffCrawler(true, Crawler, targetsPlayer: false, hasGrabbed: false));

    [Fact]
    public void ACrawlerThatHasGrabbedTheFamiliarIsKilled() =>
        Assert.False(CombatRetargetPolicy.HoldOffCrawler(true, Crawler, targetsPlayer: false, hasGrabbed: true));

    /// <summary>With no familiar out it comes for the beastmaster anyway: kill it at once.</summary>
    [Fact]
    public void ACrawlerComingForTheBeastmasterIsKilled() =>
        Assert.False(CombatRetargetPolicy.HoldOffCrawler(true, Crawler, targetsPlayer: true, hasGrabbed: false));

    [Fact]
    public void OffLegendaryTheCrawlerIsKilledAsBefore() =>
        Assert.False(CombatRetargetPolicy.HoldOffCrawler(false, Crawler, targetsPlayer: false, hasGrabbed: false));

    [Fact]
    public void OtherAddsAreNeverHeldOff() =>
        Assert.False(CombatRetargetPolicy.HoldOffCrawler(true, 14585, targetsPlayer: false, hasGrabbed: false)); // shambler

    [Fact]
    public void AMuchFullerLamiaIsTakenNext() =>
        Assert.Equal(2UL, CombatRetargetPolicy.BalancePick(0.40f, [(2UL, 0.80f)]));

    [Fact]
    public void LamiasWithinTheGapAreLeftAlone() =>
        Assert.Equal(0UL, CombatRetargetPolicy.BalancePick(0.40f, [(2UL, 0.49f)]));

    [Fact]
    public void TheFullestOfSeveralLamiasIsPicked() =>
        Assert.Equal(3UL, CombatRetargetPolicy.BalancePick(0.20f, [(2UL, 0.50f), (3UL, 0.90f)]));

    [Fact]
    public void ADeadLamiaIsNeverPicked() =>
        Assert.Equal(0UL, CombatRetargetPolicy.BalancePick(0.20f, [(2UL, 0f)]));

    [Fact]
    public void AFullerTargetNeverSwitchesToAnEmptierLamia() =>
        Assert.Equal(0UL, CombatRetargetPolicy.BalancePick(0.90f, [(2UL, 0.30f)]));
}
