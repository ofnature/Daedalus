using System.Numerics;
using Moq;
using Daedalus.Data;
using Daedalus.Services.Action;
using Daedalus.Services.Positional;
using Daedalus.Services.Positional.Navigation;

namespace Daedalus.Tests.Services.Positional.Navigation;

public class PositionalMovementServiceTests
{
    private readonly Mock<IMovementArbiter> _vNav = new();
    private readonly Mock<IBossModSafetyService> _bossMod = new();
    private readonly Mock<IActionService> _action = new();
    private readonly TestAnticipationProvider _anticipation = new();

    public PositionalMovementServiceTests()
    {
        _bossMod.Setup(x => x.ShouldAbortMovement()).Returns(false);
        _bossMod.Setup(x => x.QueryPositionSafety(It.IsAny<Vector3>(), It.IsAny<float>()))
            .Returns(PositionSafety.Safe);
        _bossMod.Setup(x => x.IsSegmentSafe(It.IsAny<Vector3>(), It.IsAny<Vector3>())).Returns(true);

        _action.Setup(x => x.GcdRemaining).Returns(2.0f);
        _action.Setup(x => x.IsCasting).Returns(false);
        _action.Setup(x => x.AnimationLockRemaining).Returns(0f);

        _vNav.Setup(x => x.IsPathRunning).Returns(false);
        _vNav.Setup(x => x.IsPathfindInProgress).Returns(false);
        _vNav.Setup(x => x.SnapToFloor(It.IsAny<Vector3>())).Returns<Vector3>(v => v);
        _vNav.Setup(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), It.IsAny<bool>()))
            .Returns(VNavMoveResult.Queued);
        _vNav.Setup(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), It.IsAny<MovementIntent>(), It.IsAny<bool>()))
            .Returns(VNavMoveResult.Queued);
    }

    [Fact]
    public void Update_BeginUpdateSnapshotCalledEachTick()
    {
        var service = CreateService();
        _anticipation.Next = null;

        service.Update(CreateRequest());

        _bossMod.Verify(x => x.BeginUpdateSnapshot(), Times.Once);
    }

    [Fact]
    public void Update_WhenAnticipatedAndSafe_QueuesVNavMove()
    {
        var service = CreateService();
        _anticipation.Next = new PositionalAnticipation(PositionalType.Rear, 7481, PositionalAnticipationReason.ComboSetup);

        service.Update(CreateRequest());

        Assert.Equal(PositionalMovementPhase.Moving, service.State.Phase);
        _vNav.Verify(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), MovementIntent.PositionalArc, false), Times.Once);
    }

    [Fact]
    public void Update_WhenUnsafe_SkipsWithoutStalling()
    {
        var service = CreateService();
        _anticipation.Next = new PositionalAnticipation(PositionalType.Rear, 7481, PositionalAnticipationReason.ComboSetup);
        _bossMod.Setup(x => x.QueryPositionSafety(It.IsAny<Vector3>(), It.IsAny<float>()))
            .Returns(PositionSafety.Unsafe);

        service.Update(CreateRequest());

        Assert.Equal(PositionalMovementPhase.Skipped, service.State.Phase);
        _vNav.Verify(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), It.IsAny<bool>()), Times.Never);
        _vNav.Verify(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), It.IsAny<MovementIntent>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void Update_WhenGcdBudgetExhausted_SkipsMove()
    {
        var service = CreateService();
        _anticipation.Next = new PositionalAnticipation(PositionalType.Rear, 7481, PositionalAnticipationReason.ComboSetup);
        _action.Setup(x => x.GcdRemaining).Returns(0.10005f);

        service.Update(CreateRequest());

        Assert.Equal(PositionalMovementPhase.Skipped, service.State.Phase);
        Assert.Equal("would clip GCD", service.State.SkipReason);
    }

    [Fact]
    public void Update_WhenGcdReady_QueuesPositionalMove()
    {
        var service = CreateService();
        _anticipation.Next = new PositionalAnticipation(PositionalType.Rear, 7481, PositionalAnticipationReason.ComboSetup);
        _action.Setup(x => x.GcdRemaining).Returns(0f);

        service.Update(CreateRequest());

        Assert.Equal(PositionalMovementPhase.Moving, service.State.Phase);
        _vNav.Verify(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), MovementIntent.PositionalArc, false), Times.Once);
    }

    [Fact]
    public void Update_WhenAnimationLockHigh_AllowsMoveForInstantCastJob()
    {
        var service = CreateService();
        _anticipation.Next = new PositionalAnticipation(PositionalType.Rear, 7481, PositionalAnticipationReason.ComboSetup);
        _action.Setup(x => x.AnimationLockRemaining).Returns(0.45f);

        service.Update(CreateRequest(allowMovementDuringActionLock: true));

        Assert.Equal(PositionalMovementPhase.Moving, service.State.Phase);
        _vNav.Verify(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), MovementIntent.PositionalArc, false), Times.Once);
    }

    [Fact]
    public void Update_WhenNotInCombat_DoesNotStopUserVNavPath()
    {
        var service = CreateService();
        _vNav.Setup(x => x.IsPathRunning).Returns(true);

        service.Update(CreateRequest() with { InCombat = false });

        Assert.Equal(PositionalMovementPhase.Skipped, service.State.Phase);
        _vNav.Verify(x => x.Stop(), Times.Never);
    }

    [Fact]
    public void Update_WhenLeavingCombat_StopsOwnedVNavPath()
    {
        var service = CreateService();
        _anticipation.Next = new PositionalAnticipation(PositionalType.Rear, 7481, PositionalAnticipationReason.ComboSetup);
        service.Update(CreateRequest());
        Assert.Equal(PositionalMovementPhase.Moving, service.State.Phase);

        _vNav.Setup(x => x.IsPathRunning).Returns(true);
        service.Update(CreateRequest() with { InCombat = false });

        _vNav.Verify(x => x.Stop(), Times.Once);
        Assert.Equal(PositionalMovementPhase.Skipped, service.State.Phase);
    }

    [Fact]
    public void Update_WhenTelegraphAppears_AbortsRunningPath()
    {
        var service = CreateService();
        _vNav.Setup(x => x.IsPathRunning).Returns(true);
        _bossMod.Setup(x => x.ShouldAbortMovement()).Returns(true);

        service.Update(CreateRequest());

        Assert.Equal(PositionalMovementPhase.Aborted, service.State.Phase);
        _vNav.Verify(x => x.Stop(), Times.Once);
    }

    [Fact]
    public void Update_WhenAlreadyAtRear_SkipsMove()
    {
        var service = CreateService();
        _anticipation.Next = new PositionalAnticipation(PositionalType.Rear, 7481, PositionalAnticipationReason.ComboSetup);

        var ctx = BaseAnticipationContext with { IsAtRear = true };
        service.Update(CreateRequest(anticipationContext: ctx));

        Assert.Equal(PositionalMovementPhase.Skipped, service.State.Phase);
        Assert.Equal("already at positional", service.State.SkipReason);
    }

    [Fact]
    public void Update_WhenMaintainAndHuggingTarget_DoesNotBackOff()
    {
        var service = CreateService();
        _anticipation.Next = null;

        // Player at z=1 is inside the max-melee ring. One-directional: never back off, only approach.
        var request = CreateRequest() with { MaintainMaxMelee = true, PlayerPosition = new Vector3(0f, 0f, 1f) };
        service.Update(request);

        Assert.Equal(PositionalMovementPhase.Skipped, service.State.Phase);
        _vNav.Verify(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void Update_WhenMaintainButAlreadyAtMaxMelee_SkipsWithoutMoving()
    {
        var service = CreateService();
        _anticipation.Next = null;

        // Default player position (z=5) is at the max-melee ring for a 2y-hitbox target → no back-off.
        var request = CreateRequest() with { MaintainMaxMelee = true };
        service.Update(request);

        Assert.Equal(PositionalMovementPhase.Skipped, service.State.Phase);
        _vNav.Verify(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void Update_WhenMaintainAndTooFar_QueuesApproach()
    {
        var service = CreateService();
        _anticipation.Next = null; // no positional arc — maintenance only

        // Player at z=8 (target hitbox 2, edge 5.5, approach trigger 6.0) has lost uptime → walk back in.
        var request = CreateRequest() with { MaintainMaxMelee = true, PlayerPosition = new Vector3(0f, 0f, 8f) };
        service.Update(request);

        Assert.Equal(PositionalMovementPhase.Moving, service.State.Phase);
        _vNav.Verify(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), false), Times.Once);
    }

    [Fact]
    public void Update_WhenMaintainAndJustOutsideRing_LeavesInPlace()
    {
        var service = CreateService();
        _anticipation.Next = null;

        // Player at z=5.3 is past the stand ring (5.0) but still inside the vNav Flex grace band
        // (5.0 ± 0.5 = [4.5, 5.5]) → suppress the vNav call, no move.
        var request = CreateRequest() with { MaintainMaxMelee = true, PlayerPosition = new Vector3(0f, 0f, 5.3f) };
        service.Update(request);

        Assert.Equal(PositionalMovementPhase.Skipped, service.State.Phase);
        _vNav.Verify(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void Update_WhenMaintainDisabledAndHugging_DoesNotMove()
    {
        var service = CreateService();
        _anticipation.Next = null;

        var request = CreateRequest() with { MaintainMaxMelee = false, PlayerPosition = new Vector3(0f, 0f, 1f) };
        service.Update(request);

        Assert.Equal(PositionalMovementPhase.Skipped, service.State.Phase);
        _vNav.Verify(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void Update_WhenPositionalDisabledButMaintainOnAndTooFar_StillApproaches()
    {
        var service = CreateService();
        _anticipation.Next = new PositionalAnticipation(PositionalType.Rear, 7481, PositionalAnticipationReason.ComboSetup);

        // EnableMovement off (e.g. job without positionals / solo) must not block range-keeping.
        var request = CreateRequest() with
        {
            EnableMovement = false,
            MaintainMaxMelee = true,
            PlayerPosition = new Vector3(0f, 0f, 8f),
        };
        service.Update(request);

        Assert.Equal(PositionalMovementPhase.Moving, service.State.Phase);
        _vNav.Verify(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), false), Times.Once);
    }

    [Fact]
    public void Update_WhenTargetChasesPlayer_SuppressesBackoff()
    {
        var service = CreateService();
        _anticipation.Next = null;

        // Hugging a mob that targets us (solo / self-tanked) must NOT back off — it would only kite-bounce.
        var request = CreateRequest() with
        {
            MaintainMaxMelee = true,
            PlayerPosition = new Vector3(0f, 0f, 1f),
            MaxMeleeTarget = new PositionalMovementTarget(Vector3.Zero, 2f, 0f, false),
            MaxMeleeTargetFollowsPlayer = true,
        };
        service.Update(request);

        Assert.Equal(PositionalMovementPhase.Skipped, service.State.Phase);
        _vNav.Verify(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void Update_WhenTargetChasesPlayerButTooFar_StillApproaches()
    {
        var service = CreateService();
        _anticipation.Next = null;

        // Suppression only covers the back-off; if knocked out of range we still walk back in.
        var request = CreateRequest() with
        {
            MaintainMaxMelee = true,
            PlayerPosition = new Vector3(0f, 0f, 8f),
            MaxMeleeTarget = new PositionalMovementTarget(Vector3.Zero, 2f, 0f, false),
            MaxMeleeTargetFollowsPlayer = true,
        };
        service.Update(request);

        Assert.Equal(PositionalMovementPhase.Moving, service.State.Phase);
        _vNav.Verify(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), false), Times.Once);
    }

    [Fact]
    public void Update_MaintainAnchorsToCurrentTargetNotPositionalTarget()
    {
        var service = CreateService();
        _anticipation.Next = null;

        // Positional target sits right next to the player (would trigger a back-off if maintenance used it),
        // but the current (max-melee) target is at the ring → maintenance must stay put, proving it follows
        // the current target, not the strategy/positional one.
        var request = CreateRequest() with
        {
            MaintainMaxMelee = true,
            Target = new PositionalMovementTarget(new Vector3(0f, 0f, 4.5f), 2f, 0f, false),
            MaxMeleeTarget = new PositionalMovementTarget(Vector3.Zero, 2f, 0f, false),
        };
        service.Update(request);

        Assert.Equal(PositionalMovementPhase.Skipped, service.State.Phase);
        _vNav.Verify(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void Update_MaintainFollowsCurrentTargetWhenHugging_NoBackOff()
    {
        var service = CreateService();
        _anticipation.Next = null;

        // Hugging the current target — one-directional means no back-off, just skip.
        var request = CreateRequest() with
        {
            MaintainMaxMelee = true,
            Target = new PositionalMovementTarget(Vector3.Zero, 2f, 0f, false),
            MaxMeleeTarget = new PositionalMovementTarget(new Vector3(0f, 0f, 4.5f), 2f, 0f, false),
        };
        service.Update(request);

        Assert.Equal(PositionalMovementPhase.Skipped, service.State.Phase);
        _vNav.Verify(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void Update_WhileAsyncPathfinding_HoldsWithoutReissuingMove()
    {
        var service = CreateService();
        _anticipation.Next = null;

        // Start a maintenance approach from too far → one PathfindAndMoveCloseTo call.
        service.Update(CreateRequest() with { MaintainMaxMelee = true, PlayerPosition = new Vector3(0f, 0f, 8f) });
        Assert.Equal(PositionalMovementPhase.Moving, service.State.Phase);

        // vNav is still computing the path (async): IsPathRunning false but IsPathfindInProgress true.
        // We must NOT re-issue the move every frame (that is the vNav spam) — hold instead.
        _vNav.Setup(x => x.IsPathRunning).Returns(false);
        _vNav.Setup(x => x.IsPathfindInProgress).Returns(true);
        service.Update(CreateRequest() with { MaintainMaxMelee = true, PlayerPosition = new Vector3(0f, 0f, 8f) });

        Assert.Equal(PositionalMovementPhase.Moving, service.State.Phase);
        _vNav.Verify(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public void Update_WithLargerFlex_WidensGraceBandAndSuppresses()
    {
        var service = CreateService();
        _anticipation.Next = null;

        // At z=6.5 with flex 0.5 the player is past the band (>5.5) → would move. With flex 2.0 the band is
        // [3.0, 7.0], so z=6.5 is inside → suppressed. Confirms vNav Flex tunes the grace band.
        var moved = CreateRequest() with { MaintainMaxMelee = true, PlayerPosition = new Vector3(0f, 0f, 6.5f), VNavFlex = 0.5f };
        service.Update(moved);
        Assert.Equal(PositionalMovementPhase.Moving, service.State.Phase);

        var suppressed = CreateRequest() with { MaintainMaxMelee = true, PlayerPosition = new Vector3(0f, 0f, 6.5f), VNavFlex = 2.0f };
        service.Update(suppressed);
        Assert.Equal(PositionalMovementPhase.Skipped, service.State.Phase);
    }

    [Fact]
    public void Update_WhileApproachingAndInsideBand_HoldsPathInsteadOfStopping()
    {
        var service = CreateService();
        _anticipation.Next = null;

        // Start an approach from too far.
        service.Update(CreateRequest() with { MaintainMaxMelee = true, PlayerPosition = new Vector3(0f, 0f, 8f) });
        Assert.Equal(PositionalMovementPhase.Moving, service.State.Phase);

        // Path now running; player has entered the grace band but not yet reached the ring.
        // It must keep the path alive (no Stop) so it settles at max melee instead of twitching.
        _vNav.Setup(x => x.IsPathRunning).Returns(true);
        service.Update(CreateRequest() with { MaintainMaxMelee = true, PlayerPosition = new Vector3(0f, 0f, 5.3f) });

        Assert.Equal(PositionalMovementPhase.Moving, service.State.Phase);
        _vNav.Verify(x => x.Stop(), Times.Never);
    }

    [Fact]
    public void Update_WhenArbiterSuppresses_SkipsWithoutStopChurn()
    {
        var service = CreateService();
        _anticipation.Next = null;

        // Arbiter yields to BMR: the submission is denied. The service must mark Skipped with the yield
        // reason and NOT call Stop() — stopping would churn against whatever the arbiter is protecting.
        _vNav.Setup(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), It.IsAny<bool>()))
            .Returns(VNavMoveResult.Suppressed);

        service.Update(CreateRequest() with { MaintainMaxMelee = true, PlayerPosition = new Vector3(0f, 0f, 8f) });

        Assert.Equal(PositionalMovementPhase.Skipped, service.State.Phase);
        Assert.Equal("movement yielded to BossMod", service.State.SkipReason);
        _vNav.Verify(x => x.Stop(), Times.Never);
    }

    [Fact]
    public void Update_AfterSuppressionLifts_ResumesApproach()
    {
        var service = CreateService();
        _anticipation.Next = null;
        var request = CreateRequest() with { MaintainMaxMelee = true, PlayerPosition = new Vector3(0f, 0f, 8f) };

        // Frame 1: arbiter denies (yielded).
        _vNav.Setup(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), It.IsAny<bool>()))
            .Returns(VNavMoveResult.Suppressed);
        service.Update(request);
        Assert.Equal(PositionalMovementPhase.Skipped, service.State.Phase);

        // Frame 2: danger cleared, arbiter grants — the walk-in resumes normally (the NIN Kunai's Bane
        // scenario: closing from 8y through the shared max-melee maintenance path).
        _vNav.Setup(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), It.IsAny<bool>()))
            .Returns(VNavMoveResult.Queued);
        service.Update(request);

        Assert.Equal(PositionalMovementPhase.Moving, service.State.Phase);
        _vNav.Verify(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), false), Times.Exactly(2));
    }

    [Fact]
    public void Update_WhenMaxMeleePathRunning_ReplacesWithPositionalArc()
    {
        var service = CreateService();
        _action.Setup(x => x.GcdRemaining).Returns(0f);

        service.Update(CreateRequest() with
        {
            EnableMovement = false,
            MaintainMaxMelee = true,
            PlayerPosition = new Vector3(0f, 0f, 8f),
        });
        Assert.Equal(PositionalMovementPhase.Moving, service.State.Phase);
        Assert.Equal("max melee maintenance", service.State.SkipReason);
        _vNav.Verify(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), false), Times.Once);

        _vNav.Invocations.Clear();
        _vNav.Setup(x => x.IsPathRunning).Returns(true);
        _anticipation.Next = new PositionalAnticipation(PositionalType.Rear, 7481, PositionalAnticipationReason.ComboSetup);

        service.Update(CreateRequest());

        _vNav.Verify(x => x.Stop(), Times.Once);
        _vNav.Verify(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), MovementIntent.PositionalArc, false), Times.Once);
        Assert.Equal(PositionalType.Rear, service.State.TargetZone);
    }

    [Fact]
    public void Update_ArcSuppressedByArbiter_SkipsWithYieldReason()
    {
        var service = CreateService();
        _anticipation.Next = new PositionalAnticipation(PositionalType.Rear, 7481, PositionalAnticipationReason.ComboSetup);
        _vNav.Setup(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), It.IsAny<MovementIntent>(), It.IsAny<bool>()))
            .Returns(VNavMoveResult.Suppressed);

        service.Update(CreateRequest());

        Assert.Equal(PositionalMovementPhase.Skipped, service.State.Phase);
        Assert.Equal("movement yielded to BossMod", service.State.SkipReason);
    }

    [Fact]
    public void Update_ArcDestination_MatchesMaxMeleeRing()
    {
        // Ring reconciliation: the arc stand ring must equal the max-melee maintenance ring
        // (targetHitbox + playerHitbox + reach − buffer = 5.0y here), or every completed hop would
        // immediately trigger a max-melee walk-in at small vNav Flex.
        Vector3? queued = null;
        _vNav.Setup(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), It.IsAny<MovementIntent>(), It.IsAny<bool>()))
            .Callback<Vector3, float, MovementIntent, bool>((d, _, _, _) => queued = d)
            .Returns(VNavMoveResult.Queued);

        var service = CreateService();
        _anticipation.Next = new PositionalAnticipation(PositionalType.Rear, 7481, PositionalAnticipationReason.ComboSetup);
        _action.Setup(x => x.GcdRemaining).Returns(0f);

        service.Update(CreateRequest());

        Assert.NotNull(queued);
        var maxMeleeRing = PositionalStandCalculator.MaxMeleeStandDistance(2f, 0.5f);
        Assert.Equal(maxMeleeRing, queued!.Value.Length(), 0.01f);
    }

    [Fact]
    public void Update_WithBoundaryBias_ArcDestinationNearBoundary()
    {
        // 10° bias, player on the target's right (+X): rear stand lands at 145° off facing, not 180°.
        Vector3? queued = null;
        _vNav.Setup(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), It.IsAny<MovementIntent>(), It.IsAny<bool>()))
            .Callback<Vector3, float, MovementIntent, bool>((d, _, _, _) => queued = d)
            .Returns(VNavMoveResult.Queued);

        var service = CreateService();
        _anticipation.Next = new PositionalAnticipation(PositionalType.Rear, 7481, PositionalAnticipationReason.ComboSetup);
        _action.Setup(x => x.GcdRemaining).Returns(0f);

        service.Update(CreateRequest() with
        {
            PlayerPosition = new Vector3(5f, 0f, 0f),
            PositionalBoundaryBiasRadians = MathF.PI / 18f,
        });

        Assert.NotNull(queued);
        var angleDeg = MathF.Atan2(queued!.Value.X, queued.Value.Z) * 180f / MathF.PI;
        Assert.Equal(145f, angleDeg, 0.5f);
    }

    private sealed class TestAnticipationProvider : IPositionalAnticipationProvider
    {
        public PositionalAnticipation? Next { get; set; }

        public PositionalAnticipation? GetAnticipatedPositional(in PositionalAnticipationContext context) => Next;
    }

    private PositionalMovementService CreateService()
        => new(_vNav.Object, _bossMod.Object);

    private PositionalMovementUpdateRequest CreateRequest(
        PositionalAnticipationContext? anticipationContext = null,
        bool allowMovementDuringActionLock = false)
    {
        return new PositionalMovementUpdateRequest(
            AnticipationProvider: _anticipation,
            AnticipationContext: anticipationContext ?? BaseAnticipationContext,
            PlayerPosition: new Vector3(0f, 0f, 5f),
            PlayerHitboxRadius: 0.5f,
            Target: new PositionalMovementTarget(
                Position: Vector3.Zero,
                HitboxRadius: 2f,
                RotationRadians: 0f,
                HasPositionalImmunity: false),
            ActionService: _action.Object,
            InCombat: true,
            AllowMovementDuringActionLock: allowMovementDuringActionLock,
            VNavFlex: 0.5f);
    }

    private static PositionalAnticipationContext BaseAnticipationContext => new(
        LastComboAction: SAMActions.Jinpu.ActionId,
        PlayerLevel: 100,
        HasTrueNorth: false,
        TargetHasPositionalImmunity: false,
        IsAtRear: false,
        IsAtFlank: false);
}
