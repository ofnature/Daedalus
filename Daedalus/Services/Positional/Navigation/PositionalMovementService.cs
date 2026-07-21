using System;
using System.Numerics;
using Daedalus.Services.Action;

namespace Daedalus.Services.Positional.Navigation;

/// <summary>
/// Orchestrates positional stand calculation, BossMod safety, and vNav execution.
/// </summary>
public sealed class PositionalMovementService : IPositionalMovementService
{
    private readonly IMovementArbiter _vNav;
    private readonly IBossModSafetyService _bossModSafety;

    public PositionalMovementService(IMovementArbiter vNav, IBossModSafetyService bossModSafety)
    {
        _vNav = vNav;
        _bossModSafety = bossModSafety;
    }

    public PositionalMovementState State { get; private set; }

    /// <summary>Test seam (stutter-fix cadence: drift re-trigger cooldown).</summary>
    internal Func<DateTime> UtcNow = () => DateTime.UtcNow;

    /// <summary>Last time a positional-arc path (hop or drift) was actually queued on vNav.</summary>
    private DateTime _lastArcQueuedUtc = DateTime.MinValue;

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

        // Hard cast guard (field report 2026-07-20: the SAM anchor walked through Midare/Ogi cast
        // bars and cancelled them). AllowMovementDuringActionLock covers ANIMATION lock on
        // instant-GCD jobs — it must never authorize movement during a real cast, and the
        // run-to-completion hold must not carry a path into one. Unconditional: stop an owned
        // path the moment a cast starts, and never queue movement while casting.
        if (request.ActionService.IsCasting)
        {
            SetSkipped("casting");
            return;
        }

        // Skip reason to fall back on if neither a positional arc nor a max-melee back-off moves us.
        string? idleReason = null;

        // --- Positional flank/rear arc (boundary camping; per-job rollout via IsPositionalArcRolloutEnabled) ---
        if (request.EnableMovement
            && request.AnticipationProvider?.GetAnticipatedPositional(request.AnticipationContext) is { } anticipation)
        {
            if (target.HasPositionalImmunity)
            {
                idleReason = "target positional immunity";
            }
            else if (IsAlreadyCorrect(anticipation.Required, request.AnticipationContext))
            {
                // Stutter fix (field report 2026-07-20, first live anchor run): an in-flight arc
                // path toward the SAME required zone runs to completion. Previously the clip gate
                // re-evaluated every frame, so as the GCD timer ran down mid-drift, canDrift
                // flipped false → SetSkipped → Stop() → the toon halted mid-hop, then resumed
                // after the GCD fired — visible stutter-stepping every single GCD cycle. Mechanic
                // telegraphs still abort at the top of Update; a required-zone CHANGE still
                // repaths through the hop branch below.
                if ((_vNav.IsPathRunning || _vNav.IsPathfindInProgress)
                    && State.Phase == PositionalMovementPhase.Moving
                    && State.TargetZone == anticipation.Required
                    && State.SkipReason != MaxMeleeMaintenanceReason)
                {
                    State = new PositionalMovementState(
                        PositionalMovementPhase.Moving, anticipation.Required, State.Destination);
                    return;
                }

                // Anchor persistence (positional-anchor-plan P1): "anywhere inside the arc" used to
                // idle here, so a knockback/dodge that dumped the toon at arc CENTER (or the far
                // side) made the next flank/rear swap a long walk instead of the ~1.5y boundary hop
                // the anchor design promises. With boundary camping active (bias > 0), drift back
                // to the boundary-biased anchor whenever we're off it by more than the tolerance —
                // same stand math and BMR safety veto as the arc hop, budget-clamped, and never
                // when it would clip the GCD (the drift is the lowest-urgency move we make).
                // The re-trigger cooldown stops anchor-chasing: every time the target turns, its
                // facing drags the anchor along the ring, and cooldown-less drift walked the toon
                // step-by-step after every twitch of a rotating mob.
                var canDrift = request.PositionalBoundaryBiasRadians > 0f
                    && (UtcNow() - _lastArcQueuedUtc).TotalSeconds
                        >= PositionalMovementConstants.AnchorDriftMinIntervalSeconds
                    && ComputeAnchorDistance(in request, target, anticipation.Required)
                        > PositionalMovementConstants.AnchorDriftToleranceYalms
                    && (request.AllowMovementDuringActionLock || !WouldClipGcd(
                        request.ActionService,
                        request.PlayerPosition,
                        request.PlayerHitboxRadius,
                        target,
                        anticipation.Required,
                        false,
                        request.PositionalBoundaryBiasRadians));

                if (canDrift && TryQueuePositionalArc(in request, target, anticipation.Required))
                    return;

                idleReason = request.PositionalBoundaryBiasRadians > 0f
                    ? "at anchor"
                    : "already at positional";
            }
            else if (!request.AllowMovementDuringActionLock && WouldClipGcd(
                request.ActionService,
                request.PlayerPosition,
                request.PlayerHitboxRadius,
                target,
                anticipation.Required,
                false,
                request.PositionalBoundaryBiasRadians))
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
            GcdRemainingSeconds: ResolveGcdBudgetSeconds(request.ActionService.GcdRemaining),
            StandRadiusOffset: ArcStandRadiusOffset(request.PlayerHitboxRadius),
            BoundaryBiasRadians: request.PositionalBoundaryBiasRadians);

        // Hazard fallback (field report 2026-07-20 #3): the primary anchor can sit inside an arena
        // hazard — previously we skipped outright and the toon parked at the hazard's edge. The
        // required arc has more than one valid spot: try the mirror-side boundary anchor, then the
        // arc center(s), and only skip when every same-arc candidate is hazarded.
        var candidates = PositionalStandCalculator.CalculateCandidates(in standRequest);
        Vector3? chosen = null;
        string? firstRejection = null;
        foreach (var candidate in candidates)
        {
            var snapped = _vNav.SnapToFloor(candidate);

            var safety = _bossModSafety.QueryPositionSafety(snapped);
            if (safety is PositionSafety.Unsafe or PositionSafety.Imminent)
            {
                firstRejection ??= $"unsafe destination ({safety})";
                continue;
            }

            if (!_bossModSafety.IsSegmentSafe(request.PlayerPosition, snapped))
            {
                firstRejection ??= "dash segment unsafe";
                continue;
            }

            chosen = snapped;
            break;
        }

        if (chosen is not { } destination)
        {
            SetSkipped(candidates.Length > 1
                ? $"{firstRejection} — all {candidates.Length} arc spots hazarded"
                : firstRejection);
            return false;
        }

        if (_vNav.IsPathRunning || _vNav.IsPathfindInProgress)
        {
            if (ShouldHoldPositionalPath(required, destination))
            {
                State = new PositionalMovementState(PositionalMovementPhase.Moving, required, destination);
                return true;
            }

            // A max-melee or foreign path is active — stop it so we can arc to flank/rear.
            if (_vNav.IsPathRunning)
                _vNav.Stop();
        }

        var moveResult = _vNav.PathfindAndMoveCloseTo(
            destination,
            PositionalMovementConstants.PositionalArrivalToleranceYalms,
            MovementIntent.PositionalArc);
        if (moveResult != VNavMoveResult.Queued)
        {
            SetSkipped(moveResult == VNavMoveResult.Suppressed
                ? "movement yielded to BossMod"
                : $"vNav unavailable ({moveResult})");
            return false;
        }

        _lastArcQueuedUtc = UtcNow();
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
        if (moveResult == VNavMoveResult.Suppressed)
        {
            // Arbiter denied (yielded to BossMod or rate-limited). Mark skipped WITHOUT the usual
            // SetSkipped Stop() — stopping here would churn against whatever the arbiter is protecting.
            State = new PositionalMovementState(
                PositionalMovementPhase.Skipped, SkipReason: "movement yielded to BossMod");
            return true;
        }

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

    private bool ShouldHoldPositionalPath(PositionalType required, Vector3 destination)
    {
        if (State.Phase != PositionalMovementPhase.Moving || State.TargetZone != required)
            return false;

        if (State.SkipReason == MaxMeleeMaintenanceReason)
            return false;

        if (!State.Destination.HasValue)
            return _vNav.IsPathRunning || _vNav.IsPathfindInProgress;

        return HorizontalDistanceTo(State.Destination.Value, destination)
            <= PositionalMovementConstants.PositionalHoldDestinationToleranceYalms;
    }

    /// <summary>
    /// Horizontal distance from the player to the (boundary-biased) anchor stand point —
    /// the drift trigger for anchor persistence.
    /// </summary>
    private static float ComputeAnchorDistance(
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
            StandRadiusOffset: ArcStandRadiusOffset(request.PlayerHitboxRadius),
            BoundaryBiasRadians: request.PositionalBoundaryBiasRadians);

        // Min over BOTH boundary anchors: a toon parked on the mirror anchor (because the primary
        // side is hazarded) is home — it must not re-drift toward the hazard every cooldown.
        return PositionalStandCalculator.ComputeMinAnchorDistance(in standRequest);
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
        bool allowMovementDuringActionLock,
        float boundaryBiasRadians = 0f)
    {
        // GCD is ready — rotation is holding for positional; use the full window to reposition.
        if (actionService.GcdRemaining <= PositionalMovementConstants.GcdClipBufferSeconds)
            return false;

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
            GcdRemainingSeconds: actionService.GcdRemaining,
            StandRadiusOffset: ArcStandRadiusOffset(playerHitboxRadius),
            BoundaryBiasRadians: boundaryBiasRadians);

        var distToIdeal = PositionalStandCalculator.ComputeIdealHorizontalDistance(in standRequest);
        var maxMove = PositionalStandCalculator.ComputeMaxHorizontalMoveYalms(distToIdeal, actionService.GcdRemaining);
        if (maxMove < 1e-3f)
            return true;

        var moveDuration = PositionalStandCalculator.EstimateMoveDurationSeconds(maxMove);

        // Capped path must finish before the GCD queue window (partial moves allowed when ideal is farther).
        return moveDuration > actionService.GcdRemaining - PositionalMovementConstants.GcdClipBufferSeconds;
    }

    /// <summary>
    /// Arc stand-ring offset from target center, matched to the max-melee maintenance ring
    /// (targetHitbox + playerHitbox + reach − safety buffer). The default arc offset (3.5y) sits 0.5y
    /// OUTSIDE the maintenance ring, so every completed hop would immediately trigger a max-melee
    /// walk-in at small vNav Flex — identical rings make the hop destination land inside the dead-band.
    /// </summary>
    private static float ArcStandRadiusOffset(float playerHitboxRadius)
        => playerHitboxRadius
            + PositionalMovementConstants.MeleeActionRangeYalms
            - PositionalMovementConstants.MaxMeleeSafetyBufferYalms;

    /// <summary>
    /// When the GCD queue window is open, budget the full positional arc instead of zero movement.
    /// </summary>
    private static float ResolveGcdBudgetSeconds(float gcdRemainingSeconds)
    {
        if (gcdRemainingSeconds <= PositionalMovementConstants.GcdClipBufferSeconds)
            return float.NaN;

        return gcdRemainingSeconds;
    }
}
