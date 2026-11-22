using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Daedalus.Models.Action;
using Daedalus.Rotation.Base;
using Daedalus.Rotation.Common;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Services;
using Daedalus.Services.Action;
using Daedalus.Services.Debug;
using Daedalus.Timeline;

namespace Daedalus.Rotation.Transform;

/// <summary>
/// Presses the one action a deep-dungeon transformation leaves on the bar (<see cref="TransformPolicy"/>). Runs from
/// BaseRotation.ExecuteInternal before the job's modules; while the character is transformed they stand down, since
/// the game refuses every job action.
/// </summary>
public sealed class TransformActionLayer
{
    private readonly ActionService _actionService;
    private readonly RotationScheduler _scheduler;
    private readonly IPluginLog _log;
    private readonly Dictionary<uint, AbilityBehavior> _behaviorCache = [];
    private TransformForm _form;
    private string? _waitingOn;

    public TransformActionLayer(
        ActionService actionService,
        IJobGauges jobGauges,
        Configuration configuration,
        ITimelineService? timelineService,
        IErrorMetricsService? errorMetrics,
        IPluginLog log)
    {
        _actionService = actionService;
        _log = log;
        _scheduler = new RotationScheduler(actionService, jobGauges, configuration, timelineService, errorMetrics);
    }

    /// <summary>True while transformed: the caller then skips the job's modules.</summary>
    public bool Execute(IRotationContext ctx, bool isMoving, bool inCombat)
    {
        try
        {
            var form = CurrentForm(ctx.Player);
            var action = TransformPolicy.ActionFor(form);
            if (form != _form)
            {
                _form = form;
                _waitingOn = null;
                Note(form == TransformForm.None ? "over -- back to the job's rotation"
                    : action is { } known ? $"{form} -- pressing {known.Name}"
                    : $"{form} -- no action known for it, the job's rotation stands down");
            }

            if (form == TransformForm.None)
                return false;
            if (action is not { } act)
                return true;

            var enemy = ctx.TargetingService.GetUserEnemyTarget() ?? ctx.Player.TargetObject as IBattleChara;
            var alive = enemy is { IsDead: false };
            var toEnemy = alive ? enemy!.Position - ctx.Player.Position : default;
            toEnemy.Y = 0f;
            var facts = new TransformFacts(
                inCombat,
                alive,
                toEnemy.Length(),
                alive ? enemy!.HitboxRadius : 0f,
                isMoving,
                Ready: _actionService.IsActionReady(act.ActionId));
            if (TransformPolicy.Reject(act, facts) is { } why)
            {
                Waiting(act, why);
                return true;
            }

            var behavior = Behavior(act);
            _scheduler.Reset();
            switch (act.Aim)
            {
                case TransformAim.Enemy:
                    _scheduler.PushGcd(behavior, enemy!.GameObjectId, 0);
                    break;
                case TransformAim.Ground:
                    _scheduler.PushGroundTargetedOgcd(behavior, enemy!.Position, 0);
                    break;
                case TransformAim.Self:
                    _scheduler.PushGcd(behavior, ctx.Player.GameObjectId, 0);
                    break;
                case TransformAim.Forward:
                    // the dash runs its full length whatever is in the way: not off a ledge or into a telegraph
                    var ahead = toEnemy.LengthSquared() > 0.01f ? Vector3.Normalize(toEnemy) : Vector3.Zero;
                    var landing = ctx.Player.Position + ahead * act.Reach;
                    if (!TargetedDashGuard.IsTargetedDashSafe(
                            ctx.Player.Position, landing, act.Reach, RotationServices.VNav, RotationServices.BossModSafety))
                    {
                        Waiting(act, "the dash would land somewhere unsafe");
                        return true;
                    }

                    if (_actionService.CanExecuteGcd)
                        _actionService.FaceTarget(enemy!.GameObjectId);
                    _scheduler.PushGcd(behavior, ctx.Player.GameObjectId, 0);
                    break;
            }

            _waitingOn = null;
            if (behavior.Action.IsGCD)
            {
                if (_actionService.CanExecuteGcd)
                    _scheduler.DispatchGcd(ctx);
            }
            else if (_actionService.CanExecuteOgcd)
            {
                _scheduler.DispatchOgcd(ctx);
            }

            return true;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "TransformActionLayer: failed");
            return false;
        }
    }

    // Why the action is held, once per reason, so a transformation that never presses anything can be read back from
    // the debug log. The recast is every press's ordinary wait and is not worth a line.
    private void Waiting(in TransformAction act, string why)
    {
        if (why == _waitingOn)
            return;
        _waitingOn = why;
        if (why != "on recast")
            Note($"{act.Name} held -- {why}");
    }

    private static void Note(string message)
        => RotationServices.DebugLog?.Log(DebugLogCategory.Action, DebugLogSeverity.Info, $"[Transform] {message}");

    private static TransformForm CurrentForm(IPlayerCharacter player)
    {
        foreach (var status in player.StatusList)
        {
            if (TransformPolicy.IsTransformStatus(status.StatusId))
                return TransformPolicy.FormOf(status.Param);
        }

        return TransformForm.None;
    }

    private AbilityBehavior Behavior(in TransformAction act)
    {
        if (!_behaviorCache.TryGetValue(act.ActionId, out var behavior))
        {
            behavior = new AbilityBehavior
            {
                Action = new ActionDefinition
                {
                    ActionId = act.ActionId,
                    Name = act.Name,
                    MinLevel = 1,
                    // They share the global cooldown, though the sheet files most of them under "Special". The
                    // scheduler places ground targets for oGCDs only, so the one placed action goes that way.
                    Category = act.Aim == TransformAim.Ground ? ActionCategory.oGCD : ActionCategory.GCD,
                    TargetType = ActionTargetType.Self,
                    CastTime = act.CastTime,
                    RecastTime = 2.5f,
                    Range = act.Aim == TransformAim.Enemy ? act.Reach : 0f,
                },
                MechanicGate = true,
            };
            _behaviorCache[act.ActionId] = behavior;
        }

        return behavior;
    }
}
