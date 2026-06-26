using System;
using System.Numerics;
using Daedalus.Services.Action;

namespace Daedalus.Services.Positional.Navigation;

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

        if (!request.InCombat || request.Target is not { } target)
        {
            SetSkipped(null);
            return;
        }

        // Skip reason to fall back on if neither a positional arc nor a max-melee back-off moves us.
        string? idleReason = null;

        // --- Positional flank/rear arc (SAM/NIN; gated by EnableMovement + a job anticipation provider) ---
        if (request.EnableMovement
            && request.AnticipationProvider?.GetAnticipatedPositional(request.AnticipationContext) is { } anticipation)
        {
            if (target.HasPositionalImmunity)
            {
                idleReason = "target positional immunity";
            }
            else if (IsAlreadyCorrect(anticipation.Required, request.AnticipationContext))
            {
                idleReason = "already at positional";
            }
            else if (!request.AllowMovementDuringActionLock && WouldClipGcd(
                request.ActionService,
                request.PlayerPosition,
                request.PlayerHitboxRadius,
                target,
                anticipation.Required,
                false))
            {
                SetSkipped("would clip GCD");
                return;
            }
            else if (TryQueuePositionalArc(in request, target, anticipation.Required))
            {
                return;
            }
            else
            {
                // TryQueuePositionalArc set the skip reason (unsafe / vNav unavailable) and returns false.
                return;
            }
        }

        // --- Max-melee maintenance (all melee jobs; independent of positional repositioning and party state) ---
        // Anchored to the player's current target (request.MaxMeleeTarget) so we keep range on the mob we're
        // actually attacking, not a strategy-selected / merely-aggroed enemy. Falls back to the positional
        // target when no dedicated current target was supplied.
        if (TryMaintainMaxMelee(in request, request.MaxMeleeTarget ?? target))
            return;

        SetSkipped(idleReason);
    }

    /// <summary>
    /// Computes, safety-checks, and queues the positional flank/rear stand point. Returns true when a move
    /// was queued (or an owned path is already running toward it); false sets the skip reason and is handled
    /// by the caller.
    /// </summary>
    private bool TryQueuePositionalArc(
        in PositionalMovementUpdateRequest request,
        PositionalMovementTarget target,
        PositionalType required)
    {
        var standRequest = new PositionalStandRequest(
            PlayerPosition: request.PlayerPosition,
            PlayerHitboxRadius: request.PlayerHitboxRadius,
            TargetPosition: target.Position,
            TargetHitboxRadius: target.HitboxRadius,
            TargetRotationRadians: target.RotationRadians,
            RequiredPositional: required,
            GcdRemainingSeconds: request.ActionService.GcdRemaining);

        var destination = _vNav.SnapToFloor(PositionalStandCalculator.Calculate(in standRequest));

        var safety = _bossModSafety.QueryPositionSafety(destination);
        if (safety is PositionSafety.Unsafe or PositionSafety.Imminent)
        {
            SetSkipped($"unsafe destination ({safety})");
            return false;
        }

        if (!_bossModSafety.IsSegmentSafe(request.PlayerPosition, destination))
        {
            SetSkipped("dash segment unsafe");
            return false;
        }

        if (_vNav.IsPathRunning)
        {
            State = new PositionalMovementState(PositionalMovementPhase.Moving, required, destination);
            return true;
        }

        var moveResult = _vNav.PathfindAndMoveCloseTo(
            destination,
            fly: false,
            toleranceYalms: PositionalMovementConstants.PositionalArrivalToleranceYalms);
        if (moveResult != VNavMoveResult.Queued)
        {
            SetSkipped($"vNav unavailable ({moveResult})");
            return false;
        }

        State = new PositionalMovementState(PositionalMovementPhase.Moving, required, destination);
        return true;
    }

    /// <summary>
    /// Keeps the character parked at the max-melee stand ring: backs out when hugging the target and walks
    /// in when drifted out of melee uptime (BossMod's "approach to range, or leave in place if already
    /// closer" rule). Returns true when a move was queued (or an owned path is already running). No-op
    /// (returns false) when disabled, already inside the dead-band, or the destination/segment is unsafe.
    /// </summary>
    private const string MaxMeleeMaintenanceReason = "max melee maintenance";

    private bool TryMaintainMaxMelee(in PositionalMovementUpdateRequest request, PositionalMovementTarget target)
    {
        if (!request.MaintainMaxMelee)
            return false;

        // Hold an in-progress owned maintenance path until vNav finishes reaching the stand ring. This also
        // covers the async pathfinding window (IsPathfindInProgress): without it we'd re-issue
        // PathfindAndMoveCloseTo every frame while the path is still being computed — that is the vNav
        // "spam" / twitch. Re-evaluating mid-step would stop the path the instant we re-enter the band and
        // jitter would re-fire it; instead we only re-arm once the path has fully completed.
        if ((_vNav.IsPathRunning || _vNav.IsPathfindInProgress)
            && State.Phase == PositionalMovementPhase.Moving
            && State.SkipReason == MaxMeleeMaintenanceReason)
        {
            State = new PositionalMovementState(
                PositionalMovementPhase.Moving, null, State.Destination, MaxMeleeMaintenanceReason);
            return true;
        }

        // Symmetric grace dead-band around the max-melee stand distance: only call vNav once the character
        // leaves [standDistance − flex, standDistance + flex]. Inside the band the call is suppressed, which
        // is what stops the move-in/move-out bouncing. flex is the user-tunable "vNav Flex".
        var standDistance = PositionalStandCalculator.MaxMeleeStandDistance(target.HitboxRadius, request.PlayerHitboxRadius);
        var distance = HorizontalDistanceTo(request.PlayerPosition, target.Position);
        var flex = System.MathF.Max(0f, request.VNavFlex);

        // One-directional: only move IN when outside max melee, never move away when too close.
        // Back-off causes kite-bounce oscillation with every mob type, not just self-targeted ones.
        if (distance <= standDistance + flex)
            return false;

        var standRequest = new MeleeApproachStandRequest(
            PlayerPosition: request.PlayerPosition,
            PlayerHitboxRadius: request.PlayerHitboxRadius,
            TargetPosition: target.Position,
            TargetHitboxRadius: target.HitboxRadius);

        // Projects to the stand ring along the current bearing in either direction (out when hugging,
        // in when too far), so the same point serves both back-off and approach.
        var destination = _vNav.SnapToFloor(PositionalStandCalculator.CalculateMaxMeleeBackoff(in standRequest));

        var safety = _bossModSafety.QueryPositionSafety(destination);
        if (safety is PositionSafety.Unsafe or PositionSafety.Imminent)
            return false;

        if (!_bossModSafety.IsSegmentSafe(request.PlayerPosition, destination))
            return false;

        if (_vNav.IsPathRunning || _vNav.IsPathfindInProgress)
        {
            // Another path is already running / computing — adopt it as the maintenance path so the hold
            // branch above keeps it alive to completion instead of re-issuing the move next frame.
            State = new PositionalMovementState(
                PositionalMovementPhase.Moving, null, destination, MaxMeleeMaintenanceReason);
            return true;
        }

        var moveResult = _vNav.PathfindAndMoveCloseTo(
            destination,
            fly: false,
            toleranceYalms: PositionalMovementConstants.PositionalArrivalToleranceYalms);
        if (moveResult != VNavMoveResult.Queued)
            return false;

        State = new PositionalMovementState(
            PositionalMovementPhase.Moving, null, destination, MaxMeleeMaintenanceReason);
        return true;
    }

    private static float HorizontalDistanceTo(System.Numerics.Vector3 from, System.Numerics.Vector3 to)
    {
        var dx = from.X - to.X;
        var dz = from.Z - to.Z;
        return System.MathF.Sqrt((dx * dx) + (dz * dz));
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
        StopOwnedPathIfActive();

        State = new PositionalMovementState(
            PositionalMovementPhase.Skipped,
            SkipReason: reason);
    }

    private void StopOwnedPathIfActive()
    {
        if (State.Phase == PositionalMovementPhase.Moving && _vNav.IsPathRunning)
            _vNav.Stop();
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
        PositionalType required,
        bool allowMovementDuringActionLock)
    {
        if (!allowMovementDuringActionLock
            && (actionService.IsCasting
                || actionService.AnimationLockRemaining
                    > PositionalMovementConstants.MovementStartMaxAnimationLockSeconds))
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
