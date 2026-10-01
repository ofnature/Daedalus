using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using Daedalus.Rotation.Common;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Services;
using Daedalus.Services.Action;
using Daedalus.Services.Occult;
using Daedalus.Timeline;

namespace Daedalus.Rotation.Roleplay;

/// <summary>
/// Presses what the boss engine says the character you are playing should use (<see cref="RoleplayActionPolicy"/>).
/// Runs from BaseRotation.ExecuteInternal before the job's modules; when it takes the frame over they stand down.
/// Definitions come from the game's Action sheet, as the duty-action layers build theirs, so no role-play action is
/// hand-maintained here.
/// </summary>
public sealed class RoleplayActionLayer
{
    private readonly ActionService _actionService;
    private readonly PhantomJobService _definitions;
    private readonly Func<bool> _enabled;
    private readonly Func<RoleplayRequest[]> _requests;
    private readonly RotationScheduler _scheduler;
    private readonly IPluginLog _log;
    private readonly Dictionary<uint, AbilityBehavior> _behaviorCache = [];
    private readonly List<string> _rejects = [];
    private bool _wasActive;

    /// <summary>A kit is being played: set by the last <see cref="Execute"/>. The job's movement reads it
    /// (<see cref="RoleplayActionPolicy.JobMovementAllowed"/>); it runs earlier in the frame, so it sees the previous one.</summary>
    public bool Active => _wasActive;

    /// <summary>The last frame's outcome, for the debug readout.</summary>
    public string LastEvent { get; private set; } = "idle";

    public RoleplayActionLayer(
        ActionService actionService,
        IJobGauges jobGauges,
        Configuration configuration,
        PhantomJobService definitions,
        ITimelineService? timelineService,
        IErrorMetricsService? errorMetrics,
        IPluginLog log,
        Func<bool> enabled,
        Func<RoleplayRequest[]> requests)
    {
        _actionService = actionService;
        _definitions = definitions;
        _enabled = enabled;
        _requests = requests;
        _log = log;
        _scheduler = new RotationScheduler(actionService, jobGauges, configuration, timelineService, errorMetrics);
    }

    /// <summary>True when this frame is the kit's: the caller then skips the job's modules.</summary>
    public bool Execute(IRotationContext ctx, bool isMoving, bool inCombat)
    {
        try
        {
            var enabled = _enabled();
            var requests = enabled ? _requests() : [];
            var active = RoleplayActionPolicy.TakesOver(enabled, inCombat, requests.Length);
            if (active != _wasActive)
                _log.Info(active
                    ? "Roleplay: playing the kit the boss engine names (first: {0})."
                    : "Roleplay: kit released.", requests.Length > 0 ? requests[0].ActionId : 0u);
            _wasActive = active;
            if (!active)
            {
                LastEvent = "idle";
                return false;
            }

            _scheduler.Reset();
            _rejects.Clear();
            var queued = 0;
            for (var i = 0; i < requests.Length; ++i)
            {
                var request = requests[i];
                var behavior = Behavior(request.ActionId);
                var facts = new RoleplayActionFacts(
                    behavior.Action.IsGCD,
                    behavior.Action.CastTime,
                    Ready: _actionService.IsActionReady(request.ActionId));
                if (RoleplayActionPolicy.Reject(request, facts, isMoving) is { } why)
                {
                    _rejects.Add($"{request.ActionId} {why}");
                    continue;
                }

                var priority = RoleplayActionPolicy.SchedulerPriority(i);
                if (request.GroundTargeted)
                    _scheduler.PushGroundTargetedOgcd(behavior, request.TargetPos, priority);
                else if (behavior.Action.IsGCD)
                    _scheduler.PushGcd(behavior, request.TargetId, priority);
                else
                    _scheduler.PushOgcd(behavior, request.TargetId, priority);
                ++queued;
            }

            var dispatched = false;
            if (_actionService.CanExecuteOgcd)
                dispatched |= _scheduler.DispatchOgcd(ctx).Dispatched;
            if (_actionService.CanExecuteGcd)
                dispatched |= _scheduler.DispatchGcd(ctx).Dispatched;

            LastEvent = dispatched ? "dispatched"
                : queued > 0 ? $"waiting -- {queued} queued"
                : _rejects.Count > 0 ? $"blocked -- {_rejects[0]}"
                : "nothing eligible";
            return true;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "RoleplayActionLayer: failed");
            return false;
        }
    }

    private AbilityBehavior Behavior(uint actionId)
    {
        if (!_behaviorCache.TryGetValue(actionId, out var behavior))
        {
            behavior = new AbilityBehavior { Action = _definitions.GetOrBuildDefinition(actionId, $"Roleplay {actionId}"), MechanicGate = true };
            _behaviorCache[actionId] = behavior;
        }
        return behavior;
    }
}
