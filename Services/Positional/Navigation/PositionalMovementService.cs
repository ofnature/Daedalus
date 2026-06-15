using System;
using System.Numerics;
using Olympus.Services.Action;

namespace Olympus.Services.Positional.Navigation;

/// <summary>
/// Orchestrates positional stand calculation, BossMod safety, and vNav execution.
/// </summary>
public sealed class PositionalMovementService : IPositionalMovementService
{
    private readonly IVNavService _vNav;
    private readonly IBossModSafetyService _bossModSafety;

    public PositionalMovementService(IVNavService vNav, IBossModSafetyService bossModSafety)
    {
        _vNav = vNav;
        _bossModSafety = bossModSafety;
    }

    public PositionalMovementState State { get; private set; }

    public void Update(in PositionalMovementUpdateRequest request)
    {
        _bossModSafety.BeginUpdateSnapshot();

        if (_vNav.IsPathRunning && _bossModSafety.ShouldAbortMovement())
        {
            Cancel("new mechanic telegraph");
            return;
        }

        if (!request.EnableMovement || !request.InCombat || request.Target is not { } target)
        {
            SetSkipped(null);
            return;
        }

        if (request.AnticipationProvider is null)
        {
            SetSkipped(null);
            return;
        }

        var anticipation = request.AnticipationProvider.GetAnticipatedPositional(request.AnticipationContext);
        if (anticipation is null)
        {
            SetSkipped(null);
            return;
        }

        if (target.HasPositionalImmunity)
        {
            SetSkipped("target positional immunity");
            return;
        }

        if (IsAlreadyCorrect(anticipation.Value.Required, request.AnticipationContext))
        {
            SetSkipped("already at positional");
            return;
        }

        if (WouldClipGcd(request.ActionService, request.PlayerPosition, request.PlayerHitboxRadius, target, anticipation.Value.Required))
        {
            SetSkipped("would clip GCD");
            return;
        }

        var standRequest = new PositionalStandRequest(
            PlayerPosition: request.PlayerPosition,
            PlayerHitboxRadius: request.PlayerHitboxRadius,
            TargetPosition: target.Position,
            TargetHitboxRadius: target.HitboxRadius,
            TargetRotationRadians: target.RotationRadians,
            RequiredPositional: anticipation.Value.Required,
            GcdRemainingSeconds: request.ActionService.GcdRemaining);

        var destination = PositionalStandCalculator.Calculate(in standRequest);
        destination = _vNav.SnapToFloor(destination);

        var safety = _bossModSafety.QueryPositionSafety(destination);
        if (safety is PositionSafety.Unsafe or PositionSafety.Imminent)
        {
            SetSkipped($"unsafe destination ({safety})");
            return;
        }

        if (!_bossModSafety.IsSegmentSafe(request.PlayerPosition, destination))
        {
            SetSkipped("dash segment unsafe");
            return;
        }

        if (_vNav.IsPathRunning)
        {
            State = new PositionalMovementState(
                PositionalMovementPhase.Moving,
                anticipation.Value.Required,
                destination);
            return;
        }

        var moveResult = _vNav.PathfindAndMoveCloseTo(
            destination,
            fly: false,
            toleranceYalms: PositionalMovementConstants.PositionalArrivalToleranceYalms);
        if (moveResult != VNavMoveResult.Queued)
        {
            SetSkipped($"vNav unavailable ({moveResult})");
            return;
        }

        State = new PositionalMovementState(
            PositionalMovementPhase.Moving,
            anticipation.Value.Required,
            destination);
    }

    public void Cancel(string reason)
    {
        _vNav.Stop();
        State = new PositionalMovementState(
            PositionalMovementPhase.Aborted,
            State.TargetZone,
            State.Destination,
            reason);
    }

    private void SetSkipped(string? reason)
    {
        if (_vNav.IsPathRunning)
            _vNav.Stop();

        State = new PositionalMovementState(
            PositionalMovementPhase.Skipped,
            SkipReason: reason);
    }

    private static bool IsAlreadyCorrect(PositionalType required, in PositionalAnticipationContext context)
    {
        return required switch
        {
            PositionalType.Rear => context.IsAtRear,
            PositionalType.Flank => context.IsAtFlank,
            _ => false,
        };
    }

    private static bool WouldClipGcd(
        IActionService actionService,
        Vector3 playerPosition,
        float playerHitboxRadius,
        PositionalMovementTarget target,
        PositionalType required)
    {
        if (actionService.IsCasting
            || actionService.AnimationLockRemaining > PositionalMovementConstants.MovementStartMaxAnimationLockSeconds)
            return true;

        var standRequest = new PositionalStandRequest(
            PlayerPosition: playerPosition,
            PlayerHitboxRadius: playerHitboxRadius,
            TargetPosition: target.Position,
            TargetHitboxRadius: target.HitboxRadius,
            TargetRotationRadians: target.RotationRadians,
            RequiredPositional: required,
            GcdRemainingSeconds: actionService.GcdRemaining);

        var distToIdeal = PositionalStandCalculator.ComputeIdealHorizontalDistance(in standRequest);
        var maxMove = PositionalStandCalculator.ComputeMaxHorizontalMoveYalms(distToIdeal, actionService.GcdRemaining);
        if (maxMove < 1e-3f)
            return true;

        var moveDuration = PositionalStandCalculator.EstimateMoveDurationSeconds(maxMove);

        // Capped path must finish before the GCD queue window (partial moves allowed when ideal is farther).
        return moveDuration > actionService.GcdRemaining - PositionalMovementConstants.GcdClipBufferSeconds;
    }
}
