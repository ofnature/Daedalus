using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Daedalus.Data;
using Daedalus.Rotation.Common;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Services;
using Daedalus.Services.Action;
using Daedalus.Services.Occult;
using Daedalus.Services.Party;
using Daedalus.Timeline;

namespace Daedalus.Rotation.Phantom;

/// <summary>
/// Variant dungeon duty-action executor (docs/variant-actions-plan.md Phase 2) —
/// sibling of <see cref="PhantomActionLayer"/> on the same BaseRotation pre/post hooks.
/// Every push is gated on the instance-granted Set status, the duty-bar slot (per-tier
/// action IDs resolve through the slot), cooldown and range. Inert outside the five
/// variant territories.
/// </summary>
public sealed class VariantActionLayer
{
    private const int PrioCure = 10;
    private const int PrioRaise = 20;
    private const int PrioRampart = 30;
    private const int PrioDartAndShot = 40;
    private const int PrioUltimatum = 50;

    private const ushort RaisePendingStatusId = 148;
    private const float RaiseRangeSquared = 900f; // 30y cast range
    private const int RaiseCastMs = 8000;
    private const float RangeBufferYalms = 0.5f;

    private readonly ActionService _actionService;
    private readonly Configuration _configuration;
    private readonly PhantomJobService _dutyState;
    private readonly IPartyCoordinationService? _partyCoordination;
    private readonly RotationScheduler _scheduler;
    private readonly IPluginLog _log;

    private readonly Dictionary<uint, AbilityBehavior> _behaviorCache = [];
    private readonly List<string> _pushRejects = [];
    private bool _dispatchedThisFrame;
    private bool _framePrepared;
    private bool _isMovingThisFrame;

    public VariantActionLayer(
        ActionService actionService,
        IJobGauges jobGauges,
        Configuration configuration,
        PhantomJobService dutyState,
        ITimelineService? timelineService,
        IErrorMetricsService? errorMetrics,
        IPluginLog log,
        IPartyCoordinationService? partyCoordination = null)
    {
        _actionService = actionService;
        _configuration = configuration;
        _dutyState = dutyState;
        _partyCoordination = partyCoordination;
        _log = log;
        _scheduler = new RotationScheduler(actionService, jobGauges, configuration, timelineService, errorMetrics);
    }

    /// <summary>Pre-modules: collect all bands; phantom GCDs (Cure, Raise) pre-empt the window.</summary>
    public void ExecutePreModules(IRotationContext ctx, bool isMoving, bool inCombat)
    {
        try
        {
            _framePrepared = false;
            PreModulesCore(ctx, isMoving, inCombat);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "VariantActionLayer: pre-modules failed");
        }
    }

    /// <summary>Post-modules: queued oGCDs into leftover weave slots (live capacity).</summary>
    public void ExecutePostModules(IRotationContext ctx, bool isMoving, bool inCombat)
    {
        try
        {
            if (!_framePrepared)
                return;

            if (_actionService.CanExecuteOgcd)
                _scheduler.DispatchOgcd(ctx);
            if (_actionService.CanExecuteGcd)
                _scheduler.DispatchGcd(ctx);

            var queued = _scheduler.InspectGcdQueue().Count + _scheduler.InspectOgcdQueue().Count;
            _dutyState.VariantLastEvent = _dispatchedThisFrame
                ? "dispatched"
                : queued > 0
                    ? $"waiting — {queued} queued, no free slot"
                    : _pushRejects.Count > 0
                        ? $"blocked — {_pushRejects[0]}"
                        : "idle — nothing eligible";
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "VariantActionLayer: post-modules failed");
        }
    }

    private void PreModulesCore(IRotationContext ctx, bool isMoving, bool inCombat)
    {
        var cfg = _configuration.Variant;
        if (!cfg.EnableVariantActions || !_dutyState.IsInVariantDungeon)
            return;

        var player = ctx.Player;
        var selfHpPct = player.MaxHp > 0 ? (float)player.CurrentHp / player.MaxHp : 1f;

        _scheduler.Reset();
        _dispatchedThisFrame = false;
        _isMovingThisFrame = isMoving;
        _pushRejects.Clear();
        _framePrepared = true;

        if (VariantBandRules.ShouldCure(cfg, selfHpPct))
            TryPush(ctx, VariantAction.Cure, PrioCure);

        PushRaise(ctx, cfg);
        PushRampart(ctx, cfg, inCombat);
        PushDamage(ctx, cfg, inCombat);
        PushUltimatum(ctx, cfg, inCombat);

        // GCD pre-empt: Cure/Raise claim the window ahead of the job's filler.
        if (_actionService.CanExecuteGcd)
            _scheduler.DispatchGcd(ctx);

        // oGCD pre-empt (field report: Spirit Dart starved behind the job's opener weaves
        // until the pack was nearly dead): variant weaves take the FIRST weave slot. They
        // are low-frequency (dart ~1/27s, Eagle Eye 1/60s, Rampart per buff cycle) and a
        // 2,040-potency DoT outranks any single job weave. Real recasts block the
        // post-pass from double-firing the same candidate.
        if (_actionService.CanExecuteOgcd)
            _scheduler.DispatchOgcd(ctx);
    }

    private void PushRaise(IRotationContext ctx, Config.VariantConfig cfg)
    {
        var (deadHealer, deadOther, livingHealer) = ScanParty(ctx);
        var decision = VariantBandRules.DecideRaise(cfg, deadHealer != null, deadOther != null, livingHealer);
        if (decision == VariantRaiseDecision.None)
            return;

        var target = decision == VariantRaiseDecision.RaiseHealer ? deadHealer! : deadOther!;
        var targetId = (uint)target.GameObjectId;

        // Shared raise buffer: never double-cast a corpse another toon (or a real healer
        // on another instance) is already raising. Rides LAN when enabled.
        if (_partyCoordination?.IsRaiseTargetReservedByOther(targetId) == true)
        {
            _pushRejects.Add("raise target reserved by another toon");
            return;
        }

        TryPush(ctx, VariantAction.Raise, PrioRaise, target.GameObjectId, target,
            onExtraDispatched: actionId =>
                _partyCoordination?.ReserveRaiseTarget(targetId, actionId, RaiseCastMs, usingSwiftcast: false));
    }

    private void PushRampart(IRotationContext ctx, Config.VariantConfig cfg, bool inCombat)
    {
        var buffActive = _actionService.PlayerHasStatus(VariantActionData.VulnerabilityDownStatusId);
        if (VariantBandRules.ShouldRampart(cfg, inCombat, buffActive))
            TryPush(ctx, VariantAction.Rampart, PrioRampart);
    }

    private void PushDamage(IRotationContext ctx, Config.VariantConfig cfg, bool inCombat)
    {
        if (!inCombat)
            return;

        var target = ctx.TargetingService.GetUserEnemyTarget() ?? ctx.Player.TargetObject as IBattleChara;
        if (target is null || target.IsDead)
            return;

        // Spirit Dart: DoT maintenance against OUR Sustained Damage on the target
        // (per-source — another toon's dart never suppresses ours).
        if (VariantBandRules.ShouldMaintainDart(cfg, GetOwnDotRemaining(ctx, target)))
            TryPush(ctx, VariantAction.SpiritDart, PrioDartAndShot, target.GameObjectId, target);

        if (cfg.UseEagleEyeShot)
            TryPush(ctx, VariantAction.EagleEyeShot, PrioDartAndShot + 1, target.GameObjectId, target);
    }

    private void PushUltimatum(IRotationContext ctx, Config.VariantConfig cfg, bool inCombat)
    {
        if (cfg.UseUltimatum && inCombat)
            TryPush(ctx, VariantAction.Ultimatum, PrioUltimatum);
    }

    private float GetOwnDotRemaining(IRotationContext ctx, IBattleChara target)
    {
        if (target.StatusList == null)
            return 0f;

        foreach (var status in target.StatusList)
        {
            if (status != null
                && status.StatusId == VariantActionData.SustainedDamageStatusId
                && status.SourceId == ctx.Player.GameObjectId)
                return status.RemainingTime;
        }

        return 0f;
    }

    /// <summary>
    /// Dead-member triage + living-healer presence for the raise policy. Skips corpses
    /// with a pending Raise (status 148) and anything out of the 30y cast range.
    /// </summary>
    private (IBattleChara? DeadHealer, IBattleChara? DeadOther, bool LivingHealerPresent) ScanParty(IRotationContext ctx)
    {
        IBattleChara? deadHealer = null;
        IBattleChara? deadOther = null;
        var livingHealer = false;

        foreach (var member in ctx.PartyList)
        {
            if (member?.GameObject is not IBattleChara chara || chara.GameObjectId == ctx.Player.GameObjectId)
                continue;

            var jobId = TrustPartyRoleHelper.ResolveJobId(chara, ctx.PartyList);
            var isHealer = JobRegistry.IsHealer(jobId);

            if (!chara.IsDead)
            {
                if (isHealer)
                    livingHealer = true;
                continue;
            }

            if (HasStatus(chara, RaisePendingStatusId))
                continue;
            if (System.Numerics.Vector3.DistanceSquared(ctx.Player.Position, chara.Position) > RaiseRangeSquared)
                continue;

            if (isHealer)
                deadHealer ??= chara;
            else
                deadOther ??= chara;
        }

        return (deadHealer, deadOther, livingHealer);
    }

    private static bool HasStatus(IBattleChara chara, uint statusId)
    {
        if (chara.StatusList == null)
            return false;

        foreach (var status in chara.StatusList)
        {
            if (status != null && status.StatusId == statusId)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gates: Set status granted → tier action ID resolved through the duty-bar slot →
    /// cooldown → cast-while-moving → range. Target 0 = self.
    /// </summary>
    private void TryPush(IRotationContext ctx, VariantAction kind, int priority,
        ulong targetId = 0, IBattleChara? rangeTarget = null, Action<uint>? onExtraDispatched = null)
    {
        var def = VariantActionData.Get(kind);

        if (!_actionService.PlayerHasStatus(def.SetStatusId))
        {
            // Not one of this run's two picks — silent (not a fixable blocker).
            return;
        }

        var actionId = ResolveSlottedId(def);
        if (actionId == 0)
        {
            _pushRejects.Add($"{def.Name} not on duty bar");
            return;
        }

        if (!_actionService.IsActionReady(actionId))
        {
            _pushRejects.Add($"{def.Name} on cooldown");
            return;
        }

        if (!_behaviorCache.TryGetValue(actionId, out var behavior))
        {
            behavior = new AbilityBehavior { Action = _dutyState.GetOrBuildDefinition(actionId, def.Name) };
            _behaviorCache[actionId] = behavior;
        }

        if (behavior.Action.CastTime > 0 && _isMovingThisFrame)
        {
            _pushRejects.Add($"{def.Name} needs a hard cast (moving)");
            return;
        }

        if (rangeTarget is not null && behavior.Action.Range > 0)
        {
            var dist = System.Numerics.Vector3.Distance(ctx.Player.Position, rangeTarget.Position)
                       - rangeTarget.HitboxRadius;
            if (dist > behavior.Action.Range + RangeBufferYalms)
            {
                _pushRejects.Add($"{def.Name} out of range");
                return;
            }
        }

        var name = def.Name;
        Action<IRotationContext> onDispatched = _ =>
        {
            _dispatchedThisFrame = true;
            _dutyState.VariantLastDispatch = $"{DateTime.Now:HH:mm:ss} {name}";
            onExtraDispatched?.Invoke(actionId);
        };

        if (behavior.Action.IsGCD)
            _scheduler.PushGcd(behavior, targetId, priority, onDispatched);
        else
            _scheduler.PushOgcd(behavior, targetId, priority, onDispatched);
    }

    /// <summary>Which of the per-tier action IDs is on the duty bar (morph-aware), or 0.</summary>
    private uint ResolveSlottedId(VariantActionDef def)
    {
        var slots = _dutyState.GetDutySlotIds();
        foreach (var candidate in def.ActionIds)
        {
            foreach (var slotId in slots)
            {
                if (slotId == 0)
                    continue;
                if (slotId == candidate || _actionService.GetAdjustedActionId(slotId) == candidate)
                    return candidate;
            }
        }

        return 0;
    }
}
