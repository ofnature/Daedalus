using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Daedalus.Data;
using Daedalus.Rotation.Common;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Services;
using Daedalus.Services.Action;
using Daedalus.Services.Occult;
using Daedalus.Timeline;

namespace Daedalus.Rotation.Phantom;

/// <summary>
/// Occult Crescent phantom duty-action executor (Phase 3 of docs/occult-phantom-plan.md:
/// survival / mitigation / interrupt / MP / party-buff bands — no damage band yet).
///
/// One shared instance runs from BaseRotation.ExecuteInternal AFTER the job's own
/// modules, so it only ever consumes leftover GCD/weave capacity — the main rotation
/// always outranks it. Its own scheduler enforces the standard dispatch gates; every
/// push is additionally gated on phantom level, duty-bar slot presence (fail closed)
/// and the rotation-lockout status list (never stomp a burst/combo window).
/// </summary>
public sealed class PhantomActionLayer
{
    // Ascending scheduler priorities within the layer's own queues.
    private const int PrioEmergencySustain = 10;
    private const int PrioSelfMit = 20;
    private const int PrioInterrupt = 30;
    private const int PrioMpRestore = 40;
    private const int PrioPartyBuff = 50;

    private const float InterruptMeleeRangeYalms = 5f;

    private readonly ActionService _actionService;
    private readonly Configuration _configuration;
    private readonly PhantomJobService _phantomJobs;
    private readonly RotationScheduler _scheduler;
    private readonly IPluginLog _log;

    private readonly Dictionary<uint, AbilityBehavior> _behaviorCache = [];

    public PhantomActionLayer(
        ActionService actionService,
        IJobGauges jobGauges,
        Configuration configuration,
        PhantomJobService phantomJobs,
        ITimelineService? timelineService,
        IErrorMetricsService? errorMetrics,
        IPluginLog log)
    {
        _actionService = actionService;
        _configuration = configuration;
        _phantomJobs = phantomJobs;
        _log = log;
        _scheduler = new RotationScheduler(actionService, jobGauges, configuration, timelineService, errorMetrics);
    }

    /// <summary>Runs once per frame after the job modules. Never throws.</summary>
    public void Execute(IRotationContext ctx, bool isMoving, bool inCombat)
    {
        try
        {
            ExecuteCore(ctx, isMoving, inCombat);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "PhantomActionLayer: execute failed");
        }
    }

    private void ExecuteCore(IRotationContext ctx, bool isMoving, bool inCombat)
    {
        var cfg = _configuration.Occult;
        if (!cfg.EnablePhantomActions || !_phantomJobs.IsInOccultCrescent)
            return;

        var (job, level) = _phantomJobs.GetActiveJob();
        if (job == PhantomJob.None || level == 0)
        {
            _phantomJobs.LayerLastEvent = "held — no phantom job";
            return;
        }

        if (HasLockoutStatus() && inCombat)
        {
            _phantomJobs.LayerLastEvent = "held — rotation lockout status";
            return;
        }

        var player = ctx.Player;
        var selfHpPct = player.MaxHp > 0 ? (float)player.CurrentHp / player.MaxHp : 1f;

        _scheduler.Reset();

        PushSurvival(ctx, cfg, job, level, selfHpPct, inCombat);
        PushSelfMit(ctx, job, level, selfHpPct, inCombat);
        PushInterrupts(ctx, job, level, inCombat);
        PushMpRestore(ctx, cfg, job, level, inCombat);
        PushPartyBuffs(ctx, job, level, inCombat);

        // Only leftover capacity: the job's modules dispatched first this frame.
        if (ctx.CanExecuteOgcd)
            _scheduler.DispatchOgcd(ctx);
        if (ctx.CanExecuteGcd && !isMoving)
            _scheduler.DispatchGcd(ctx);
    }

    private void PushSurvival(IRotationContext ctx, Config.PhantomConfig cfg, PhantomJob job, byte level, float selfHpPct, bool inCombat)
    {
        var potionCount = _phantomJobs.GetItemCount(PhantomJobData.OccultPotionItemId);

        if (job == PhantomJob.Chemist)
        {
            if (PhantomBandRules.ShouldUsePotion(cfg, selfHpPct, potionCount, inCombat))
                TryPush(ctx, 41631, job, level, PrioEmergencySustain);

            var elixirCount = _phantomJobs.GetItemCount(PhantomJobData.OccultElixirItemId);
            if (PhantomBandRules.ShouldUseElixir(cfg, selfHpPct, elixirCount, inCombat))
                TryPush(ctx, 41635, job, level, PrioEmergencySustain + 1);
        }

        if (job == PhantomJob.Monk && PhantomBandRules.ShouldUseChakraForHp(cfg, selfHpPct, inCombat))
            TryPush(ctx, 41598, job, level, PrioEmergencySustain + 2);

        if (job == PhantomJob.Freelancer && PhantomBandRules.ShouldResuscitate(cfg, selfHpPct))
            TryPush(ctx, 41650, job, level, PrioEmergencySustain);

        if (job == PhantomJob.Knight && PhantomBandRules.ShouldPray(cfg, selfHpPct))
            TryPush(ctx, 41589, job, level, PrioEmergencySustain + 3);
    }

    private void PushSelfMit(IRotationContext ctx, PhantomJob job, byte level, float selfHpPct, bool inCombat)
    {
        if (!PhantomBandRules.ShouldSelfMit(selfHpPct, inCombat))
            return;

        if (job == PhantomJob.Knight)
            TryPush(ctx, 41588, job, level, PrioSelfMit); // Phantom Guard
        if (job == PhantomJob.Gladiator)
            TryPush(ctx, 46595, job, level, PrioSelfMit); // Defend
    }

    private void PushInterrupts(IRotationContext ctx, PhantomJob job, byte level, bool inCombat)
    {
        if (!inCombat)
            return;
        if (ctx.Player.TargetObject is not IBattleChara target || target.IsDead)
            return;

        var distance = System.Numerics.Vector3.Distance(ctx.Player.Position, target.Position)
                       - target.HitboxRadius;

        if (job == PhantomJob.Samurai
            && PhantomBandRules.ShouldInterrupt(target.IsCasting, target.IsCastInterruptible, distance, InterruptMeleeRangeYalms))
            TryPush(ctx, 41603, job, level, PrioInterrupt, target.GameObjectId); // Mineuchi

        if (job == PhantomJob.Bard
            && PhantomBandRules.ShouldInterrupt(target.IsCasting, target.IsCastInterruptible, distance, 25f))
            TryPush(ctx, 41609, job, level, PrioInterrupt, target.GameObjectId); // Romeo's Ballad
    }

    private void PushMpRestore(IRotationContext ctx, Config.PhantomConfig cfg, PhantomJob job, byte level, bool inCombat)
    {
        var player = ctx.Player;

        if (job == PhantomJob.Monk
            && PhantomBandRules.ShouldUseChakraForMp(cfg, player.CurrentMp, player.MaxMp, inCombat))
            TryPush(ctx, 41598, job, level, PrioMpRestore);

        if (job == PhantomJob.Chemist)
        {
            var potionCount = _phantomJobs.GetItemCount(PhantomJobData.OccultPotionItemId);
            if (PhantomBandRules.ShouldUseEther(cfg, player.CurrentMp, player.MaxMp, potionCount, inCombat))
                TryPush(ctx, 41633, job, level, PrioMpRestore + 1);
        }
    }

    private void PushPartyBuffs(IRotationContext ctx, PhantomJob job, byte level, bool inCombat)
    {
        if (!inCombat)
            return;

        // Real recast timers pace these; the ready-gate inside TryPush prevents spam.
        switch (job)
        {
            case PhantomJob.Bard:
                TryPush(ctx, 41608, job, level, PrioPartyBuff);     // Offensive Aria
                TryPush(ctx, 41607, job, level, PrioPartyBuff + 1); // Mighty March
                TryPush(ctx, 41610, job, level, PrioPartyBuff + 2); // Hero's Rime
                break;
            case PhantomJob.Geomancer:
                TryPush(ctx, 41611, job, level, PrioPartyBuff);     // Battle Bell
                TryPush(ctx, 41619, job, level, PrioPartyBuff + 1); // Ringing Respite
                break;
            case PhantomJob.Ranger:
                TryPush(ctx, 41599, job, level, PrioPartyBuff);     // Phantom Aim
                break;
            case PhantomJob.MysticKnight:
                TryPush(ctx, 46590, job, level, PrioPartyBuff);     // Magic Shell
                break;
        }
    }

    /// <summary>
    /// Common per-action gates (catalog membership, phantom level, duty-bar slot,
    /// cooldown) and the actual scheduler push. Target 0 = self.
    /// </summary>
    private void TryPush(IRotationContext ctx, uint actionId, PhantomJob job, byte level, int priority, ulong targetId = 0)
    {
        PhantomActionDef? found = null;
        foreach (var def in PhantomActions.All)
        {
            if (def.ActionId == actionId)
            {
                found = def;
                break;
            }
        }

        if (found is not { } action || action.Job != job || level < action.RequiredLevel)
            return;
        if (!_phantomJobs.IsSlotted(actionId))
            return;
        if (!_actionService.IsActionReady(actionId))
            return;

        if (!_behaviorCache.TryGetValue(actionId, out var behavior))
        {
            behavior = new AbilityBehavior { Action = _phantomJobs.GetActionDefinition(action) };
            _behaviorCache[actionId] = behavior;
        }

        var name = action.Name;
        Action<IRotationContext> onDispatched = _ =>
            _phantomJobs.LayerLastEvent = $"{DateTime.Now:HH:mm:ss} {name}";

        if (behavior.Action.IsGCD)
            _scheduler.PushGcd(behavior, targetId, priority, onDispatched);
        else
            _scheduler.PushOgcd(behavior, targetId, priority, onDispatched);
    }

    private bool HasLockoutStatus()
    {
        foreach (var statusId in PhantomActions.LockoutStatusIds)
        {
            if (_actionService.PlayerHasStatus(statusId))
                return true;
        }

        return false;
    }
}
