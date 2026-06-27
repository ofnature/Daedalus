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

    // Last executed action (for debugging)
    private ActionDefinition? _lastExecutedAction;
    private DateTime _lastExecuteTime;

    // Minimal action history (last GCD / last oGCD id) for oGCD sequencing consumers.
    private readonly ActionHistory _history = new();

    // Track oGCD usage per GCD cycle (allows up to 2 weaves)
    private int _ogcdsUsedThisCycle;
    private float _prevGcdRemaining;

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
        IDataManager? dataManager = null)
    {
        _actionTracker = actionTracker;
        _errorMetrics = errorMetrics;
        _objectTable = objectTable;
        _dataManager = dataManager;
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

        // Stale guard: submit accepted but recast group 57 never activated.
        if (_gcdSubmittedThisCycle && !_gcdRecastSeenSinceSubmit
            && secondsSinceSubmit > UncommittedSubmitStaleSeconds)
        {
            _gcdSubmittedThisCycle = false;
            _nextGcdAttemptAllowed = DateTime.UtcNow.AddSeconds(FailedSubmitBackoffSeconds);
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
        // and blocks all oGCD weaving during Overheat.
        if (GcdRemaining > _prevGcdRemaining + 0.3f)
            _ogcdsUsedThisCycle = 0;
        _prevGcdRemaining = GcdRemaining;

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
            _gcdSubmittedThisCycle = false;
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

        var actionManager = SafeGameAccess.GetActionManager(_errorMetrics);
        if (actionManager is null)
            return false;

        // If we already submitted a GCD for this cycle, don't spam UseAction every frame.
        if (_gcdSubmittedThisCycle)
            return false;

        var dispatchId = actionManager->GetAdjustedActionId(action.ActionId);
        if (ShouldBlockRepeatGcd(dispatchId, actionManager))
            return false;

        if (IsChargeBasedGcd(action.ActionId, actionManager) && IsChargeBasedGcdSubmitBlocked())
            return false;

        if (DateTime.UtcNow < _nextGcdAttemptAllowed)
            return false;

        // Do NOT pre-check GetActionStatus here: while the global GCD is rolling it returns 583 ("not ready"),
        // but UseAction still accepts the call in the last ~0.5s and queues the action to fire on rollover.
        // Pre-checking defeats the server-side action queue, so we delegate the "can fire now?" decision to UseAction.
        var result = actionManager->UseAction(ActionType.Action, dispatchId, targetId);

        if (result)
        {
            _gcdSubmittedThisCycle = true;
            _blockedRepeatGcdDispatchId = dispatchId;
            RecordChargeBasedGcdSubmit(dispatchId);
            _gcdRecastSeenSinceSubmit = false;
            _peakRecastElapsedSinceSubmit = 0;
            _peakRecastTotalSinceSubmit = 0;

            _lastExecutedAction = action;
            _lastExecuteTime = DateTime.UtcNow;
            _history.RecordGcd(action.ActionId);

            // Track for statistics
            var gcdDuration = actionManager->GetRecastTime(ActionType.Action, dispatchId);
            _actionTracker.LogGcdCast(gcdDuration);
            var (tName, tHp) = ResolveTargetInfo(targetId);
            _actionTracker.LogAttempt(action.ActionId, tName, tHp, ActionResult.Success, 0);
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

            var gcdDuration = actionManager->GetRecastTime(ActionType.Action, rawDispatchId);
            _actionTracker.LogGcdCast(gcdDuration);
            var (rName, rHp) = ResolveTargetInfo(targetId);
            _actionTracker.LogAttempt(action.ActionId, rName, rHp, ActionResult.Success, 0);
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

    private (string? name, uint? hp) ResolveTargetInfo(ulong targetId)
    {
        if (targetId == 0 || _objectTable == null)
            return (null, null);
        var obj = _objectTable.SearchById(targetId);
        if (obj is IBattleChara bc)
            return (bc.Name?.TextValue, bc.CurrentHp);
        return (obj?.Name?.TextValue, null);
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
        var queueReserve = _lastKnownGcdTotal > 0 && _lastKnownGcdTotal <= 1.6f
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
