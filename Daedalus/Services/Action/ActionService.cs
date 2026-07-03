using System;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Daedalus.Data;
using Daedalus.Models;
using Daedalus.Models.Action;

namespace Daedalus.Services.Action;

/// <summary>
/// GCD state enumeration.
/// </summary>
public enum GcdState
{
    /// <summary>GCD is ready, can cast immediately.</summary>
    Ready,
    /// <summary>GCD is rolling, waiting for cooldown.</summary>
    Rolling,
    /// <summary>In weave window, can use oGCDs.</summary>
    WeaveWindow,
    /// <summary>Currently casting a spell.</summary>
    Casting,
    /// <summary>In animation lock from recent action.</summary>
    AnimationLock
}

/// <summary>
/// Simplified action execution service (RSR-style reactive).
/// No queuing - calculates and executes best action each frame.
/// </summary>
public sealed unsafe class ActionService : IActionService
{
    private readonly IActionTracker _actionTracker;
    private readonly IErrorMetricsService? _errorMetrics;
    private readonly IObjectTable? _objectTable;
    private readonly IDataManager? _dataManager;
    private readonly WeaveOptimizer _weaveOptimizer;

    // GCD tracking state
    private float _lastGcdTotal;
    private float _lastGcdElapsed;

    // Group 57 reports a 0 total while the GCD is ready, which is exactly when modules evaluate
    // the next cast. Cache the last rolling total so GCD-relative timing has a stable duration.
    private float _lastKnownGcdTotal = 2.5f;
    private float _lastAnimationLock;
    private bool _lastIsCasting;
    // True once a hard cast was observed in the current GCD cycle; cleared on rollover. Used to detect
    // the post-cast tail so casters get their single weave slot there (see GetAvailableWeaveSlots).
    private bool _castSeenThisCycle;

    // Last executed action (for debugging)
    private ActionDefinition? _lastExecutedAction;
    private DateTime _lastExecuteTime;
    // Why the last ExecuteGcd returned false due to an INTERNAL guard (not a game UseAction refusal).
    // Null when the last GCD succeeded or was refused by the game (scheduler enriches that case).
    private string? _lastGcdRejectReason;

    // Curated diagnostic log (refused casts surface here, separate from the Dalamud log). Nullable.
    private readonly Daedalus.Services.Debug.DebugLogService? _debugLog;
    // Display name of the last GCD we submitted, for the "submitted but not cast" diagnostic.
    private string _lastSubmittedActionName = "";

    // Deferred GCD logging: the game QUEUES a submit and only the last one per GCD window actually fires, so
    // the scheduler's multiple submits-per-window would inflate the action log/uptime with casts that never
    // happened. We stash the last submit here and flush it to the ActionTracker only when a new GCD really
    // fires (recast rolls over — detected in UpdateGcdState), so the log reflects one entry per real cast.
    private bool _hasPendingGcdLog;
    private uint _pendingGcdLogActionId;
    private ulong _pendingGcdLogTargetId;
    private float _pendingGcdLogDuration;
    private float _pendingGcdLogAoeRadius;

    // Minimal action history (last GCD / last oGCD id) for oGCD sequencing consumers.
    private readonly ActionHistory _history = new();

    // Track oGCD usage per GCD cycle (allows up to 2 weaves)
    private int _ogcdsUsedThisCycle;
    private float _prevGcdRemaining;
    private float _prevRecastElapsed;

    // Guard so modules can't spam UseAction every frame while GcdRemaining stays at 0
    // after a successful submit but before recast group 57 activates.
    private bool _gcdSubmittedThisCycle;
    // Latches the one-time queue-window release of the submit guard per GCD cycle. Without it the
    // still-active (near-complete) outgoing recast keeps re-satisfying HasCompletedSubmittedRecast
    // every frame, re-clearing the guard and letting ExecuteGcd re-submit the same GCD every frame
    // for the whole ~0.5s queue window (observed as melee skill spam, e.g. 17x Spinning Edge).
    private bool _queueWindowReleasedThisCycle;
    // Last adjusted GCD dispatch id accepted this recast-group-57 cycle. Cleared on rollover so the
    // next cycle can fire the same action again, but blocks duplicate UseAction for the same GCD
    // (e.g. Phlegma III / Eukrasian Dosis III double-feed) if the submit guard flaps mid queue window.
    private uint _blockedRepeatGcdDispatchId;
    // Short post-submit block for instant oGCDs direct-dispatched during CollectCandidates (SGE
    // Eukrasia) before the game's cooldown/status catches up on the next frame.
    private uint _blockedRepeatOgcdId;
    private DateTime _blockedRepeatOgcdUntil = DateTime.MinValue;
    // Floor for charge-based GCDs (Phlegma, etc.): block a second submit until one full GCD
    // window has elapsed. Covers sub-second double-feed when burst/queue latches flap.
    private DateTime _lastChargeBasedGcdSubmitUtc = DateTime.MinValue;
    private bool _recastGroupWasActive;
    private bool _gcdRecastSeenSinceSubmit;
    private float _peakRecastElapsedSinceSubmit;
    private float _peakRecastTotalSinceSubmit;
    private DateTime _nextGcdAttemptAllowed = DateTime.MinValue;
    // Last GCD submitted via UseAction this cycle (adjusted id + target). Used to query the real
    // GetActionStatus(id, target) reason and trigger a face-the-target recovery when a submit is
    // accepted by the queue but never commits (no recast) — the multi-mob "Fire in Red" spam case.
    private uint _lastSubmittedDispatchId;
    private ulong _lastSubmittedTargetId;
    // FFXIV GetActionStatus LogMessage code: target not within the facing cone.
    private const uint StatusNotFacing = 566;

    private const float MinRecastCompletionRatio = 0.85f;
    private const double UncommittedSubmitStaleSeconds = 0.5;
    private const double PartialRecastStaleSeconds = 1.5;
    private const double FailedSubmitBackoffSeconds = 0.1;

    /// <summary>Current GCD state.</summary>
    public GcdState CurrentGcdState { get; private set; } = GcdState.Ready;

    /// <summary>Time remaining on GCD (0 if ready).</summary>
    public float GcdRemaining => Math.Max(0, _lastGcdTotal - _lastGcdElapsed);

    /// <summary>
    /// Live GCD duration in seconds (recast group 57 total), scaled by skill speed / haste.
    /// Falls back to the last rolling value while the GCD is ready (group 57 reports 0 then),
    /// defaulting to 2.5s before the first GCD has rolled.
    /// </summary>
    public float GcdDuration => _lastKnownGcdTotal;

    /// <inheritdoc/>
    public string? LastGcdRejectReason => _lastGcdRejectReason;

    /// <inheritdoc/>
    public double SecondsSinceLastAction =>
        _lastExecuteTime == default ? double.MaxValue : (DateTime.UtcNow - _lastExecuteTime).TotalSeconds;

    /// <summary>Animation lock remaining.</summary>
    public float AnimationLockRemaining => Math.Max(0, _lastAnimationLock);

    /// <summary>Whether player is currently casting.</summary>
    public bool IsCasting => _lastIsCasting;

    /// <summary>
    /// Whether GCD is ready for a new action.
    /// True during the queue window (last <see cref="FFXIVTimings.QueueWindow"/>s) as well as at true rollover
    /// so we can submit into the server-side action queue and avoid eating a full latency round-trip on every GCD.
    /// </summary>
    public bool CanExecuteGcd => CurrentGcdState == GcdState.Ready;

    /// <summary>Whether we can weave an oGCD right now.</summary>
    public bool CanExecuteOgcd => IsInWeaveWindow();

    /// <inheritdoc/>
    public Func<ulong, bool>? KardiaRecastGuard { get; set; }

    /// <inheritdoc/>
    public Action<ulong>? FaceTargetOnStuck { get; set; }

    /// <inheritdoc/>
    public Func<ulong>? HardTargetIdProvider { get; set; }

    /// <summary>Last executed action (for debugging).</summary>
    public ActionDefinition? LastExecutedAction => _lastExecutedAction;

    /// <inheritdoc/>
    public uint LastOgcdId => _history.LastOgcdId;

    /// <inheritdoc/>
    public bool WasLastGcd(uint actionId) => _history.WasLastGcd(actionId);

    /// <inheritdoc/>
    public bool WasLastOgcd(uint actionId) => _history.WasLastOgcd(actionId);

    /// <inheritdoc/>
    public bool WasLastAction(uint actionId) => _history.WasLastAction(actionId);

    /// <inheritdoc/>
    public void RecordActionExecuted(uint actionId) => _history.RecordAction(actionId);

    /// <inheritdoc/>
    public void RecordGcdExecuted(uint actionId) => _history.RecordGcd(actionId);

    /// <inheritdoc/>
    public void NotifyActionExecuted(ActionDefinition action, uint recordActionId = 0)
    {
        var historyId = recordActionId != 0 ? recordActionId : action.ActionId;

        _lastExecutedAction = action;
        _lastExecuteTime = DateTime.UtcNow;

        if (action.IsGCD)
        {
            _history.RecordGcd(historyId);
            _gcdSubmittedThisCycle = true;
            var dispatchId = recordActionId != 0 ? recordActionId : action.ActionId;
            _blockedRepeatGcdDispatchId = dispatchId;
            RecordChargeBasedGcdSubmit(dispatchId);

            // Raw ActionManager dispatches (Hermes mudra/ninjutsu/TCJ) bypass ExecuteGcd but still consume GCD.
            var actionManager = SafeGameAccess.GetActionManager(_errorMetrics);
            if (actionManager is not null)
            {
                var recastActionId = recordActionId != 0 ? recordActionId : action.ActionId;
                var gcdDuration = actionManager->GetRecastTime(ActionType.Action, recastActionId);
                _actionTracker.LogGcdCast(gcdDuration);
            }
        }
        else
        {
            _history.RecordOgcd(historyId);
        }

        _actionTracker.LogAttempt(action.ActionId, null, null, ActionResult.Success, 0);
        RaiseActionExecuted(action);
    }

    /// <summary>
    /// Fired after a successful action execution. Used by the action feed overlay.
    /// </summary>
    public event Action<ActionExecutedEvent>? ActionExecuted;

    /// <summary>Gets the WeaveOptimizer for intelligent oGCD timing.</summary>
    public IWeaveOptimizer WeaveOptimizer => _weaveOptimizer;

    public ActionService(
        IActionTracker actionTracker,
        IErrorMetricsService? errorMetrics = null,
        IObjectTable? objectTable = null,
        IDataManager? dataManager = null,
        Daedalus.Services.Debug.DebugLogService? debugLog = null)
    {
        _actionTracker = actionTracker;
        _errorMetrics = errorMetrics;
        _objectTable = objectTable;
        _dataManager = dataManager;
        _debugLog = debugLog;
        _weaveOptimizer = new WeaveOptimizer();
    }

    /// <summary>
    /// Called every frame to update GCD state.
    /// </summary>
    public void Update(bool isCasting)
    {
        var actionManager = SafeGameAccess.GetActionManager(_errorMetrics);
        if (actionManager is null)
            return;

        _lastIsCasting = isCasting;
        if (isCasting)
            _castSeenThisCycle = true;
        UpdateGcdState(actionManager);

        // Update WeaveOptimizer with current state
        _weaveOptimizer.Update(GcdRemaining, _lastGcdTotal, AnimationLockRemaining, _ogcdsUsedThisCycle);
    }

    private void UpdateGcdState(ActionManager* actionManager)
    {
        // Group 57 is hardcoded by the game as the global GCD recast group
        // Works for all jobs (caster, healer, tank, melee, ranged)
        var recastDetail = actionManager->GetRecastGroupDetail(57);
        var recastActive = recastDetail is not null && recastDetail->IsActive;

        if (recastActive && _gcdSubmittedThisCycle && recastDetail is not null)
        {
            _gcdRecastSeenSinceSubmit = true;
            if (recastDetail->Elapsed > _peakRecastElapsedSinceSubmit)
                _peakRecastElapsedSinceSubmit = recastDetail->Elapsed;
            if (recastDetail->Total > _peakRecastTotalSinceSubmit)
                _peakRecastTotalSinceSubmit = recastDetail->Total;
        }

        // Current GCD far enough along — release the guard ONCE so the queue window can submit the
        // next GCD. The per-cycle latch is critical: after a release+submit resets the peak trackers,
        // the next frame repopulates them from the still-active (~99% complete) outgoing recast, which
        // would otherwise re-satisfy the 85% threshold and re-clear the guard every frame.
        if (_gcdSubmittedThisCycle && recastActive && !_queueWindowReleasedThisCycle
            && HasCompletedSubmittedRecast())
        {
            _gcdSubmittedThisCycle = false;
            _queueWindowReleasedThisCycle = true;
        }

        // Recast group 57 rolled over: the queued GCD has begun its own recast (or the queue emptied).
        // Re-arm the per-cycle release latch and reset the peak trackers. The submit guard intentionally
        // carries the "next GCD already queued" state into the new cycle; it is released again at ~85%,
        // or recovered by the stale-guard timeouts below if the queued action never committed.
        if (_recastGroupWasActive && !recastActive)
        {
            _queueWindowReleasedThisCycle = false;
            if (!_gcdSubmittedThisCycle
                && (DateTime.UtcNow - _lastExecuteTime).TotalSeconds > 0.5)
            {
                _blockedRepeatGcdDispatchId = 0;
            }
            _lastChargeBasedGcdSubmitUtc = DateTime.MinValue;
            _peakRecastElapsedSinceSubmit = 0;
            _peakRecastTotalSinceSubmit = 0;
            _gcdRecastSeenSinceSubmit = false;
        }

        var secondsSinceSubmit = (DateTime.UtcNow - _lastExecuteTime).TotalSeconds;

        // Stale guard: submit accepted but the GCD isn't actually casting. Either recast group 57 never
        // activated (queued action rejected at fire time for facing / LoS / range), OR the recast is no
        // longer active while the latch is still set — the deadlock where a cast was seen + read as
        // "completed" but the GCD is sitting Ready, so neither the 85% release (needs recastActive) nor
        // stale-guard #2 (needs !HasCompletedSubmittedRecast) clears it, latching "already submitted this
        // GCD cycle" indefinitely. The `!recastActive` term recovers that: no active recast + >0.5s since
        // submit means nothing is in flight. The time threshold protects the normal queue-window carry
        // (the next GCD's recast activates within a frame, so secondsSinceSubmit stays tiny there).
        if (_gcdSubmittedThisCycle && (!_gcdRecastSeenSinceSubmit || !recastActive)
            && secondsSinceSubmit > UncommittedSubmitStaleSeconds)
        {
            _gcdSubmittedThisCycle = false;
            _nextGcdAttemptAllowed = DateTime.UtcNow.AddSeconds(FailedSubmitBackoffSeconds);

            if (_lastSubmittedTargetId != 0)
            {
                var status = GetActionStatusCode(_lastSubmittedDispatchId, _lastSubmittedTargetId);
                var reason = DescribeStatusReason(status);
                _lastGcdRejectReason = reason is not null
                    ? $"submitted but not cast — {reason}"
                    : status != 0
                        ? $"submitted but not cast (game status {status})"
                        : "submitted but not cast (line-of-sight / facing / moving?)";
                TryFaceRecovery(_lastSubmittedDispatchId, _lastSubmittedTargetId);
                LogCastRefusal(_lastSubmittedActionName, _lastSubmittedDispatchId, _lastSubmittedTargetId,
                    submittedNotCast: true);
            }
        }

        // Stale guard: recast blip without a full roll (guard would otherwise stay latched indefinitely).
        if (_gcdSubmittedThisCycle && _gcdRecastSeenSinceSubmit && !HasCompletedSubmittedRecast()
            && secondsSinceSubmit > PartialRecastStaleSeconds)
        {
            _gcdSubmittedThisCycle = false;
            _nextGcdAttemptAllowed = DateTime.UtcNow.AddSeconds(FailedSubmitBackoffSeconds);
        }

        _recastGroupWasActive = recastActive;

        _lastAnimationLock = actionManager->AnimationLock;

        if (recastActive)
        {
            _lastGcdTotal = recastDetail->Total;
            _lastGcdElapsed = recastDetail->Elapsed;
            if (_lastGcdTotal > 0)
                _lastKnownGcdTotal = _lastGcdTotal;
        }
        else
        {
            _lastGcdTotal = 0;
            _lastGcdElapsed = 0;
        }

        // Detect new GCD cycle: GcdRemaining jumped up, meaning a new GCD fired via the queue window.
        // Without this, _ogcdsUsedThisCycle accumulates across queue-submitted GCDs (e.g. Heat Blast spam)
        // and blocks all oGCD weaving during Overheat. This is also the moment a queued GCD actually COMMITS,
        // so flush the deferred GCD log here — one entry per real cast instead of per submit.
        // A real new cycle also RESTARTS the recast group: Elapsed snaps back below the previous
        // frame's value (or the group re-activates). A bare GcdRemaining increase can also come
        // from Total/Elapsed jitter without any new cast — that fired phantom log flushes at
        // sub-GCD spacing (triple Verstone, Mistwake 2026-07-02) and spuriously reset the weave
        // counter, so the elapsed-restart signal is required as well.
        var recastElapsedNow = recastActive ? recastDetail->Elapsed : 0f;
        var recastRestarted = recastActive
            && (_prevRecastElapsed <= 0f || recastElapsedNow < _prevRecastElapsed - 0.05f);
        if (GcdRemaining > _prevGcdRemaining + 0.3f && recastRestarted)
        {
            _ogcdsUsedThisCycle = 0;
            _castSeenThisCycle = false;
            FlushPendingGcdLog();
        }
        _prevGcdRemaining = GcdRemaining;
        _prevRecastElapsed = recastElapsedNow;

        // Determine current state
        if (_lastIsCasting)
        {
            CurrentGcdState = GcdState.Casting;
        }
        else if (_lastAnimationLock > FFXIVConstants.WeaveWindowBuffer)
        {
            CurrentGcdState = GcdState.AnimationLock;
        }
        else if (GcdRemaining <= 0)
        {
            CurrentGcdState = GcdState.Ready;
            _ogcdsUsedThisCycle = 0;
            // Normally clear the submit latch so the next GCD can fire. EXCEPTION: a submit was accepted
            // this cycle but no recast has started (the action was queued but rejected at execution —
            // facing / LoS / range). Clearing here every frame is what let it re-submit ~5x/second (the
            // multi-mob "Fire in Red" spam). Hold the latch through the uncommitted grace so the stale
            // guard below clears it once (with backoff + face recovery) instead of spamming.
            var submitUncommitted = _gcdSubmittedThisCycle && !_gcdRecastSeenSinceSubmit
                && (DateTime.UtcNow - _lastExecuteTime).TotalSeconds <= UncommittedSubmitStaleSeconds;
            if (!submitUncommitted)
                _gcdSubmittedThisCycle = false;
            // Recast is fully done — a new GCD cycle. Release the repeat-GCD block here too, not only on
            // the single recast roll-over frame: if that frame's clear was missed (e.g. _gcdSubmittedThisCycle
            // was still latched) AND no further GCD fires, the roll-over never recurs and the block would
            // deadlock the rotation (observed: "Slug Shot: repeat-GCD guard" stuck for 12s). Re-submitting
            // the same GCD once the recast is complete is legitimate.
            _blockedRepeatGcdDispatchId = 0;
        }
        else if (GcdRemaining <= FFXIVTimings.QueueWindow)
        {
            // Queue window: submit the next GCD early so the game's action queue fires it on rollover.
            CurrentGcdState = GcdState.Ready;
        }
        else if (IsInWeaveWindow())
        {
            CurrentGcdState = GcdState.WeaveWindow;
        }
        else
        {
            CurrentGcdState = GcdState.Rolling;
        }
    }

    /// <summary>
    /// Execute a GCD action immediately.
    /// Call this when GCD is ready and you've determined the best action.
    /// </summary>
    /// <returns>True if action was executed successfully.</returns>
    public bool ExecuteGcd(ActionDefinition action, ulong targetId)
    {
        if (!action.IsGCD)
            return false;
        _lastGcdRejectReason = null;

        var actionManager = SafeGameAccess.GetActionManager(_errorMetrics);
        if (actionManager is null)
        {
            _lastGcdRejectReason = "action manager unavailable";
            return false;
        }

        // If we already submitted a GCD for this cycle, don't spam UseAction every frame.
        if (_gcdSubmittedThisCycle)
        {
            _lastGcdRejectReason = "already submitted this GCD cycle";
            return false;
        }

        var dispatchId = actionManager->GetAdjustedActionId(action.ActionId);
        if (ShouldBlockRepeatGcd(dispatchId, actionManager))
        {
            _lastGcdRejectReason = "repeat-GCD guard (awaiting recast roll-over)";
            return false;
        }

        if (IsChargeBasedGcd(action.ActionId, actionManager) && IsChargeBasedGcdSubmitBlocked())
        {
            _lastGcdRejectReason = "charge-GCD submit guard";
            return false;
        }

        if (DateTime.UtcNow < _nextGcdAttemptAllowed)
        {
            _lastGcdRejectReason = "submit backoff (recent failed submit)";
            return false;
        }

        // Do NOT pre-check GetActionStatus here: while the global GCD is rolling it returns 583 ("not ready"),
        // but UseAction still accepts the call in the last ~0.5s and queues the action to fire on rollover.
        // Pre-checking defeats the server-side action queue, so we delegate the "can fire now?" decision to UseAction.
        var result = actionManager->UseAction(ActionType.Action, dispatchId, targetId);
        if (!result)
        {
            _lastGcdRejectReason = null; // game refused — scheduler enriches with range/status/LoS
            // Facing recovery: the game refused outright; if it's because we aren't facing the target,
            // force a re-face (hard-target) so the next attempt lands instead of stalling (PLD case).
            TryFaceRecovery(dispatchId, targetId);
            LogCastRefusal(action.Name, dispatchId, targetId, submittedNotCast: false);
        }

        if (result)
        {
            _gcdSubmittedThisCycle = true;
            _lastSubmittedDispatchId = dispatchId;
            _lastSubmittedTargetId = targetId;
            _lastSubmittedActionName = action.Name;
            _blockedRepeatGcdDispatchId = dispatchId;
            RecordChargeBasedGcdSubmit(dispatchId);
            _gcdRecastSeenSinceSubmit = false;
            _peakRecastElapsedSinceSubmit = 0;
            _peakRecastTotalSinceSubmit = 0;

            _lastExecutedAction = action;
            _lastExecuteTime = DateTime.UtcNow;
            _history.RecordGcd(action.ActionId);

            // Defer the ActionTracker log to commit (see _hasPendingGcdLog): the game queues this submit and
            // only the last one this GCD window actually fires, so logging here inflates the timeline.
            _pendingGcdLogActionId = action.ActionId;
            _pendingGcdLogTargetId = targetId;
            _pendingGcdLogDuration = actionManager->GetRecastTime(ActionType.Action, dispatchId);
            _pendingGcdLogAoeRadius = action.Radius;
            _hasPendingGcdLog = true;
            RaiseActionExecuted(action);
        }

        return result;
    }

    /// <summary>
    /// Execute an oGCD action immediately.
    /// Call this during weave windows.
    /// </summary>
    /// <returns>True if action was executed successfully.</returns>
    public bool ExecuteOgcd(ActionDefinition action, ulong targetId)
    {
        if (!action.IsOGCD)
            return false;

        if (action.ActionId == ActionIds.Kardia
            && KardiaRecastGuard?.Invoke(targetId) == true)
        {
            return false;
        }

        if (action.ActionId == _blockedRepeatOgcdId && DateTime.UtcNow < _blockedRepeatOgcdUntil)
            return false;

        var actionManager = SafeGameAccess.GetActionManager(_errorMetrics);
        if (actionManager is null)
            return false;

        // Check if action can be executed
        if (actionManager->GetActionStatus(ActionType.Action, action.ActionId) != 0)
            return false;

        // Execute
        var result = actionManager->UseAction(ActionType.Action, action.ActionId, targetId);

        if (result)
        {
            _lastExecutedAction = action;
            _lastExecuteTime = DateTime.UtcNow;
            _history.RecordOgcd(action.ActionId);
            _ogcdsUsedThisCycle++;
            _blockedRepeatOgcdId = action.ActionId;
            _blockedRepeatOgcdUntil = DateTime.UtcNow.AddSeconds(1.0);
            var (oName, oHp) = ResolveTargetInfo(targetId);
            _actionTracker.LogAttempt(action.ActionId, oName, oHp, ActionResult.Success, 0);
            RaiseActionExecuted(action);
        }

        return result;
    }

    /// <summary>
    /// Execute a ground-targeted oGCD action at a specific position.
    /// Used for abilities like Asylum, Liturgy of the Bell that place effects on the ground.
    /// </summary>
    /// <returns>True if action was executed successfully.</returns>
    public bool ExecuteGroundTargetedOgcd(ActionDefinition action, Vector3 targetPosition)
    {
        if (!action.IsOGCD)
            return false;

        var actionManager = SafeGameAccess.GetActionManager(_errorMetrics);
        if (actionManager is null)
            return false;

        // Check if action can be executed
        if (actionManager->GetActionStatus(ActionType.Action, action.ActionId) != 0)
            return false;

        // Execute at target location
        var result = actionManager->UseActionLocation(ActionType.Action, action.ActionId, 0xE0000000, &targetPosition);

        if (result)
        {
            _lastExecutedAction = action;
            _lastExecuteTime = DateTime.UtcNow;
            _history.RecordOgcd(action.ActionId);
            _ogcdsUsedThisCycle++; // Increment oGCD count for double-weave tracking
            _blockedRepeatOgcdId = action.ActionId;
            _blockedRepeatOgcdUntil = DateTime.UtcNow.AddSeconds(1.0);
            _actionTracker.LogAttempt(action.ActionId, null, null, ActionResult.Success, 0);
            RaiseActionExecuted(action);
        }

        return result;
    }

    /// <summary>
    /// Execute a GCD targeting the optimal enemy for a directional AoE (cone/line).
    /// The game auto-faces toward the target, so by picking the right target
    /// we control the cone/line direction to hit the most enemies.
    /// </summary>
    public bool ExecuteDirectionalGcd(ActionDefinition action, ulong optimalTargetId)
    {
        // Just a regular ExecuteGcd with the smart-selected target
        return ExecuteGcd(action, optimalTargetId);
    }

    /// <inheritdoc/>
    public bool ExecuteGcdRaw(ActionDefinition action, uint rawDispatchId, ulong targetId)
    {
        var actionManager = SafeGameAccess.GetActionManager(_errorMetrics);
        if (actionManager is null)
            return false;

        // Same spam guard as ExecuteGcd — the Raw bypass is for validation checks,
        // not for cycle accounting.
        if (_gcdSubmittedThisCycle)
            return false;

        if (ShouldBlockRepeatGcd(rawDispatchId, actionManager))
            return false;

        if (IsChargeBasedGcd(action.ActionId, actionManager) && IsChargeBasedGcdSubmitBlocked())
            return false;

        if (DateTime.UtcNow < _nextGcdAttemptAllowed)
            return false;

        var result = actionManager->UseAction(ActionType.Action, rawDispatchId, targetId);

        if (result)
        {
            _gcdSubmittedThisCycle = true;
            _blockedRepeatGcdDispatchId = actionManager->GetAdjustedActionId(rawDispatchId);
            RecordChargeBasedGcdSubmit(rawDispatchId);
            _gcdRecastSeenSinceSubmit = false;
            _peakRecastElapsedSinceSubmit = 0;
            _peakRecastTotalSinceSubmit = 0;

            _lastExecutedAction = action;
            _lastExecuteTime = DateTime.UtcNow;
            _history.RecordGcd(action.ActionId);

            // Defer the ActionTracker log to commit (see ExecuteGcd / _hasPendingGcdLog).
            _pendingGcdLogActionId = action.ActionId;
            _pendingGcdLogTargetId = targetId;
            _pendingGcdLogDuration = actionManager->GetRecastTime(ActionType.Action, rawDispatchId);
            _pendingGcdLogAoeRadius = action.Radius;
            _hasPendingGcdLog = true;
            RaiseActionExecuted(action);
        }

        return result;
    }

    /// <inheritdoc/>
    public bool ExecuteOgcdRaw(ActionDefinition action, uint rawDispatchId, ulong targetId)
    {
        if (action.ActionId == _blockedRepeatOgcdId && DateTime.UtcNow < _blockedRepeatOgcdUntil)
            return false;

        var actionManager = SafeGameAccess.GetActionManager(_errorMetrics);
        if (actionManager is null)
            return false;

        // NO GetActionStatus pre-check — that's what "Raw" intentionally bypasses.
        var result = actionManager->UseAction(ActionType.Action, rawDispatchId, targetId);

        if (result)
        {
            _lastExecutedAction = action;
            _lastExecuteTime = DateTime.UtcNow;
            _history.RecordOgcd(action.ActionId);
            _ogcdsUsedThisCycle++;
            _blockedRepeatOgcdId = action.ActionId;
            _blockedRepeatOgcdUntil = DateTime.UtcNow.AddSeconds(1.0);
            var (rwName, rwHp) = ResolveTargetInfo(targetId);
            _actionTracker.LogAttempt(action.ActionId, rwName, rwHp, ActionResult.Success, 0);
            RaiseActionExecuted(action);
        }

        return result;
    }

    /// <inheritdoc/>
    public bool ExecuteItem(uint itemId, bool preferHq, ulong targetId)
    {
        var actionManager = SafeGameAccess.GetActionManager(_errorMetrics);
        if (actionManager is null) return false;

        var resolvedId = preferHq ? itemId + 1_000_000u : itemId;
        // extraParam: 0xFFFF is the standard "use any quality" sentinel for items.
        return actionManager->UseAction(ActionType.Item, resolvedId, targetId, 0xFFFF);
    }

    /// <inheritdoc/>
    public uint GetAdjustedActionId(uint baseActionId)
    {
        var am = SafeGameAccess.GetActionManager(_errorMetrics);
        if (am == null) return baseActionId;
        return am->GetAdjustedActionId(baseActionId);
    }

    /// <summary>
    /// Blocks re-submitting the same GCD dispatch id within one cycle, except when the hotbar
    /// slot has combo-advanced (same base id, new adjusted action — e.g. Total Eclipse → Prominence).
    /// </summary>
    private bool ShouldBlockRepeatGcd(uint useActionId, ActionManager* actionManager)
    {
        if (_blockedRepeatGcdDispatchId == 0 || useActionId != _blockedRepeatGcdDispatchId)
            return false;

        var slotAdjusted = actionManager->GetAdjustedActionId(useActionId);
        return slotAdjusted == useActionId;
    }

    /// <summary>
    /// Writes the just-committed GCD to the action tracker. Called from <see cref="UpdateGcdState"/> the frame
    /// a new GCD actually fires (recast rolled over), NOT on submit — so the action log and GCD-uptime reflect
    /// one entry per real cast instead of the multiple queue submissions the scheduler makes per window.
    /// </summary>
    private void FlushPendingGcdLog()
    {
        if (!_hasPendingGcdLog)
            return;
        _hasPendingGcdLog = false;
        _actionTracker.LogGcdCast(_pendingGcdLogDuration);
        var (name, hp) = ResolveTargetInfo(_pendingGcdLogTargetId);
        var aoeCount = CountEnemiesNearTarget(_pendingGcdLogTargetId, _pendingGcdLogAoeRadius);
        _actionTracker.LogAttempt(_pendingGcdLogActionId, name, hp, ActionResult.Success, 0, aoeTargetCount: aoeCount);
    }

    private (string? name, uint? hp) ResolveTargetInfo(ulong targetId)
    {
        if (targetId == 0 || _objectTable == null)
            return (null, null);
        var obj = _objectTable.SearchById(targetId);
        if (obj is IBattleChara bc)
            return (bc.Name?.TextValue, bc.CurrentHp);
        return (obj?.Name?.TextValue, null);
    }

    /// <summary>
    /// Counts alive hostile battle NPCs within <paramref name="radius"/> yalms of the given object
    /// (AoE validation aid for the action log — approximate: circle around the primary target, so
    /// cones/lines over-count slightly). Returns null for non-AoE actions (radius 0) or when the
    /// center can't be resolved.
    /// </summary>
    private int? CountEnemiesNearTarget(ulong targetId, float radius)
    {
        if (radius <= 0f || targetId == 0 || _objectTable == null)
            return null;
        var center = _objectTable.SearchById(targetId);
        if (center == null)
            return null;

        var centerPos = center.Position;
        var radiusSq = radius * radius;
        var count = 0;
        foreach (var obj in _objectTable)
        {
            if (obj is not IBattleNpc npc) continue;
            // Same combatant filter as TargetingService (BattleNpcKinds.Combatant + SubKind).
            if ((byte)npc.BattleNpcKind != Daedalus.Compat.BattleNpcKinds.Combatant && npc.SubKind != 0) continue;
            if (npc.CurrentHp == 0 || !npc.IsTargetable) continue;
            if (Vector3.DistanceSquared(npc.Position, centerPos) > radiusSq) continue;
            count++;
        }
        return count;
    }

    /// <inheritdoc/>
    public bool PlayerHasStatus(uint statusId)
    {
        var player = _objectTable?.LocalPlayer;
        if (player?.StatusList == null) return false;
        foreach (var status in player.StatusList)
        {
            if (status != null && status.StatusId == statusId) return true;
        }
        return false;
    }

    /// <summary>
    /// Checks if we're in a valid weave window for oGCDs.
    /// Supports double-weaving when timing allows.
    /// </summary>
    public bool IsInWeaveWindow()
    {
        // In weave window if:
        // 1. Not casting
        // 2. No animation lock blocking us
        // 3. Have available weave slots remaining
        // 4. Not inside the GCD queue window (last 0.5s is reserved for the next GCD's early-submit)
        var availableSlots = GetAvailableWeaveSlots();
        if (_lastIsCasting || AnimationLockRemaining >= FFXIVConstants.WeaveWindowBuffer)
            return false;
        if (availableSlots <= _ogcdsUsedThisCycle)
            return false;
        if (GcdRemaining > 0 && GcdRemaining <= FFXIVTimings.QueueWindow)
            return false;
        return true;
    }

    /// <summary>
    /// Checks if it's safe to weave an oGCD without clipping the GCD.
    /// Returns true if GcdRemaining > oGcdAnimationLock + ClipPreventionBuffer.
    /// Use this before executing oGCDs to prevent DPS loss from GCD delays.
    /// </summary>
    /// <param name="oGcdAnimationLock">Animation lock of the oGCD (default: 0.6s for most oGCDs).</param>
    /// <returns>True if the oGCD can be safely weaved without clipping.</returns>
    public bool IsSafeToWeave(float oGcdAnimationLock = FFXIVTimings.AnimationLockBase)
    {
        // Not safe if we're casting or already in animation lock
        if (_lastIsCasting || AnimationLockRemaining > FFXIVConstants.WeaveWindowBuffer)
            return false;

        // Calculate if there's enough time for the animation lock to complete
        // before the GCD comes back up
        var requiredTime = oGcdAnimationLock + FFXIVTimings.ClipPreventionBuffer;
        return GcdRemaining >= requiredTime;
    }

    /// <summary>
    /// Checks if a specific oGCD would clip the GCD if used now.
    /// Returns true if using this oGCD would delay the next GCD.
    /// </summary>
    /// <param name="oGcdAnimationLock">Animation lock of the oGCD.</param>
    /// <returns>True if executing the oGCD would cause clipping.</returns>
    public bool WouldClipGcd(float oGcdAnimationLock = FFXIVTimings.AnimationLockBase)
    {
        // If GCD is ready (not rolling), no clipping concern
        if (GcdRemaining <= 0)
            return false;

        // Would clip if animation lock extends past when GCD becomes ready
        var animationEndTime = AnimationLockRemaining + oGcdAnimationLock;
        return animationEndTime > GcdRemaining;
    }

    /// <summary>Number of oGCDs used this GCD cycle.</summary>
    public int OgcdsUsedThisCycle => _ogcdsUsedThisCycle;

    /// <summary>Whether another oGCD can be weaved this cycle.</summary>
    public bool CanWeaveAnother => GetAvailableWeaveSlots() > _ogcdsUsedThisCycle;

    /// <summary>
    /// Gets cooldown remaining for a specific action.
    /// </summary>
    public float GetCooldownRemaining(uint actionId)
    {
        var actionManager = SafeGameAccess.GetActionManager(_errorMetrics);
        if (actionManager is null)
            return float.MaxValue;

        var elapsed = actionManager->GetRecastTimeElapsed(ActionType.Action, actionId);
        var total = actionManager->GetRecastTime(ActionType.Action, actionId);

        // Some actions (e.g. GNB Bloodfest, id 16164) return 0 from GetRecastTime-by-id even while on
        // cooldown — a ClientStructs quirk where the action→recast-group lookup fails. RSR avoids this by
        // reading the recast GROUP detail directly. Fall back to that when the by-id read comes back empty.
        if (total <= 0)
        {
            var group = ActionUnlockHelper.GetCooldownGroup(_dataManager, actionId);
            if (group > 0)
            {
                var detail = actionManager->GetRecastGroupDetail((byte)(group - 1));
                if (detail is not null && detail->IsActive && detail->Total > 0)
                {
                    total = detail->Total;
                    elapsed = detail->Elapsed;
                }
            }
        }

        if (total <= 0)
            return 0;

        return Math.Max(0, total - elapsed);
    }

    /// <inheritdoc />
    public float GetRecastTimeElapsed(uint actionId)
    {
        var actionManager = SafeGameAccess.GetActionManager(_errorMetrics);
        if (actionManager is null)
            return 0f;

        return actionManager->GetRecastTimeElapsed(ActionType.Action, actionId);
    }

    /// <summary>
    /// Checks if a specific action is ready to use.
    /// For charge-based abilities, returns true if any charges are available.
    /// For non-charge abilities, returns true if cooldown is complete.
    /// </summary>
    public bool IsActionReady(uint actionId)
    {
        // For charge-based abilities, check if any charges are available
        // GetCurrentCharges returns 1 for non-charge abilities when ready, 0 when on cooldown
        return GetCurrentCharges(actionId) > 0;
    }

    /// <inheritdoc/>
    public bool CanExecuteAction(ActionDefinition action)
        => CanExecuteActionId(GetAdjustedActionId(action.ActionId));

    /// <inheritdoc/>
    public bool CanExecuteActionId(uint actionId)
    {
        var actionManager = SafeGameAccess.GetActionManager(_errorMetrics);
        if (actionManager is null)
            return false;

        return actionManager->GetActionStatus(ActionType.Action, actionId) == 0;
    }

    /// <inheritdoc/>
    public uint GetActionStatusCode(uint actionId)
    {
        var actionManager = SafeGameAccess.GetActionManager(_errorMetrics);
        if (actionManager is null)
            return 0;

        return actionManager->GetActionStatus(ActionType.Action, actionId);
    }

    /// <inheritdoc/>
    public uint GetActionStatusCode(uint actionId, ulong targetId)
    {
        var actionManager = SafeGameAccess.GetActionManager(_errorMetrics);
        if (actionManager is null)
            return 0;

        // Passing the target evaluates facing / range / line-of-sight refusals (the target-less
        // overload reports 0 for those because it has no target to check against).
        return actionManager->GetActionStatus(ActionType.Action, actionId, targetId);
    }

    /// <summary>
    /// Maps the FFXIV GetActionStatus LogMessage codes we recognize to a human reason; null otherwise.
    /// Mirrors the scheduler's DescribeActionStatusCode so reject reasons read consistently.
    /// </summary>
    private static string? DescribeStatusReason(uint status) => status switch
    {
        565 => "not unlocked",
        566 => "not facing target",
        562 => "out of range",
        563 => "target not in line of sight",
        _ => null,
    };

    private static string NameOr(string name, string fallback)
        => string.IsNullOrEmpty(name) ? fallback : name;

    /// <summary>
    /// Surfaces a genuine "couldn't cast this action" event to the curated debug log. Skips pure
    /// out-of-range (562) — that's normal movement between packs, not a real cast failure.
    /// </summary>
    private void LogCastRefusal(string actionName, uint dispatchId, ulong targetId, bool submittedNotCast)
    {
        if (_debugLog is null)
            return;

        // A cast is in flight: the real GCD is casting fine, and this submit is just a duplicate re-probe of
        // it (the scheduler re-submitting Holy/Stone while it's already mid-cast). Not a refusal — don't log.
        if (_lastIsCasting)
            return;

        var status = targetId != 0 ? GetActionStatusCode(dispatchId, targetId) : 0u;
        // 562 = out of range (movement between packs, not a cast failure).
        // 582 = "not ready" — the GCD is already in flight / mid-cast and the scheduler re-probed the same
        // action (a duplicate-GCD probe, e.g. while a hard-cast is rolling), not a genuine refusal.
        if (status == 562 || status == 582)
            return;

        var label = DescribeStatusReason(status)
            ?? (status != 0 ? $"game status {status}" : "facing / line-of-sight / moving?");

        // Hard-target comparison: auto-face only turns toward the HARD target, so if the action's target
        // isn't the hard target the cast keeps failing facing. This tag tells the two stall causes apart.
        // Self-targeted actions (PBAoE like Vicepit/Steel Maw, self-buffs) dispatch at the player, so the
        // hard-target comparison is meaningless for them — never flag those as a mismatch.
        var selfId = _objectTable?.LocalPlayer?.GameObjectId ?? 0;
        string targetTag;
        if (targetId != 0 && targetId == selfId)
        {
            targetTag = " [self-targeted]";
        }
        else
        {
            var hardId = HardTargetIdProvider?.Invoke() ?? 0;
            targetTag = hardId == 0
                ? " [no hard target]"
                : hardId == targetId
                    ? " [hard target matches]"
                    : " [hard target MISMATCH — auto-face turns elsewhere]";
        }

        var targetName = targetId != 0 ? ResolveTargetInfo(targetId).name ?? "Target" : "Target";
        var verb = submittedNotCast ? "submitted but not cast" : "unable to cast";
        _debugLog.Log(Daedalus.Services.Debug.DebugLogCategory.Action,
            Daedalus.Services.Debug.DebugLogSeverity.Warning,
            $"{verb}: {NameOr(actionName, "Action")} -> {targetName} — {label}{targetTag}");
    }

    /// <summary>
    /// Invoke the face-the-target hook (hard-targets the enemy) when a GCD was refused for a reason that
    /// hard-targeting can fix. The game does NOT report a "not facing" status from GetActionStatus — facing
    /// is checked at UseAction time and surfaces as status <b>0</b> (the "los/facing/moving?" fallback), not
    /// 566. The root in AutoDuty is that the bot's combat target isn't the hard target, so AutoFaceTarget
    /// turns toward the (wrong/absent) hard target and the action at the real target fails facing. Syncing
    /// the hard target fixes it. Fire on 566 (explicit) AND 0 (the real case); skip 562/563 (range / LoS),
    /// which need movement, not facing.
    /// </summary>
    private void TryFaceRecovery(uint dispatchId, ulong targetId)
    {
        if (FaceTargetOnStuck is null || targetId == 0)
            return;
        var status = GetActionStatusCode(dispatchId, targetId);
        if (status == StatusNotFacing || status == 0)
            FaceTargetOnStuck(targetId);
    }

    private const uint ActionStatusNotLearned = 565;

    /// <inheritdoc/>
    public bool IsActionLearned(uint actionId)
    {
        var actionManager = SafeGameAccess.GetActionManager(_errorMetrics);
        if (actionManager is null)
            return false;

        var adjustedId = actionManager->GetAdjustedActionId(actionId);
        var status = actionManager->GetActionStatus(
            ActionType.Action,
            adjustedId,
            checkRecastActive: false,
            checkCastingActive: false);

        // A trait upgrade replaces the base on the bar (e.g. Howling Fist → Enlightenment at Lv74). When the
        // upgraded action is castable, the ability is genuinely available — don't report the base as
        // "missing" just because the base row's own UnlockLink reads unsatisfied; the castable upgrade
        // proves the chain is unlocked. (A truly locked ability returns status 565 here and falls through.)
        if (adjustedId != actionId && status != ActionStatusNotLearned)
            return true;

        return status != ActionStatusNotLearned
            && ActionUnlockHelper.IsActionQuestUnlocked(_dataManager, actionId);
    }

    /// <summary>
    /// Gets the number of available weave slots before the GCD is ready.
    /// </summary>
    public int GetAvailableWeaveSlots()
    {
        if (AnimationLockRemaining > FFXIVConstants.WeaveWindowBuffer || _lastIsCasting)
            return 0;

        // Each oGCD takes ~0.7s animation lock. Reserve the queue window at the tail of the GCD for the next
        // GCD's early submission so a weaved oGCD can never clip into it.
        // For short GCDs (Heat Blast 1.5s), the standard 0.5s queue reserve + 0.1s buffer leaves
        // no room for a 0.7s weave (0.9s - 0.6 = 0.3s). Drop the queue reserve entirely — the
        // game's built-in action queue handles the next Heat Blast submission, and a single weaved
        // oGCD's animation lock (~0.6s) finishes before the GCD rolls over.
        // Same for the post-hard-cast tail (casters): a 1.5s cast in a 2.4s GCD leaves ~0.9s — the queue
        // reserve ate all of it, so oGCDs could ONLY weave after instant GCDs. When instants dried up
        // (PCT with Holy/Comet locked), muses/Striking/Starry starved for entire fights. One ~0.6s weave
        // fits the tail without clipping, and the next GCD is queue-submitted regardless (RSR weaves here).
        var postCastTail = _castSeenThisCycle && !_lastIsCasting;
        var queueReserve = postCastTail || (_lastKnownGcdTotal > 0 && _lastKnownGcdTotal <= 1.6f)
            ? 0f
            : FFXIVTimings.QueueWindow;
        var availableTime = GcdRemaining - queueReserve - FFXIVConstants.WeaveWindowBuffer;
        var slots = (int)(availableTime / FFXIVTimings.AnimationLockBase);

        return Math.Max(0, Math.Min(2, slots)); // Max 2 weaves (double weave)
    }

    /// <summary>
    /// Debug info for display.
    /// </summary>
    public string GetDebugInfo()
    {
        var lastAction = _lastExecutedAction?.Name ?? "none";
        var timeSinceLast = (DateTime.UtcNow - _lastExecuteTime).TotalSeconds;

        return $"GCD: {CurrentGcdState} ({GcdRemaining:F2}s) | " +
               $"AnimLock: {AnimationLockRemaining:F2}s | " +
               $"Last: {lastAction} ({timeSinceLast:F1}s ago)";
    }

    /// <summary>
    /// Gets the current number of charges available for an action.
    /// For non-charge actions, returns 1 if ready, 0 if on cooldown.
    /// </summary>
    public uint GetCurrentCharges(uint actionId)
    {
        var actionManager = SafeGameAccess.GetActionManager(_errorMetrics);
        if (actionManager is null)
            return 0;

        var maxCharges = ActionManager.GetMaxCharges(actionId, 0);
        if (maxCharges <= 1)
        {
            // Single-charge abilities on their own recast group (Drill, Air Anchor, Chain Saw):
            // GetCurrentCharges may return 1 even on cooldown. Fall back to cooldown check.
            // Skip for global GCD abilities (recast group 57) — their cooldown = GCD remaining,
            // which would incorrectly report 0 charges during the normal GCD roll.
            var adjustedId = actionManager->GetAdjustedActionId(actionId);
            var recastGroup = actionManager->GetRecastGroup((int)ActionType.Action, adjustedId);
            if (recastGroup != 57)
            {
                var remaining = GetCooldownRemaining(actionId);
                return remaining <= 0f ? 1u : 0u;
            }
        }

        return actionManager->GetCurrentCharges(actionId);
    }

    /// <summary>
    /// Gets the maximum number of charges for an action at a given level.
    /// Pass level 0 to get max charges for current level.
    /// </summary>
    public ushort GetMaxCharges(uint actionId, uint level)
    {
        return ActionManager.GetMaxCharges(actionId, level);
    }

    private bool HasCompletedSubmittedRecast()
        => _gcdRecastSeenSinceSubmit
           && _peakRecastTotalSinceSubmit > 0f
           && _peakRecastElapsedSinceSubmit >= _peakRecastTotalSinceSubmit * MinRecastCompletionRatio;

    private bool IsChargeBasedGcdSubmitBlocked()
        => ChargeGcdSubmitGuard.ShouldBlock(_lastChargeBasedGcdSubmitUtc, _lastKnownGcdTotal, DateTime.UtcNow);

    private static bool IsChargeBasedGcd(uint baseActionId, ActionManager* actionManager)
    {
        var dispatchId = actionManager->GetAdjustedActionId(baseActionId);
        return ActionManager.GetMaxCharges(dispatchId, 0) > 1;
    }

    private void RecordChargeBasedGcdSubmit(uint baseActionId)
    {
        if (ActionManager.GetMaxCharges(baseActionId, 0) <= 1)
            return;

        _lastChargeBasedGcdSubmitUtc = DateTime.UtcNow;
    }

    private void RaiseActionExecuted(ActionDefinition action)
    {
        var handler = ActionExecuted;
        if (handler is null)
            return;

        handler(new ActionExecutedEvent(
            ActionId: action.ActionId,
            ActionName: action.Name,
            IsGcd: action.IsGCD,
            TimestampUtc: _lastExecuteTime));
    }
}
