using System;
using System.Numerics;

namespace Daedalus.Services.Positional.Navigation;

/// <summary>
/// Decorator over <see cref="IVNavService"/> that owns the Daedalus↔BossMod movement cadence.
/// Yield rule: BMR danger (zones live, BMR steering, or damage inside the yield window) stops any
/// Daedalus-owned path and denies submissions until the danger clears plus a re-grab cooldown.
/// Churn rules: minimum re-path interval, path commitment window, and minimum destination delta apply
/// everywhere (BMR present or not). <c>Stop()</c> is never gated — telegraph aborts must always land —
/// and foreign paths (AutoDuty, manual /vnav) are never stopped, only paths this arbiter granted.
/// </summary>
public sealed class MovementArbiter : IMovementArbiter
{
    private readonly IVNavService _inner;
    private readonly IBossModSafetyService _bossMod;
    private readonly Func<bool> _yieldEnabled;
    private readonly Func<DateTime> _utcNow;
    private readonly Daedalus.Services.Debug.DebugLogService? _debugLog;

    private bool _dangerThisFrame;
    private bool _dangerLastFrame;
    private bool _steeringThisFrame;
    private bool _realDangerThisFrame;
    private DateTime _lastNavigatingUtc = DateTime.MinValue;
    private DateTime _lastDangerUtc = DateTime.MinValue;
    private DateTime _lastRealDangerUtc = DateTime.MinValue;
    private DateTime _lastQueuedUtc = DateTime.MinValue;
    private Vector3 _lastDestination;
    private bool _weIssuedActivePath;
    private MovementIntent _lastGrantIntent = MovementIntent.MaxMelee;

    // Adaptive regrab: staggered multi-effect mechanics have calm gaps longer than the base cooldown, so a
    // fixed cooldown regrabs mid-sequence and freezes BMR's next dodge (BMR defers to vNav). Each grant
    // that danger interrupts doubles the cooldown, capped; sustained calm resets it.
    private float _regrabCooldownSeconds = PositionalMovementConstants.BmrRegrabCooldownSeconds;

    public MovementArbiter(
        IVNavService inner,
        IBossModSafetyService bossMod,
        Func<bool> yieldEnabled,
        Func<DateTime>? utcNow = null,
        Daedalus.Services.Debug.DebugLogService? debugLog = null)
    {
        _inner = inner;
        _bossMod = bossMod;
        _yieldEnabled = yieldEnabled;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _debugLog = debugLog;
    }

    public MovementArbiterSnapshot Snapshot { get; private set; }

    public bool IsExternalMovementActive { get; private set; }

    // --- IVNavService pass-through reads ---
    public bool IsAvailable => _inner.IsAvailable;
    public bool IsNavReady => _inner.IsNavReady;
    public bool IsPathRunning => _inner.IsPathRunning;
    public bool IsPathfindInProgress => _inner.IsPathfindInProgress;
    public Vector3 SnapToFloor(Vector3 position) => _inner.SnapToFloor(position);

    public void BeginFrame()
    {
        var now = _utcNow();

        // Ownership latch decay: once the granted path fully finishes, the next running path is foreign.
        if (_weIssuedActivePath && !_inner.IsPathRunning && !_inner.IsPathfindInProgress)
            _weIssuedActivePath = false;

        var yieldActive = _bossMod.IsAvailable && _yieldEnabled();
        var zones = yieldActive ? _bossMod.ForbiddenZonesCount : 0;
        var navigating = yieldActive && _bossMod.IsBmrNavigating;
        var nextDamageIn = yieldActive ? _bossMod.NextDamageInSeconds : float.MaxValue;
        var forbiddenIn = yieldActive ? _bossMod.ForbiddenZoneActivationInSeconds : float.MaxValue;

        // Sticky steering: the raw IsNavigating signal flickers per-frame while BMR micro-adjusts follow
        // distance (field-logged onset→clear cycles within 300ms). Debounced over a window instead:
        // BMR seen steering recently = BMR owns movement, don't touch the wheel at all.
        if (navigating)
            _lastNavigatingUtc = now;
        var bmrSteering = navigating
            || (_lastNavigatingUtc > DateTime.MinValue
                && (now - _lastNavigatingUtc).TotalSeconds < PositionalMovementConstants.BmrSteeringStickySeconds);

        // NOTE: deliberately NOT "ForbiddenZonesCount > 0" — zones appear at cast START, 5-7s before
        // activation on trash, and yielding for whole casts parked melee out of range for most of a pull
        // (field log 2026-07-05). Activation-imminent + steering covers the actual dodge window; the
        // destination safety checks in PositionalMovementService already refuse pathing INTO a live zone.
        // Real danger (zone/damage imminent) is tracked separately from steering: positional arcs may run
        // while BMR merely steers, but never through real danger.
        _realDangerThisFrame = yieldActive
            && (nextDamageIn <= PositionalMovementConstants.BmrYieldWindowSeconds
                || forbiddenIn <= PositionalMovementConstants.BmrYieldWindowSeconds);
        _dangerThisFrame = bmrSteering || _realDangerThisFrame;

        if (_dangerThisFrame)
        {
            // Danger onset right after we granted a path = we regrabbed inside a multi-effect sequence
            // and froze BMR's next dodge. Back off harder before the next grab.
            if (!_dangerLastFrame
                && _lastQueuedUtc > DateTime.MinValue
                && (now - _lastQueuedUtc).TotalSeconds < PositionalMovementConstants.BmrRegrabBackoffTriggerSeconds)
            {
                _regrabCooldownSeconds = Math.Min(
                    _regrabCooldownSeconds * 2f,
                    PositionalMovementConstants.BmrRegrabCooldownMaxSeconds);
                LogNav($"regrab backoff escalated to {_regrabCooldownSeconds:0.00}s (grant interrupted by danger)");
            }

            if (!_dangerLastFrame)
                LogNav($"yielding to BossMod (steering {bmrSteering}, zones {zones}, damage {FormatIn(nextDamageIn)}, zone-in {FormatIn(forbiddenIn)})");

            _lastDangerUtc = now;
            if (_realDangerThisFrame)
                _lastRealDangerUtc = now;

            // Cede the input pipeline: BMR skips its own steering while a vNav path runs, so a
            // Daedalus-owned path must stop for the dodge to happen at all. Exception: an arc-intent
            // path during steering-only danger — the ~0.4s hop is the point of the carve-out and BMR
            // isn't dodging anything; real danger still aborts it instantly.
            if (_weIssuedActivePath && _inner.IsPathRunning
                && (_realDangerThisFrame || _lastGrantIntent == MovementIntent.MaxMelee))
            {
                LogNav("stopped owned vNav path for BMR dodge");
                Stop();
            }
        }
        else
        {
            if (_dangerLastFrame)
                LogNav($"danger cleared — regrab in {_regrabCooldownSeconds:0.00}s");

            // Sustained calm: the mechanic sequence is over, drop back to the base cooldown.
            if (_regrabCooldownSeconds > PositionalMovementConstants.BmrRegrabCooldownSeconds
                && _lastDangerUtc > DateTime.MinValue
                && (now - _lastDangerUtc).TotalSeconds >= PositionalMovementConstants.BmrRegrabResetCalmSeconds)
            {
                _regrabCooldownSeconds = PositionalMovementConstants.BmrRegrabCooldownSeconds;
                LogNav("regrab backoff reset (sustained calm)");
            }
        }

        _dangerLastFrame = _dangerThisFrame;

        // Cast-hold signal: only when BMR actually has a nav target (input injection live/starting).
        // NOT the full danger predicate — NextDamageIn also fires on raidwides nobody dodges, and holding
        // every hard-cast 1.5s before each raidwide would be a straight DPS regression.
        IsExternalMovementActive = navigating;

        var regrabRemaining = Math.Max(
            0d,
            _regrabCooldownSeconds - (now - _lastDangerUtc).TotalSeconds);

        _steeringThisFrame = bmrSteering;

        Snapshot = new MovementArbiterSnapshot(
            Owner: _dangerThisFrame
                ? MovementOwner.BossMod
                : _weIssuedActivePath && _inner.IsPathRunning ? MovementOwner.Daedalus : MovementOwner.None,
            Suppression: _dangerThisFrame
                ? (bmrSteering ? MovementSuppression.BmrNavigating : MovementSuppression.BmrDanger)
                : regrabRemaining > 0d && _lastDangerUtc > DateTime.MinValue
                    ? MovementSuppression.RegrabCooldown
                    : MovementSuppression.None,
            ForbiddenZonesCount: zones,
            BmrNavigating: navigating,
            NextDamageInSeconds: nextDamageIn,
            ForbiddenZoneInSeconds: forbiddenIn,
            RegrabCooldownRemainingSeconds: regrabRemaining,
            SecondsSinceLastGrant: _lastQueuedUtc == DateTime.MinValue
                ? double.MaxValue
                : (now - _lastQueuedUtc).TotalSeconds,
            BmrNaviTarget: yieldActive ? _bossMod.BmrNaviTarget : null,
            LastGrantIntent: _lastGrantIntent);
    }

    public VNavMoveResult PathfindAndMoveTo(Vector3 destination, bool fly = false)
        => Gate(destination, MovementIntent.MaxMelee) is { } denied
            ? Deny(denied)
            : Grant(_inner.PathfindAndMoveTo(destination, fly), destination, MovementIntent.MaxMelee);

    public VNavMoveResult PathfindAndMoveCloseTo(Vector3 destination, float toleranceYalms, bool fly = false)
        => PathfindAndMoveCloseTo(destination, toleranceYalms, MovementIntent.MaxMelee, fly);

    public VNavMoveResult PathfindAndMoveCloseTo(Vector3 destination, float toleranceYalms, MovementIntent intent, bool fly = false)
        => Gate(destination, intent) is { } denied
            ? Deny(denied)
            : Grant(_inner.PathfindAndMoveCloseTo(destination, toleranceYalms, fly), destination, intent);

    public void Stop()
    {
        _inner.Stop();
        _weIssuedActivePath = false;
    }

    private MovementSuppression? Gate(Vector3 destination, MovementIntent intent)
    {
        var now = _utcNow();

        if (_dangerThisFrame)
        {
            // Arc carve-out: steering-only danger doesn't block a positional hop (BMR keeps range,
            // Daedalus owns the angle). Real zone/damage danger blocks everything.
            if (intent != MovementIntent.PositionalArc)
                return _steeringThisFrame
                    ? MovementSuppression.BmrNavigating
                    : MovementSuppression.BmrDanger;

            if (_realDangerThisFrame)
                return MovementSuppression.BmrDanger;
        }

        // Regrab: arcs reference the REAL-danger clock — steering refreshes _lastDangerUtc every frame,
        // which would otherwise permanently cooldown-block arcs whenever BMR AI is active.
        var regrabReference = intent == MovementIntent.PositionalArc ? _lastRealDangerUtc : _lastDangerUtc;
        if (_bossMod.IsAvailable && _yieldEnabled()
            && regrabReference > DateTime.MinValue
            && (now - regrabReference).TotalSeconds < _regrabCooldownSeconds)
            return MovementSuppression.RegrabCooldown;

        if (_lastQueuedUtc > DateTime.MinValue
            && (now - _lastQueuedUtc).TotalSeconds < PositionalMovementConstants.MinRepathIntervalSeconds)
            return MovementSuppression.RepathInterval;

        var ownedPathActive = _weIssuedActivePath && (_inner.IsPathRunning || _inner.IsPathfindInProgress);
        if (ownedPathActive
            && (now - _lastQueuedUtc).TotalSeconds < PositionalMovementConstants.PathCommitmentSeconds)
            return MovementSuppression.PathCommitment;

        // Destination memory: the delta rule also applies for a short window AFTER a path completes.
        // Short hops finish inside the repath interval, which used to clear ownership and let max-melee
        // maintenance machine-gun near-identical destinations at ~3Hz while chasing a drifting pack
        // (field log: "granted ×14/×18/×22"). Real corrections (mob fleeing) accumulate >0.75y quickly
        // because _lastDestination only advances on grants.
        var deltaWindowActive = ownedPathActive
            || (_lastQueuedUtc > DateTime.MinValue
                && (now - _lastQueuedUtc).TotalSeconds < PositionalMovementConstants.DestinationMemorySeconds);
        if (deltaWindowActive
            && Vector3.Distance(destination, _lastDestination) < PositionalMovementConstants.MinDestinationDeltaYalms)
            return MovementSuppression.DestinationDelta;

        return null;
    }

    private VNavMoveResult Deny(MovementSuppression reason)
    {
        Snapshot = Snapshot with { Suppression = reason };
        return VNavMoveResult.Suppressed;
    }

    private VNavMoveResult Grant(VNavMoveResult result, Vector3 destination, MovementIntent intent)
    {
        if (result == VNavMoveResult.Queued)
        {
            _lastQueuedUtc = _utcNow();
            _lastDestination = destination;
            _weIssuedActivePath = true;
            _lastGrantIntent = intent;
            Snapshot = Snapshot with
            {
                Owner = MovementOwner.Daedalus,
                Suppression = MovementSuppression.None,
                LastGrantIntent = intent,
            };
            LogNav($"granted vNav path to ({destination.X:0.0}, {destination.Z:0.0})"
                + (intent == MovementIntent.PositionalArc ? " [positional arc]" : ""));
        }

        return result;
    }

    private void LogNav(string message)
        => _debugLog?.Log(
            Daedalus.Services.Debug.DebugLogCategory.Nav,
            Daedalus.Services.Debug.DebugLogSeverity.Info,
            $"[Arbiter] {message}");

    private static string FormatIn(float seconds)
        => seconds >= float.MaxValue * 0.5f ? "—"
            : seconds < -60f ? "active" // BMR reports huge negatives for zones already past activation
            : $"{seconds:0.0}s";
}
