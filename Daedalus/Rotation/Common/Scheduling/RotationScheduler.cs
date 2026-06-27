using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Plugin.Services;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Services.Action;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Services;
using Daedalus.Timeline;

namespace Daedalus.Rotation.Common.Scheduling;

/// <summary>
/// Per-rotation, per-frame priority scheduler. Modules push candidates into
/// the GCD and oGCD queues during <c>CollectCandidates</c>; the scheduler
/// dispatches the best-fit candidate per queue in priority order.
/// </summary>
public sealed class RotationScheduler
{
    private readonly IActionService _actionService;
    private readonly IJobGauges _jobGauges;
    private readonly Configuration _configuration;
    private readonly ITimelineService? _timelineService;
    private readonly IErrorMetricsService? _errorMetrics;

    private readonly List<AbilityCandidate> _gcdQueue = new(capacity: 32);
    private readonly List<AbilityCandidate> _ogcdQueue = new(capacity: 32);
    private readonly List<string> _lastFailReasons = new(capacity: 16);
    private int _insertionCounter;

    public RotationScheduler(
        IActionService actionService,
        IJobGauges jobGauges,
        Configuration configuration,
        ITimelineService? timelineService = null,
        IErrorMetricsService? errorMetrics = null)
    {
        _actionService = actionService;
        _jobGauges = jobGauges;
        _configuration = configuration;
        _timelineService = timelineService;
        _errorMetrics = errorMetrics;
    }

    public void Reset()
    {
        _gcdQueue.Clear();
        _ogcdQueue.Clear();
        _lastFailReasons.Clear();
        _insertionCounter = 0;
    }

    public void PushGcd(AbilityBehavior behavior, ulong targetId, int priority,
                        Action<IRotationContext>? onDispatched = null)
    {
        // Duplicate GCD guard: during one scheduler cycle, keep only one
        // candidate per action id to avoid queue flooding (e.g. repeated V3 pushes).
        foreach (var existing in _gcdQueue)
        {
            if (existing.Behavior.Action.ActionId == behavior.Action.ActionId)
                return;
        }

        _gcdQueue.Add(new AbilityCandidate
        {
            Behavior = behavior,
            TargetId = targetId,
            Priority = priority,
            InsertionOrder = _insertionCounter++,
            OnDispatched = onDispatched,
        });
    }

    public void PushOgcd(AbilityBehavior behavior, ulong targetId, int priority,
                         Action<IRotationContext>? onDispatched = null)
        => _ogcdQueue.Add(new AbilityCandidate
        {
            Behavior = behavior,
            TargetId = targetId,
            Priority = priority,
            InsertionOrder = _insertionCounter++,
            OnDispatched = onDispatched,
        });

    /// <summary>
    /// Push a ground-targeted oGCD candidate. Dispatch routes through
    /// <c>ExecuteGroundTargetedOgcd(action, position)</c>. Used for healer
    /// abilities like Asylum, Liturgy of the Bell, and Earthly Star.
    /// </summary>
    public void PushGroundTargetedOgcd(AbilityBehavior behavior, Vector3 position, int priority,
                                       Action<IRotationContext>? onDispatched = null)
        => _ogcdQueue.Add(new AbilityCandidate
        {
            Behavior = behavior,
            TargetId = 0,
            GroundPosition = position,
            Priority = priority,
            InsertionOrder = _insertionCounter++,
            OnDispatched = onDispatched,
        });

    public SchedulerDispatchResult DispatchGcd(IRotationContext ctx)
        => Dispatch(_gcdQueue, ctx, isOgcd: false, maxPriority: null);

    public SchedulerDispatchResult DispatchOgcd(IRotationContext ctx)
        => Dispatch(_ogcdQueue, ctx, isOgcd: true, maxPriority: null);

    /// <summary>
    /// Dispatches the highest-priority oGCD candidate at or below <paramref name="maxPriority"/>.
    /// Used for pre-pull Kardia (priority 0) while out of combat.
    /// </summary>
    public SchedulerDispatchResult DispatchOgcd(IRotationContext ctx, int maxPriority)
        => Dispatch(_ogcdQueue, ctx, isOgcd: true, maxPriority: maxPriority);

    /// <summary>Test-only inspection of the GCD queue contents.</summary>
    internal IReadOnlyList<AbilityCandidate> InspectGcdQueue() => _gcdQueue;

    /// <summary>Test-only inspection of the oGCD queue contents.</summary>
    internal IReadOnlyList<AbilityCandidate> InspectOgcdQueue() => _ogcdQueue;

    private SchedulerDispatchResult Dispatch(List<AbilityCandidate> queue, IRotationContext ctx, bool isOgcd, int? maxPriority)
    {
        _lastFailReasons.Clear();
        if (queue.Count == 0)
            return SchedulerDispatchResult.Empty;

        queue.Sort(static (a, b) =>
        {
            var cmp = a.Priority.CompareTo(b.Priority);
            return cmp != 0 ? cmp : a.InsertionOrder.CompareTo(b.InsertionOrder);
        });

        foreach (var candidate in queue)
        {
            if (maxPriority is int cap && candidate.Priority > cap)
                continue;
            var effective = ResolveLevelReplacement(candidate.Behavior, ctx.Player.Level);

            // Gate: level
            if (ctx.Player.Level < effective.MinLevel)
            {
                RecordFail(candidate, $"Level<{effective.MinLevel}");
                continue;
            }

            // Gate: job quest / unlock (status 565)
            if (!_actionService.IsActionLearned(effective.ActionId))
            {
                RecordFail(candidate, "NotLearned");
                continue;
            }

            // Gate: toggle
            if (candidate.Behavior.Toggle is { } toggle && !toggle(_configuration))
            {
                RecordFail(candidate, "Toggle");
                continue;
            }

            // Gate: proc buff
            if (candidate.Behavior.ProcBuff is { } procId && !_actionService.PlayerHasStatus(procId))
            {
                RecordFail(candidate, $"ProcBuff {procId}");
                continue;
            }

            // Gate: combo step (typed gauge predicate)
            if (candidate.Behavior.ComboStep is { } comboStep)
            {
                bool stepOk;
                try { stepOk = comboStep(_jobGauges); }
                catch (Exception ex)
                {
                    _errorMetrics?.RecordError("Scheduler", $"ComboStep threw: {ex.Message}");
                    RecordFail(candidate, "ComboStep threw");
                    continue;
                }
                if (!stepOk)
                {
                    RecordFail(candidate, "ComboStep");
                    continue;
                }
            }

            // Gate: adjusted action probe
            if (candidate.Behavior.AdjustedActionProbe is { } probeId)
            {
                var adjusted = _actionService.GetAdjustedActionId(probeId);
                if (adjusted != effective.ActionId)
                {
                    RecordFail(candidate, $"AdjustedActionProbe (expected {effective.ActionId}, got {adjusted})");
                    continue;
                }
            }

            // Gate: target. Skipped when TargetId == 0 (self-targeted, ground-targeted, or intentional).
            if (candidate.TargetId != 0 && ctx.ObjectTable is { } objectTable)
            {
                var target = objectTable.SearchById(candidate.TargetId);
                if (target is null)
                {
                    RecordFail(candidate, "Target missing");
                    continue;
                }
            }

            // Gate: cooldown / charges
            //
            // ChargeSource: pre-check charges. Only failing when actually out of charges
            // (GetCurrentCharges is stable across the global GCD roll for charge-based actions).
            //
            // Non-charge oGCDs: pre-check via IsActionReady. oGCDs have their own cooldown
            // groups separate from the global GCD (group 57), so GetCurrentCharges reflects
            // only the oGCD's own state.
            //
            // Non-charge GCDs: skip the pre-check. For plain GCDs on the global recast group,
            // GetCurrentCharges returns 0 during the GCD roll — that would incorrectly reject
            // valid queue-window dispatches (the server-side action queue accepts UseAction
            // in the last ~0.5s of the GCD and fires the action on rollover). ExecuteGcd
            // delegates to UseAction which handles the queue window correctly; if the GCD
            // is on its own independent cooldown (Sonic Break, Gnashing Fang, etc.) UseAction
            // returns false and we fall through to DispatchRejected at the bottom of the loop.
            if (candidate.Behavior.ChargeHold is { } hold)
            {
                // P5 charge-hold: reserve charges for the burst window so a multi-charge oGCD
                // never dumps its last charge off-burst (RSR usedUp semantics).
                var holdChargeId = candidate.Behavior.ChargeSource ?? effective.ActionId;
                var charges = _actionService.GetCurrentCharges(holdChargeId);
                if (charges == 0)
                {
                    RecordFail(candidate, $"Cooldown (no charges on {holdChargeId})");
                    continue;
                }

                bool inBurst;
                try { inBurst = hold.InBurst(ctx); }
                catch (Exception ex)
                {
                    _errorMetrics?.RecordError("Scheduler", $"ChargeHold.InBurst threw: {ex.Message}");
                    RecordFail(candidate, "ChargeHold threw");
                    continue;
                }

                if (!inBurst && charges <= hold.HoldCharges)
                {
                    RecordFail(candidate, $"ChargeHold (reserve {hold.HoldCharges} for burst)");
                    continue;
                }
            }
            else if (candidate.Behavior.ChargeSource is { } chargeId)
            {
                if (_actionService.GetCurrentCharges(chargeId) == 0)
                {
                    RecordFail(candidate, $"Cooldown (no charges on {chargeId})");
                    continue;
                }
            }
            else if (isOgcd && !_actionService.IsActionReady(effective.ActionId))
            {
                var remaining = _actionService.GetCooldownRemaining(effective.ActionId);
                RecordFail(candidate, $"Cooldown {remaining:F1}s");
                continue;
            }

            // Gate: mechanic
            if (candidate.Behavior.MechanicGate && effective.CastTime > 0f && _timelineService is not null)
            {
                if (MechanicCastGate.ShouldBlock(ctx, effective.CastTime))
                {
                    RecordFail(candidate, "Mechanic");
                    continue;
                }
            }

            // Gate: action-manager status (RSR ActionManagerStatusValid parity).
            // GCD path only. Skipped during the queue window: GetActionStatus returns 583 while
            // the global GCD rolls, but UseAction still accepts queue submission (see ExecuteGcd).
            // Skipped for ReplacementBaseId / ExecuteGcdRaw paths that intentionally bypass status.
            if (!isOgcd && candidate.Behavior.ReplacementBaseId is null)
            {
                var gcdRemaining = _actionService.GcdRemaining;
                var inQueueWindow = gcdRemaining > 0f && gcdRemaining <= FFXIVTimings.QueueWindow;
                if (!inQueueWindow)
                {
                    var adjustedId = _actionService.GetAdjustedActionId(effective.ActionId);
                    if (!_actionService.CanExecuteActionId(adjustedId))
                    {
                        RecordFail(candidate, "ActionStatus");
                        continue;
                    }
                }
            }

            bool dispatched;
            if (candidate.GroundPosition is { } position)
            {
                // Ground-targeted dispatch (oGCD only). Asylum, Liturgy of the Bell,
                // Earthly Star, Sacred Soil, etc.
                dispatched = _actionService.ExecuteGroundTargetedOgcd(effective, position);
            }
            else if (candidate.Behavior.ReplacementBaseId is { } rawId)
            {
                dispatched = isOgcd
                    ? _actionService.ExecuteOgcdRaw(effective, rawId, candidate.TargetId)
                    : _actionService.ExecuteGcdRaw(effective, rawId, candidate.TargetId);
            }
            else
            {
                dispatched = isOgcd
                    ? _actionService.ExecuteOgcd(effective, candidate.TargetId)
                    : _actionService.ExecuteGcd(effective, candidate.TargetId);
            }

            if (dispatched)
            {
                candidate.OnDispatched?.Invoke(ctx);
                return new SchedulerDispatchResult
                {
                    Dispatched = true,
                    Winner = candidate.Behavior,
                    GateFailReasons = _lastFailReasons.ToArray(),
                };
            }

            RecordFail(candidate, DescribeReject(candidate, effective, ctx));
        }

        return new SchedulerDispatchResult
        {
            Dispatched = false,
            Winner = null,
            GateFailReasons = _lastFailReasons.ToArray(),
        };
    }

    private void RecordFail(in AbilityCandidate candidate, string reason)
    {
        if (_lastFailReasons.Count >= 16) return;
        _lastFailReasons.Add($"{candidate.Behavior.Action.Name}: {reason}");
    }

    /// <summary>
    /// Explains why the game's UseAction refused a dispatch (the generic "DispatchRejected"). Checks
    /// range first (computable), then the raw GetActionStatus code; if the action is in range and the
    /// status is clear, the rejection is a transient execution-time refusal (line of sight / facing /
    /// movement) — common while AutoDuty runs the character around corners.
    /// </summary>
    private string DescribeReject(in AbilityCandidate candidate, ActionDefinition effective, IRotationContext ctx)
    {
        if (candidate.TargetId != 0
            && ctx.ObjectTable.SearchById(candidate.TargetId) is { } target
            && effective.Range > 0f)
        {
            var dist = Vector3.Distance(ctx.Player.Position, target.Position)
                       - target.HitboxRadius - ctx.Player.HitboxRadius;
            if (dist > effective.Range + 0.5f)
                return $"out of range ({dist:F0}y > {effective.Range:F0}y)";
        }

        var status = _actionService.GetActionStatusCode(_actionService.GetAdjustedActionId(effective.ActionId));
        if (status == 565)
            return "not unlocked";
        if (status != 0)
            return $"rejected (game status {status})";

        return "rejected (line-of-sight / facing / moving?)";
    }

    private ActionDefinition ResolveLevelReplacement(AbilityBehavior behavior, byte playerLevel)
    {
        if (behavior.LevelReplacements is null)
            return behavior.Action;

        var current = behavior.Action;
        byte bestLevel = 0;
        foreach (var (level, replacement) in behavior.LevelReplacements)
        {
            if (playerLevel >= level && level >= bestLevel
                && ActionAvailability.MeetsLevelAndLearned(playerLevel, _actionService, replacement))
            {
                current = replacement;
                bestLevel = level;
            }
        }

        return current;
    }
}
