using Daedalus.Services.Targeting;
using Xunit;

namespace Daedalus.Tests.Services.Targeting;

/// <summary>
/// Split-boss recovery: when the followed enemy cannot be hit but another one can, the hard target
/// has to move.
///
/// <para>
/// Field 2026-08-07, the Shinryu encounter. The boss occupies two STACKED platforms. Dropping to
/// the lower floor leaves the upper section only a dozen or so yalms away — comfortably in range —
/// but behind a solid floor. Two independent faults kept the target welded to it:
/// </para>
/// <list type="number">
/// <item>Reachability was judged on RANGE alone, so "in range, through a floor" read as perfectly
/// fine and the recovery bailed on its first condition.</item>
/// <item>The recovery only ran when the strategy found NOTHING. It found the reachable lower
/// section, so the branch was skipped entirely and the hard target was never rewritten.</item>
/// </list>
/// </summary>
public class UnreachableRetargetTests
{
    [Fact]
    public void Writes_the_hard_target_even_when_the_strategy_already_found_one()
    {
        // The Shinryu case. Gating this on "no candidate" is what let the rotation attack the
        // lower boss part while the player stayed locked onto the upper one.
        Assert.True(CombatRetargetPolicy.ShouldWriteReachableHardTarget(
            heldTargetUnreachable: true,
            haveReachableCandidate: true,
            candidateIsAlreadyHardTarget: false));
    }

    [Fact]
    public void Does_nothing_when_the_held_target_is_fine()
    {
        Assert.False(CombatRetargetPolicy.ShouldWriteReachableHardTarget(
            heldTargetUnreachable: false,
            haveReachableCandidate: true,
            candidateIsAlreadyHardTarget: false));
    }

    [Fact]
    public void Does_nothing_when_there_is_nowhere_to_switch()
    {
        // Unreachable with no alternative is a movement problem, not a targeting one — dropping
        // the target here would leave the player with nothing at all.
        Assert.False(CombatRetargetPolicy.ShouldWriteReachableHardTarget(
            heldTargetUnreachable: true,
            haveReachableCandidate: false,
            candidateIsAlreadyHardTarget: false));
    }

    [Fact]
    public void Never_rewrites_the_target_it_already_holds()
    {
        // Guards against a per-frame write loop: re-writing the same id would re-arm the manual
        // control grace every frame and fight the player for the stick.
        Assert.False(CombatRetargetPolicy.ShouldWriteReachableHardTarget(
            heldTargetUnreachable: true,
            haveReachableCandidate: true,
            candidateIsAlreadyHardTarget: true));
    }

    [Fact]
    public void Sustained_unreachability_is_still_required_before_acting()
    {
        // The grace window is what keeps a flickering line-of-sight raycast from bouncing the
        // target — the Scholar Art-of-War alternation is the precedent.
        Assert.False(CombatRetargetPolicy.ShouldRetargetUnreachable(
            featureEnabled: true,
            playerInCombat: true,
            heldTargetIsLivingEnemy: true,
            heldTargetInRange: false,
            gracePassed: false,
            hasReachableAlternative: true));

        Assert.True(CombatRetargetPolicy.ShouldRetargetUnreachable(
            featureEnabled: true,
            playerInCombat: true,
            heldTargetIsLivingEnemy: true,
            heldTargetInRange: false,
            gracePassed: true,
            hasReachableAlternative: true));
    }

    [Fact]
    public void The_feature_toggle_still_wins()
    {
        Assert.False(CombatRetargetPolicy.ShouldRetargetUnreachable(
            featureEnabled: false,
            playerInCombat: true,
            heldTargetIsLivingEnemy: true,
            heldTargetInRange: false,
            gracePassed: true,
            hasReachableAlternative: true));
    }
}
