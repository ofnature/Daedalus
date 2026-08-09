namespace Daedalus.Services.Targeting;

/// <summary>
/// Pure policy helpers for combat death retargeting. Extracted so unit tests
/// can validate the three-layer rules without Dalamud runtime mocks.
/// </summary>
internal static class CombatRetargetPolicy
{
    public static bool IsAggregateStrategy(EnemyTargetingStrategy strategy) =>
        strategy is EnemyTargetingStrategy.LowestHp
            or EnemyTargetingStrategy.HighestHp
            or EnemyTargetingStrategy.Nearest
            or EnemyTargetingStrategy.TankAssist;

    /// <summary>
    /// Layer 2: do not pause damage targeting when the player is in combat,
    /// the hard target is invalid, and live hostiles are nearby.
    /// </summary>
    public static bool ShouldUnpauseForCombatRetarget(
        bool pauseWhenNoTarget,
        bool hasValidUserSelectedEnemy,
        bool hasLiveStickyTarget,
        bool playerInCombat,
        bool hardTargetInvalid,
        bool hasLiveHostilesNearby)
    {
        if (!pauseWhenNoTarget)
            return true;

        if (hasValidUserSelectedEnemy || hasLiveStickyTarget)
            return true;

        return playerInCombat && hardTargetInvalid && hasLiveHostilesNearby;
    }

    /// <summary>
    /// Layer 3: relax StrictCurrentTargetStrategy fallback when target died mid-combat
    /// and the configured strategy is aggregate.
    /// </summary>
    public static bool ShouldRelaxStrictOnCombatDeath(
        bool strictCurrentTargetStrategy,
        EnemyTargetingStrategy enemyStrategy,
        bool isCombatRetargetScenario) =>
        isCombatRetargetScenario
        && strictCurrentTargetStrategy
        && IsAggregateStrategy(enemyStrategy);

    /// <summary>
    /// Layer 1 gate: may the auto-retarget SEIZE the player's hard target and point it at an enemy?
    ///
    /// <para>
    /// This exists to recover from "the thing I was hitting died" — nothing else. Two situations
    /// look like an invalid target to the damage code but are in fact the player driving:
    /// </para>
    /// <list type="bullet">
    /// <item>They are holding a non-enemy — a party member, a friendly NPC, an object. Field
    /// 2026-08-04: a Sage on a controller could not keep anything but the boss selected in combat,
    /// because every cycle onto an ally was classified "invalid" and snapped back to the boss on
    /// the next frame. That is most of a healer's job made impossible.</item>
    /// <item>They changed target within the manual-control grace. Movement pulses already yield to
    /// that; targeting must too, or the plugin fights the player for the stick.</item>
    /// </list>
    /// </summary>
    /// <param name="isCombatRetargetScenario">In combat, hard target invalid, live hostiles nearby.</param>
    /// <param name="hasValidUserSelectedEnemy">The hard target already resolves to a live enemy.</param>
    /// <param name="holdingNonEnemyTarget">A target is set and it is not an enemy (ally / NPC / object).</param>
    /// <param name="manualControlGraceActive">The user changed target within the grace window.</param>
    public static bool ShouldSeizeHardTarget(
        bool isCombatRetargetScenario,
        bool hasValidUserSelectedEnemy,
        bool holdingNonEnemyTarget,
        bool manualControlGraceActive)
    {
        if (!isCombatRetargetScenario)
            return false;

        if (hasValidUserSelectedEnemy)
            return false;

        if (holdingNonEnemyTarget)
            return false;

        if (manualControlGraceActive)
            return false;

        return true;
    }

    /// <summary>
    /// Strategy used to pick the game-target write on combat death retarget.
    /// Explicit strategies (CurrentTarget/FocusTarget) fall back to LowestHp for the pick.
    /// </summary>
    public static EnemyTargetingStrategy ResolveAutoRetargetStrategy(EnemyTargetingStrategy configured) =>
        IsAggregateStrategy(configured) ? configured : EnemyTargetingStrategy.LowestHp;

    /// <summary>
    /// Having decided the held target is unreachable, should we actually WRITE a new hard target?
    ///
    /// <para>
    /// The subtle part is that this must fire even when the targeting strategy already produced a
    /// candidate. Field 2026-08-07, the Shinryu encounter's two stacked platforms: from the lower
    /// floor the strategy happily returned the reachable lower boss section, so the recovery branch
    /// — which was gated on "the strategy found nothing" — never ran. The rotation quietly attacked
    /// the lower part while the player's hard target stayed locked on the unreachable upper one,
    /// which is precisely the symptom the feature exists to prevent.
    /// </para>
    /// </summary>
    /// <param name="heldTargetUnreachable">The followed target cannot be hit (range or line of sight).</param>
    /// <param name="haveReachableCandidate">A reachable enemy is available to switch to.</param>
    /// <param name="candidateIsAlreadyHardTarget">That candidate is already the hard target.</param>
    public static bool ShouldWriteReachableHardTarget(
        bool heldTargetUnreachable,
        bool haveReachableCandidate,
        bool candidateIsAlreadyHardTarget)
        => heldTargetUnreachable
           && haveReachableCandidate
           && !candidateIsAlreadyHardTarget;

    /// <summary>
    /// Unreachable-target retarget (split-boss recovery): switch off a followed target that is a
    /// valid living enemy but out of effective action range, when a reachable attackable hostile
    /// exists. Gated on a grace period so a brief out-of-range blip (knockback, repositioning) and
    /// normal gap-closing to a single far target never trigger it.
    /// </summary>
    /// <param name="featureEnabled"><see cref="TargetingConfig.RetargetUnreachableTarget"/>.</param>
    /// <param name="playerInCombat">Player is effectively in combat.</param>
    /// <param name="heldTargetIsLivingEnemy">The followed target resolves to a live, attackable enemy (not gone/dead/immune).</param>
    /// <param name="heldTargetInRange">The followed target is within effective action range.</param>
    /// <param name="gracePassed">The target has been continuously out of range past the grace window.</param>
    /// <param name="hasReachableAlternative">Another attackable hostile is in range.</param>
    public static bool ShouldRetargetUnreachable(
        bool featureEnabled,
        bool playerInCombat,
        bool heldTargetIsLivingEnemy,
        bool heldTargetInRange,
        bool gracePassed,
        bool hasReachableAlternative) =>
        featureEnabled
        && playerInCombat
        && heldTargetIsLivingEnemy
        && !heldTargetInRange
        && gracePassed
        && hasReachableAlternative;
}
