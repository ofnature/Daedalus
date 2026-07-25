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
    private const int PrioDamage = 300;

    private const float InterruptMeleeRangeYalms = 5f;
    private const float RangeBufferYalms = 0.5f;

    private readonly ActionService _actionService;
    private readonly Configuration _configuration;
    private readonly PhantomJobService _phantomJobs;
    private readonly IBurstWindowService? _burstWindows;
    private readonly RotationScheduler _scheduler;
    private readonly IPluginLog _log;

    private readonly Dictionary<uint, AbilityBehavior> _behaviorCache = [];
    private readonly List<string> _pushRejects = [];
    private bool _dispatchedThisFrame;
    private bool _framePrepared;
    private bool _isMovingThisFrame;

    public PhantomActionLayer(
        ActionService actionService,
        IJobGauges jobGauges,
        Configuration configuration,
        PhantomJobService phantomJobs,
        ITimelineService? timelineService,
        IErrorMetricsService? errorMetrics,
        IPluginLog log,
        IBurstWindowService? burstWindows = null)
    {
        _actionService = actionService;
        _configuration = configuration;
        _phantomJobs = phantomJobs;
        _burstWindows = burstWindows;
        _log = log;
        _scheduler = new RotationScheduler(actionService, jobGauges, configuration, timelineService, errorMetrics);
    }

    /// <summary>
    /// Runs BEFORE the job's modules: collects every band and pre-empts the GCD window
    /// for phantom GCDs (damage GCDs and emergency heal GCDs would otherwise starve —
    /// the job rotation wins every window; RSR checks duty actions first the same way).
    /// Never throws.
    /// </summary>
    public void ExecutePreModules(IRotationContext ctx, bool isMoving, bool inCombat)
    {
        try
        {
            _framePrepared = false;
            PreModulesCore(ctx, isMoving, inCombat);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "PhantomActionLayer: pre-modules failed");
        }
    }

    /// <summary>
    /// Runs AFTER the job's modules: dispatches queued phantom oGCDs into leftover
    /// weave slots (job weaves outrank phantom weaves). Never throws.
    /// </summary>
    public void ExecutePostModules(IRotationContext ctx, bool isMoving, bool inCombat)
    {
        try
        {
            if (!_framePrepared)
                return;

            // Live ActionService state — the context's CanExecute flags are frozen at
            // context creation, before the job's modules consumed their slots.
            if (_actionService.CanExecuteOgcd)
                _scheduler.DispatchOgcd(ctx);
            if (_actionService.CanExecuteGcd)
                _scheduler.DispatchGcd(ctx);

            var queued = _scheduler.InspectGcdQueue().Count + _scheduler.InspectOgcdQueue().Count;
            _phantomJobs.LayerLastEvent = _dispatchedThisFrame
                ? "dispatched"
                : queued > 0
                    ? $"waiting — {queued} queued, no free slot"
                    : _pushRejects.Count > 0
                        ? $"blocked — {_pushRejects[0]}"
                        : "idle — nothing eligible";
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "PhantomActionLayer: post-modules failed");
        }
    }

    private void PreModulesCore(IRotationContext ctx, bool isMoving, bool inCombat)
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

        if (inCombat && FindLockoutStatus() is { } lockoutId)
        {
            _phantomJobs.LayerLastEvent = $"held — lockout status {lockoutId}";
            return;
        }

        var player = ctx.Player;
        var selfHpPct = player.MaxHp > 0 ? (float)player.CurrentHp / player.MaxHp : 1f;

        _scheduler.Reset();
        _dispatchedThisFrame = false;
        _isMovingThisFrame = isMoving;
        _pushRejects.Clear();
        _framePrepared = true;

        PushSurvival(ctx, cfg, job, level, selfHpPct, inCombat);
        PushSelfMit(ctx, job, level, selfHpPct, inCombat);
        PushInterrupts(ctx, job, level, inCombat);
        PushMpRestore(ctx, cfg, job, level, inCombat);
        PushPartyBuffs(ctx, job, level, inCombat);
        PushDamage(ctx, cfg, job, level, inCombat);

        // GCD pre-empt: phantom GCDs (emergency heals first, then damage) claim the GCD
        // window before the job's filler. Big phantom cooldowns pace this to ~1 GCD per
        // 30-60s. The oGCD queue waits for post-modules leftover weave slots.
        if (_actionService.CanExecuteGcd)
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

    private void PushDamage(IRotationContext ctx, Config.PhantomConfig cfg, PhantomJob job, byte level, bool inCombat)
    {
        if (!inCombat)
            return;

        var target = ctx.TargetingService.GetUserEnemyTarget() ?? ctx.Player.TargetObject as IBattleChara;
        if (target is null || target.IsDead)
            return;

        var targetHpPct = target.MaxHp > 0 ? (float)target.CurrentHp / target.MaxHp : 1f;
        var distance = System.Numerics.Vector3.Distance(ctx.Player.Position, target.Position) - target.HitboxRadius;

        // Executes / non-scaling utility fire regardless of the burst hold (RSR parity).
        if (job == PhantomJob.Thief && PhantomBandRules.ShouldSteal(targetHpPct))
            TryPush(ctx, 41645, job, level, PrioDamage, target.GameObjectId, target); // Steal

        var hold = PhantomBandRules.ShouldHoldDamage(
            cfg.SaveDamageForBurst,
            _burstWindows?.IsInBurstWindow ?? false,
            _burstWindows?.SecondsSinceLastBurstStart ?? -1f);
        if (hold)
        {
            _pushRejects.Add("damage held for burst window");
            return;
        }

        switch (job)
        {
            case PhantomJob.Berserker:
                TryPush(ctx, 41592, job, level, PrioDamage, target.GameObjectId, target);     // Rage
                TryPush(ctx, 41594, job, level, PrioDamage + 1, target.GameObjectId, target); // Deadly Blow
                break;

            case PhantomJob.Samurai:
                if (_phantomJobs.GetItemCount(PhantomJobData.ZeninageCofferItemId) > 0)
                    TryPush(ctx, 41606, job, level, PrioDamage, target.GameObjectId, target); // Zeninage
                TryPush(ctx, 41605, job, level, PrioDamage + 1, target.GameObjectId, target); // Iainuki
                break;

            case PhantomJob.Cannoneer:
                TryPush(ctx, 41630, job, level, PrioDamage, target.GameObjectId, target);     // Silver Cannon
                if (cfg.CannoneerPreferDarkCannon)
                {
                    TryPush(ctx, 41628, job, level, PrioDamage + 1, target.GameObjectId, target); // Dark
                    TryPush(ctx, 41629, job, level, PrioDamage + 2, target.GameObjectId, target); // Shock
                }
                else
                {
                    TryPush(ctx, 41629, job, level, PrioDamage + 1, target.GameObjectId, target);
                    TryPush(ctx, 41628, job, level, PrioDamage + 2, target.GameObjectId, target);
                }
                TryPush(ctx, 41627, job, level, PrioDamage + 3, target.GameObjectId, target); // Holy Cannon
                TryPush(ctx, 41626, job, level, PrioDamage + 4, target.GameObjectId, target); // Phantom Fire
                break;

            case PhantomJob.MysticKnight:
                TryPush(ctx, 46593, job, level, PrioDamage, target.GameObjectId, target);     // Blazing Spellblade
                TryPush(ctx, 46592, job, level, PrioDamage + 1, target.GameObjectId, target); // Holy Spellblade
                TryPush(ctx, 46591, job, level, PrioDamage + 2, target.GameObjectId, target); // Sundering Spellblade
                break;

            case PhantomJob.Gladiator:
                TryPush(ctx, 46594, job, level, PrioDamage, target.GameObjectId, target);     // Finisher
                TryPush(ctx, 46596, job, level, PrioDamage + 1, target.GameObjectId, target); // Long Reach
                TryPush(ctx, 46597, job, level, PrioDamage + 2, target.GameObjectId, target); // Bladeblitz
                break;

            case PhantomJob.Monk:
                if (PhantomBandRules.ShouldPhantomKick(distance, cfg.MonkKickMaxRangeYalms))
                    TryPush(ctx, 41595, job, level, PrioDamage, target.GameObjectId, target); // Phantom Kick
                break;

            case PhantomJob.TimeMage:
                TryPush(ctx, 41623, job, level, PrioDamage, target.GameObjectId, target);     // Occult Comet
                break;

            case PhantomJob.Thief:
                TryPush(ctx, 41649, job, level, PrioDamage + 5, target.GameObjectId, target); // Pilfer Weapon
                break;
        }
    }

    /// <summary>
    /// Common per-action gates (catalog membership, phantom level, duty-bar slot,
    /// cooldown, range, cast-while-moving) and the actual scheduler push. Target 0 = self.
    /// </summary>
    private void TryPush(IRotationContext ctx, uint actionId, PhantomJob job, byte level, int priority,
        ulong targetId = 0, IBattleChara? rangeTarget = null)
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

        if (found is not { } action || action.Job != job)
            return;
        // Below the phantom-level unlock = the action simply isn't learned yet — not a
        // fixable blocker, so it doesn't pollute the "blocked" readout.
        if (level < action.RequiredLevel)
            return;
        if (!_phantomJobs.IsSlotted(actionId))
        {
            _pushRejects.Add($"{action.Name} not on duty bar");
            return;
        }
        if (!_actionService.IsActionReady(actionId))
        {
            _pushRejects.Add($"{action.Name} on cooldown");
            return;
        }

        if (!_behaviorCache.TryGetValue(actionId, out var behavior))
        {
            behavior = new AbilityBehavior { Action = _phantomJobs.GetActionDefinition(action) };
            _behaviorCache[actionId] = behavior;
        }

        if (behavior.Action.CastTime > 0 && _isMovingThisFrame)
        {
            _pushRejects.Add($"{action.Name} needs a hard cast (moving)");
            return;
        }

        if (rangeTarget is not null && behavior.Action.Range > 0)
        {
            var dist = System.Numerics.Vector3.Distance(ctx.Player.Position, rangeTarget.Position)
                       - rangeTarget.HitboxRadius;
            if (dist > behavior.Action.Range + RangeBufferYalms)
            {
                _pushRejects.Add($"{action.Name} out of range");
                return;
            }
        }

        var name = action.Name;
        Action<IRotationContext> onDispatched = _ =>
        {
            _dispatchedThisFrame = true;
            _phantomJobs.LayerLastDispatch = $"{DateTime.Now:HH:mm:ss} {name}";
        };

        if (behavior.Action.IsGCD)
            _scheduler.PushGcd(behavior, targetId, priority, onDispatched);
        else
            _scheduler.PushOgcd(behavior, targetId, priority, onDispatched);
    }

    private uint? FindLockoutStatus()
    {
        foreach (var statusId in PhantomActions.LockoutStatusIds)
        {
            if (_actionService.PlayerHasStatus(statusId))
                return statusId;
        }

        return null;
    }
}
