using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Daedalus.Data;
using Daedalus.Rotation.Common;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Services;
using Daedalus.Services.Action;
using Daedalus.Services.Diagnostics;
using Daedalus.Services.Occult;
using Daedalus.Services.Party;
using Daedalus.Timeline;

namespace Daedalus.Rotation.Phantom;

/// <summary>
/// Bozja Lost Action executor — sibling of <see cref="VariantActionLayer"/> on the same BaseRotation
/// pre/post hooks, using the actions RSR's "Bozja Reborn" rotation uses, placed where RSR places them:
/// raises, heals and barriers ahead of the job; buffs and damage GCDs ahead of the job's filler; combat
/// self buffs in the weave slots the job leaves; Lost Focus only on a GCD the job leaves free.
/// Inert unless one of them is set to a duty action slot, which only happens in Bozjan content (BMR's
/// Bozja module gates the same way), so no territory list is needed.
/// </summary>
public sealed class BozjaActionLayer
{
    private const ushort RaisePendingStatusId = 148;
    private const int RaiseCastMs = 3000;
    private const float RangeBufferYalms = 0.5f;

    private readonly ActionService _actionService;
    private readonly Configuration _configuration;
    private readonly PhantomJobService _dutyState;
    private readonly IPartyCoordinationService? _partyCoordination;
    private readonly RotationScheduler _scheduler;
    private readonly IPluginLog _log;

    private readonly Dictionary<uint, AbilityBehavior> _behaviorCache = [];
    private readonly Dictionary<(ulong Target, uint Action), DateTime> _recentCasts = [];
    private readonly List<LostActionDef> _slotted = [];
    private bool _framePrepared;
    private bool _raisePending;
    private bool _raiseQueued;

    public BozjaActionLayer(
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

    /// <summary>
    /// Is a leap from here to there safe? Supplied by Plugin from <see cref="TargetedDashGuard"/>, as for
    /// Phantom Kick. Null means nothing is known, and the leap is allowed.
    /// </summary>
    public Func<Vector3, Vector3, bool>? DashSafety { get; set; }

    /// <summary>Pre-modules: raises, heals, barriers, buffs and damage GCDs; they may pre-empt the job.</summary>
    public void ExecutePreModules(IRotationContext ctx, bool isMoving, bool inCombat)
    {
        try
        {
            _framePrepared = false;
            PreModulesCore(ctx, isMoving, inCombat);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "BozjaActionLayer: pre-modules failed");
        }
    }

    /// <summary>Post-modules: combat self buffs and oGCD attacks into leftover weaves; free GCDs.</summary>
    public void ExecutePostModules(IRotationContext ctx, bool isMoving, bool inCombat)
    {
        try
        {
            if (!_framePrepared)
                return;

            if (inCombat)
            {
                foreach (var def in _slotted)
                {
                    switch (def.Role)
                    {
                        case LostRole.SelfBuff:
                            if (!BozjaActionRules.NeedsBuff(def, id => RemainingOn(ctx.Player, id), inCombat: true))
                                break;
                            Push(ctx, def, 0);
                            break;
                        case LostRole.GapCloser:
                            PushSeraphStrike(ctx, def);
                            break;
                        case LostRole.Cone:
                            if (EnemyTarget(ctx) is { } coneTarget && InRange(ctx, coneTarget, def.Range))
                                Push(ctx, def, coneTarget.GameObjectId);
                            break;
                        case LostRole.FreeGcdSelfBuff:
                            // Only into a GCD the job left empty: a Boost stack is not worth a job GCD.
                            if (_actionService.CanExecuteGcd && !isMoving
                                && BozjaActionRules.NeedsBuff(def, id => RemainingOn(ctx.Player, id), inCombat: true))
                                Push(ctx, def, 0);
                            break;
                    }
                }
            }

            if (_actionService.CanExecuteOgcd)
                _scheduler.DispatchOgcd(ctx);
            if ((!_raisePending || _raiseQueued) && _actionService.CanExecuteGcd)
                _scheduler.DispatchGcd(ctx);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "BozjaActionLayer: post-modules failed");
        }
    }

    private void PreModulesCore(IRotationContext ctx, bool isMoving, bool inCombat)
    {
        var cfg = _configuration.Bozja;
        if (!cfg.EnableLostActions)
            return;

        var slots = _dutyState.GetDutySlotIds();
        _slotted.Clear();
        foreach (var def in BozjaActionData.All)
        {
            if (Array.IndexOf(slots, def.ActionId) >= 0 && cfg.IsEnabled(def))
                _slotted.Add(def);
        }

        if (_slotted.Count == 0)
            return;

        _scheduler.Reset();
        _framePrepared = true;
        PruneRecentCasts();

        var raiseQueued = false;
        var weaveHealQueued = false;
        foreach (var def in _slotted)
        {
            // A hard cast would only be refused by the scheduler.
            if (def.CastTime > 0 && isMoving)
                continue;

            switch (def.Role)
            {
                case LostRole.Raise:
                    raiseQueued |= PushRaise(ctx, def);
                    break;
                case LostRole.SelfAreaHeal:
                    if (!RecentlyUsed(def) && CountHurtNear(ctx, ctx.Player.Position, def.Radius, cfg) >= BozjaActionRules.AreaHealMinTargets)
                        weaveHealQueued |= Push(ctx, def, 0);
                    break;
                case LostRole.AreaHeal:
                    // A GCD area heal only when no weave heal is on its way this frame.
                    if ((def.IsGcd && weaveHealQueued) || RecentlyUsed(def))
                        break;
                    if (PickAreaHealCentre(ctx, def, cfg) is { } centre)
                        weaveHealQueued |= Push(ctx, def, centre.GameObjectId) && !def.IsGcd;
                    break;
                case LostRole.SingleHeal:
                    if (def.IsGcd && weaveHealQueued)
                        break;
                    if (PickLowest(ctx, cfg, def) is { } hurt)
                        weaveHealQueued |= Push(ctx, def, hurt.GameObjectId) && !def.IsGcd;
                    break;
                case LostRole.AreaBarrier:
                    if (inCombat && !RecentlyUsed(def) && AoeIncoming(ctx)
                        && CountWithout(ctx, def, def.Radius) >= BozjaActionRules.AreaBarrierMinTargets)
                        Push(ctx, def, 0);
                    break;
                case LostRole.Barrier:
                    if (inCombat && BusterTarget(ctx) is { } victim
                        && !RecentlyCastAt(victim.GameObjectId, def)
                        && BozjaActionRules.NeedsBuff(def, id => RemainingOn(victim, id), inCombat: true))
                        Push(ctx, def, victim.GameObjectId);
                    break;
                case LostRole.Forge:
                    if (inCombat)
                        PushForge(ctx, cfg, def);
                    break;
                case LostRole.AversionAoe:
                    if (inCombat)
                        PushAversionAoe(ctx, cfg, def);
                    break;
                case LostRole.PartyBuff:
                    if (PickBuffTarget(ctx, cfg, def, inCombat) is { } buffTarget)
                        Push(ctx, def, buffTarget.GameObjectId);
                    break;
                case LostRole.AoeDot:
                    if (inCombat && EnemyTarget(ctx) is { } dotTarget && InRange(ctx, dotTarget, def.Radius)
                        && RemainingOn(dotTarget, BozjaActionData.LostFlareStarStatusId) is null)
                        Push(ctx, def, 0);
                    break;
            }
        }

        _raisePending = RaisePendingForJob(ctx);
        _raiseQueued = raiseQueued;
        var isHealer = JobRegistry.IsHealer(ctx.Player.ClassJob.RowId);
        if (_actionService.CanExecuteGcd && BozjaActionRules.MayPreemptGcd(inCombat, isHealer, _raisePending, raiseQueued))
            _scheduler.DispatchGcd(ctx);

        // Heal weaves go ahead of the job's weaves, as in RSR.
        if (_actionService.CanExecuteOgcd)
            _scheduler.DispatchOgcd(ctx);
    }

    // ---- target picking ----

    /// <summary>Ourselves, then every other party member: alive, within <paramref name="range"/> yalms.</summary>
    private static IEnumerable<(IBattleChara Chara, bool IsSelf)> Party(IRotationContext ctx, float range)
    {
        yield return (ctx.Player, true);

        foreach (var member in ctx.PartyList)
        {
            if (member?.GameObject is IBattleChara chara
                && chara.GameObjectId != ctx.Player.GameObjectId
                && !chara.IsDead
                && InRange(ctx, chara, range))
                yield return (chara, false);
        }
    }

    private IBattleChara? PickBuffTarget(IRotationContext ctx, Config.BozjaConfig cfg, LostActionDef def, bool inCombat)
    {
        foreach (var (chara, isSelf) in Party(ctx, def.Range))
        {
            if (BozjaActionRules.TargetAllowed(cfg, isSelf) && Wants(ctx, def, chara, inCombat))
                return chara;
        }

        return null;
    }

    private bool Wants(IRotationContext ctx, LostActionDef def, IBattleChara chara, bool inCombat)
        => !RecentlyCastAt(chara.GameObjectId, def)
           && BozjaActionRules.NeedsBuff(def, id => RemainingOn(chara, id), inCombat)
           && !SomeoneElseIsCasting(ctx, chara.GameObjectId, def);

    private IBattleChara? PickLowest(IRotationContext ctx, Config.BozjaConfig cfg, LostActionDef def)
    {
        IBattleChara? worst = null;
        var worstPct = float.MaxValue;
        foreach (var (chara, _) in Party(ctx, def.Range))
        {
            var pct = HpPct(chara);
            if (BozjaActionRules.NeedsHeal(cfg, pct) && pct < worstPct && !RecentlyCastAt(chara.GameObjectId, def))
            {
                worst = chara;
                worstPct = pct;
            }
        }

        return worst;
    }

    /// <summary>The party member whose surroundings hold the most hurt people, if enough are hurt.</summary>
    private static IBattleChara? PickAreaHealCentre(IRotationContext ctx, LostActionDef def, Config.BozjaConfig cfg)
    {
        IBattleChara? best = null;
        var bestCount = 0;
        foreach (var (chara, _) in Party(ctx, def.Range))
        {
            var count = CountHurtNear(ctx, chara.Position, def.Radius, cfg);
            if (count > bestCount)
            {
                best = chara;
                bestCount = count;
            }
        }

        return bestCount >= BozjaActionRules.AreaHealMinTargets ? best : null;
    }

    private static int CountHurtNear(IRotationContext ctx, Vector3 centre, float radius, Config.BozjaConfig cfg)
    {
        var count = 0;
        foreach (var (chara, _) in Party(ctx, float.MaxValue))
        {
            if (Vector3.Distance(centre, chara.Position) <= radius && BozjaActionRules.NeedsHeal(cfg, HpPct(chara)))
                count++;
        }

        return count;
    }

    private static int CountWithout(IRotationContext ctx, LostActionDef def, float radius)
    {
        var count = 0;
        foreach (var (chara, _) in Party(ctx, radius))
        {
            if (BozjaActionRules.NeedsBuff(def, id => RemainingOn(chara, id), inCombat: true))
                count++;
        }

        return count;
    }

    /// <summary>
    /// The party member our enemy is casting at — RSR's "defend single" moment (a tankbuster), read the
    /// only way we can: a cast bar on the enemy aimed at one of us.
    /// </summary>
    private static IBattleChara? BusterTarget(IRotationContext ctx)
    {
        if (EnemyTarget(ctx) is not { IsCasting: true } enemy)
            return null;

        foreach (var (chara, _) in Party(ctx, 30f))
        {
            if (chara.GameObjectId == enemy.CastTargetObjectId)
                return chara;
        }

        return null;
    }

    /// <summary>
    /// An AoE is coming: the timeline says a raidwide is near, or our enemy is casting at nobody in
    /// particular (a ground/self-centred cast — how area attacks are cast).
    /// </summary>
    private bool AoeIncoming(IRotationContext ctx)
    {
        if (ApolloCore.Helpers.TimelineHelper.IsRaidwideImminent(ctx.TimelineService, null, _configuration, out _))
            return true;

        if (EnemyTarget(ctx) is not { IsCasting: true } enemy)
            return false;

        foreach (var (chara, _) in Party(ctx, float.MaxValue))
        {
            if (chara.GameObjectId == enemy.CastTargetObjectId)
                return false;
        }

        return true;
    }

    // ---- per-role pushes ----

    private bool PushRaise(IRotationContext ctx, LostActionDef def)
    {
        foreach (var member in ctx.PartyList)
        {
            if (member?.GameObject is not IBattleChara corpse
                || corpse.GameObjectId == ctx.Player.GameObjectId
                || !corpse.IsDead
                || RemainingOn(corpse, RaisePendingStatusId) is not null
                || !InRange(ctx, corpse, def.Range))
                continue;

            var reserveId = (uint)corpse.GameObjectId;
            if (_partyCoordination?.IsRaiseTargetReservedByOther(reserveId) == true
                || SomeoneElseIsCasting(ctx, corpse.GameObjectId, def))
            {
                ReviveDiagnostics.Report(ReviveSource.BozjaRaise, "reserved by another toon");
                continue;
            }

            var queued = Push(ctx, def, corpse.GameObjectId,
                () => _partyCoordination?.ReserveRaiseTarget(reserveId, def.ActionId, RaiseCastMs, usingSwiftcast: false));
            ReviveDiagnostics.Report(ReviveSource.BozjaRaise,
                queued ? $"{def.Name} queued on {corpse.Name?.TextValue ?? "target"}" : $"{def.Name} not ready");
            return queued;
        }

        ReviveDiagnostics.Report(ReviveSource.BozjaRaise, "nobody to raise");
        return false;
    }

    private void PushForge(IRotationContext ctx, Config.BozjaConfig cfg, LostActionDef def)
    {
        if (EnemyTarget(ctx) is not { } enemy)
            return;

        var magicAverse = RemainingOn(enemy, BozjaActionData.MagicalAversionStatusId) is not null;
        var physicalAverse = RemainingOn(enemy, BozjaActionData.PhysicalAversionStatusId) is not null;
        if (!magicAverse && !physicalAverse)
            return;

        var isSpellforge = def.ActionId == 20706;
        foreach (var (chara, isSelf) in Party(ctx, def.Range))
        {
            var jobId = isSelf ? ctx.Player.ClassJob.RowId : TrustPartyRoleHelper.ResolveJobId(chara, ctx.PartyList);
            var dealsMagic = JobRegistry.IsHealer(jobId) || JobRegistry.IsCasterDps(jobId);
            if (BozjaActionRules.TargetAllowed(cfg, isSelf)
                && BozjaActionRules.WantsForge(isSpellforge, dealsMagic, magicAverse, physicalAverse)
                && Wants(ctx, def, chara, inCombat: true))
            {
                Push(ctx, def, chara.GameObjectId);
                return;
            }
        }
    }

    private void PushAversionAoe(IRotationContext ctx, Config.BozjaConfig cfg, LostActionDef def)
    {
        var target = EnemyTarget(ctx);
        var aversion = def.ActionId == 23909
            ? BozjaActionData.MagicalAversionStatusId
            : BozjaActionData.PhysicalAversionStatusId;
        var inRange = target is not null && InRange(ctx, target, def.Radius);
        var averse = inRange && RemainingOn(target!, aversion) is not null;
        var debuffed = inRange && RemainingOn(target!, def.Provides[0]) is not null;
        var enemies = cfg.AversionAoeSpam ? ctx.TargetingService.CountEnemiesInRange(def.Radius, ctx.Player) : 0;

        if (BozjaActionRules.ShouldAversionAoe(cfg.AversionAoeSpam, enemies, averse, debuffed))
            Push(ctx, def, 0);
    }

    private void PushSeraphStrike(IRotationContext ctx, LostActionDef def)
    {
        if (EnemyTarget(ctx) is not { } target || !InRange(ctx, target, def.Range))
            return;

        var isHealer = JobRegistry.IsHealer(ctx.Player.ClassJob.RowId);
        var hasStance = RemainingOn(ctx.Player, BozjaActionData.ClericStanceStatusId) is not null;
        var dashSafe = DashSafety is null || DashSafety(ctx.Player.Position, target.Position);
        if (BozjaActionRules.SeraphAllowed(isHealer, hasStance, dashSafe))
            Push(ctx, def, target.GameObjectId);
    }

    /// <summary>Queue one use. Target 0 = self / self-centred. Returns whether it was queued.</summary>
    private bool Push(IRotationContext ctx, LostActionDef def, ulong targetId, Action? onDispatched = null)
    {
        if (!_actionService.IsActionReady(def.ActionId))
            return false;

        if (!_behaviorCache.TryGetValue(def.ActionId, out var behavior))
        {
            behavior = new AbilityBehavior { Action = BuildDefinition(def) };
            _behaviorCache[def.ActionId] = behavior;
        }

        var priority = IndexOf(def);
        var who = targetId == 0 || targetId == ctx.Player.GameObjectId
            ? "self"
            : ctx.ObjectTable.SearchById(targetId)?.Name.TextValue ?? "target";
        Action<IRotationContext> dispatched = _ =>
        {
            _recentCasts[(targetId == 0 ? ctx.Player.GameObjectId : targetId, def.ActionId)] = DateTime.UtcNow;
            onDispatched?.Invoke();
            Daedalus.Rotation.Base.RotationServices.DebugLog?.Log(
                Daedalus.Services.Debug.DebugLogCategory.Action,
                Daedalus.Services.Debug.DebugLogSeverity.Info,
                $"Bozja: {def.Name} on {who}");
        };

        if (def.IsGcd)
            _scheduler.PushGcd(behavior, targetId, priority, dispatched);
        else
            _scheduler.PushOgcd(behavior, targetId, priority, dispatched);
        return true;
    }

    private static int IndexOf(LostActionDef def)
    {
        for (var i = 0; i < BozjaActionData.All.Count; i++)
        {
            if (ReferenceEquals(BozjaActionData.All[i], def))
                return i;
        }

        return BozjaActionData.All.Count;
    }

    /// <summary>
    /// Built from the table, not the Action sheet's category: the table's GCD flag follows the recast
    /// group, which is what the scheduler needs (Lost Focus is an "Ability" on the GCD).
    /// </summary>
    private static Daedalus.Models.Action.ActionDefinition BuildDefinition(LostActionDef def) => new()
    {
        ActionId = def.ActionId,
        Name = def.Name,
        MinLevel = 1,
        Category = def.IsGcd ? Daedalus.Models.Action.ActionCategory.GCD : Daedalus.Models.Action.ActionCategory.oGCD,
        TargetType = Daedalus.Models.Action.ActionTargetType.Self,
        CanTargetDead = def.Role == LostRole.Raise,
        CastTime = def.CastTime,
        RecastTime = def.RecastTime,
        Range = def.Range,
        Radius = def.Radius,
    };

    // ---- helpers ----

    private static IBattleChara? EnemyTarget(IRotationContext ctx)
    {
        var target = ctx.TargetingService.GetUserEnemyTarget() ?? ctx.Player.TargetObject as IBattleChara;
        return target is { IsDead: false } ? target : null;
    }

    private static bool InRange(IRotationContext ctx, IBattleChara target, float range)
        => range <= 0f || range == float.MaxValue
           || Vector3.Distance(ctx.Player.Position, target.Position) - target.HitboxRadius <= range + RangeBufferYalms;

    private static float HpPct(IBattleChara chara)
        => chara.MaxHp == 0 ? 1f : (float)chara.CurrentHp / chara.MaxHp;

    private static float? RemainingOn(IBattleChara chara, uint statusId)
    {
        if (chara.StatusList == null)
            return null;

        foreach (var status in chara.StatusList)
        {
            if (status != null && status.StatusId == statusId)
                return status.RemainingTime;
        }

        return null;
    }

    /// <summary>
    /// Another party member (usually another of our toons) is already casting this action at the target.
    /// Every toon sees the same missing buff or corpse on the same frame, so without this they all start
    /// on the same person.
    /// </summary>
    private static bool SomeoneElseIsCasting(IRotationContext ctx, ulong targetId, LostActionDef def)
    {
        foreach (var member in ctx.PartyList)
        {
            if (member?.GameObject is IBattleChara caster
                && caster.GameObjectId != ctx.Player.GameObjectId
                && caster.IsCasting
                && caster.CastTargetObjectId == targetId
                && BozjaActionData.Find(caster.CastActionId) is { } casting
                && SharesPurpose(casting, def))
                return true;
        }

        return false;
    }

    /// <summary>Two actions do the same job on a target: same action, or they cover the same status.</summary>
    private static bool SharesPurpose(LostActionDef a, LostActionDef b)
    {
        if (a.ActionId == b.ActionId || (a.Role == LostRole.Raise && b.Role == LostRole.Raise))
            return true;

        foreach (var s in a.Provides)
        {
            if (Array.IndexOf(b.Provides, s) >= 0)
                return true;
        }

        return false;
    }

    /// <summary>We sent this action, or one doing the same job, at this target a moment ago.</summary>
    private bool RecentlyCastAt(ulong targetId, LostActionDef def)
    {
        foreach (var (target, actionId) in _recentCasts.Keys)
        {
            if (target == targetId && BozjaActionData.Find(actionId) is { } sent && SharesPurpose(sent, def))
                return true;
        }

        return false;
    }

    /// <summary>We used this action at anyone a moment ago — its heal or barrier may not have landed yet.</summary>
    private bool RecentlyUsed(LostActionDef def)
    {
        foreach (var (_, actionId) in _recentCasts.Keys)
        {
            if (actionId == def.ActionId)
                return true;
        }

        return false;
    }

    private void PruneRecentCasts()
    {
        if (_recentCasts.Count == 0)
            return;

        var cutoff = DateTime.UtcNow.AddSeconds(-BozjaActionRules.RecentCastGuardSeconds);
        var expired = new List<(ulong, uint)>();
        foreach (var (key, at) in _recentCasts)
        {
            if (at < cutoff)
                expired.Add(key);
        }

        foreach (var key in expired)
            _recentCasts.Remove(key);
    }

    /// <summary>
    /// A raisable body is waiting and this job can raise it, so the layer must not take the GCD with
    /// anything but its own raise. Same check as <see cref="VariantActionLayer"/>.
    /// </summary>
    private static bool RaisePendingForJob(IRotationContext ctx)
    {
        if (!JobRegistry.IsHealer(ctx.Player.ClassJob.RowId))
            return false;

        foreach (var member in ctx.PartyList)
        {
            if (member?.GameObject is IBattleChara chara
                && chara.GameObjectId != ctx.Player.GameObjectId
                && chara.IsDead
                && RemainingOn(chara, RaisePendingStatusId) is null
                && InRange(ctx, chara, 30f))
                return true;
        }

        return false;
    }
}
