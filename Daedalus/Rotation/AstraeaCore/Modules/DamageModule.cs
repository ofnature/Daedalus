using Daedalus.Services.Action;
using Daedalus.Config;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.AstraeaCore.Abilities;
using Daedalus.Rotation.AstraeaCore.Context;
using Daedalus.Rotation.AstraeaCore.Helpers;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Modules;
using Daedalus.Services;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Services.Training;

namespace Daedalus.Rotation.AstraeaCore.Modules;

/// <summary>
/// Astrologian-specific damage module (scheduler-driven).
/// </summary>
public sealed class DamageModule : BaseDamageModule<IAstraeaContext>, IAstraeaModule
{
    private readonly IBurstWindowService? _burstWindowService;

    public DamageModule(IBurstWindowService? burstWindowService = null)
    {
        _burstWindowService = burstWindowService;
    }
    protected override bool IsDamageEnabled(IAstraeaContext context) => context.Configuration.Astrologian.EnableSingleTargetDamage;
    protected override bool IsDoTEnabled(IAstraeaContext context) => context.Configuration.Astrologian.EnableDot;
    protected override bool IsAoEDamageEnabled(IAstraeaContext context) => context.Configuration.Astrologian.EnableAoEDamage;
    protected override int AoEMinTargets(IAstraeaContext context) => context.Configuration.Astrologian.AoEDamageMinTargets;
    protected override float DoTRefreshThreshold(IAstraeaContext context) => context.Configuration.Astrologian.DotRefreshThreshold;
    protected override uint GetDoTStatusId(IAstraeaContext context) => ASTActions.GetDotStatusId(context.Player.Level);
    protected override ActionDefinition? GetDoTAction(IAstraeaContext context) => ASTActions.GetDotForLevel(context.Player.Level, context.ActionService);
    protected override ActionDefinition? GetAoEDamageAction(IAstraeaContext context) => ASTActions.GetAoEDamageForLevel(context.Player.Level, context.ActionService);
    protected override ActionDefinition GetSingleTargetAction(IAstraeaContext context, bool isMoving) => ASTActions.GetDamageGcdForLevel(context.Player.Level, context.ActionService);
    protected override void SetDpsState(IAstraeaContext context, string state) => context.Debug.DpsState = state;
    protected override void SetAoEDpsState(IAstraeaContext context, string state) => context.Debug.AoEDpsState = state;
    protected override void SetAoEDpsEnemyCount(IAstraeaContext context, int count) => context.Debug.AoEDpsEnemyCount = count;
    protected override void SetAoEDpsEngagedCount(IAstraeaContext context, int count) => context.Debug.AoEDpsEngagedCount = count;
    protected override void SetPlannedAction(IAstraeaContext context, string action) => context.Debug.PlannedAction = action;
    protected override bool BlocksOnExecution => false;
    protected override bool CanDoT(IAstraeaContext context, bool isMoving) => true;

    public override bool TryExecute(IAstraeaContext context, bool isMoving) => false;

    public new void CollectCandidates(IAstraeaContext context, RotationScheduler scheduler, bool isMoving)
    {
        if (!context.InCombat) return;
        if (AstraeaCardHelper.HasAstlock(context)) { SetDpsState(context, "Paused (Collective Unconscious)"); return; }
        if (context.TargetingService.IsDamageTargetingPaused()) { SetDpsState(context, "Paused (no target)"); return; }
        if (context.Configuration.Targeting.SuppressDamageOnForcedMovement
            && PlayerSafetyHelper.IsForcedMovementActive(context.Player))
        {
            SetDpsState(context, "Paused (forced movement)");
            return;
        }

        TryPushOracle(context, scheduler);
        TryPushLordOfCrowns(context, scheduler);
        TryPushDoT(context, scheduler);
        TryPushAoEDamage(context, scheduler);
        TryPushSingleTargetDamage(context, scheduler, isMoving);
    }

    public override void UpdateDebugState(IAstraeaContext context)
    {
        context.Debug.OracleState = context.HasDivining ? "Ready" : "Idle";
    }

    private void TryPushOracle(IAstraeaContext context, RotationScheduler scheduler)
    {
        var config = context.Configuration.Astrologian;
        var player = context.Player;

        if (!config.EnableOracle) return;
        if (!ActionAvailability.MeetsLevelAndLearned(player.Level, context.ActionService, ASTActions.Oracle)) return;
        if (!context.HasDivining) return;

        var target = context.TargetingService.FindEnemy(
            context.Configuration.Targeting.EnemyStrategy, ASTActions.Oracle.Range, player);
        if (target == null) return;

        scheduler.PushOgcd(AstraeaAbilities.Oracle, target.GameObjectId, priority: 285,
            onDispatched: _ =>
            {
                SetPlannedAction(context, ASTActions.Oracle.Name);
                context.Debug.OracleState = "Used";
                SetDpsState(context, "Oracle");
                context.TrainingService?.RecordConceptApplication(AstConcepts.OracleUsage, wasSuccessful: true, "Divining buff consumed");
            });
    }

    private void TryPushLordOfCrowns(IAstraeaContext context, RotationScheduler scheduler)
    {
        var config = context.Configuration.Astrologian;
        var player = context.Player;

        if (!config.EnableMinorArcana) return;
        if (!context.CardService.HasLord) return;
        if (!AstraeaCardHelper.ShouldPlayLord(context, _burstWindowService)) return;
        if (!ActionAvailability.MeetsLevelAndLearned(player.Level, context.ActionService, ASTActions.LordOfCrowns)) return;

        // A 20y burst around the Astrologian, not a targeted skill: it needs an enemy inside that ring.
        if (context.TargetingService.CountEnemiesInRange(ASTActions.LordOfCrowns.Radius, player) < 1) return;

        scheduler.PushOgcd(AstraeaAbilities.LordOfCrowns, player.GameObjectId, priority: 290,
            onDispatched: _ =>
            {
                SetPlannedAction(context, ASTActions.LordOfCrowns.Name);
                SetDpsState(context, "Lord of Crowns");
                context.TrainingService?.RecordConceptApplication(AstConcepts.DpsOptimization, wasSuccessful: true, "Lord of Crowns damage");
            });
    }

    private void TryPushDoT(IAstraeaContext context, RotationScheduler scheduler)
    {
        if (!IsDoTEnabled(context)) return;

        var dotAction = GetDoTAction(context);
        if (dotAction == null) return;

        // In a pack Gravity competes with the DoT: dot only the mobs that live past the break-even.
        float? packMinTtk = null;
        if (IsAoEDamageEnabled(context))
        {
            var aoeAction = GetAoEDamageAction(context);
            if (aoeAction != null)
            {
                var (_, hits) = context.TargetingService.FindBestAoETarget(aoeAction.Radius, aoeAction.Range, context.Player);
                if (hits >= AoEMinTargets(context))
                {
                    packMinTtk = AstraeaPackDot.BreakEvenSeconds(hits);
                    if (packMinTtk > AstraeaPackDot.DotDurationSeconds)
                    {
                        SetDpsState(context, $"DoT: skipped ({hits} enemies — Gravity is worth more)");
                        return;
                    }
                }
            }
        }

        var dotCastTime = context.HasSwiftcast ? 0f : dotAction.CastTime;
        if (MechanicCastGate.ShouldBlock(context, dotCastTime)) { SetDpsState(context, "DoT: " + MechanicCastGate.FormatBlockedState(context, dotCastTime)); return; }

        var dotStatusId = GetDoTStatusId(context);
        if (dotStatusId == 0) return;

        var target = packMinTtk is { } minTtk
            ? context.TargetingService.FindEnemyNeedingDot(dotStatusId, DoTRefreshThreshold(context), dotAction.Range, context.Player, minTtk)
            : context.TargetingService.FindEnemyNeedingDot(dotStatusId, DoTRefreshThreshold(context), dotAction.Range, context.Player);
        if (target == null)
        {
            // "No target" used to cover the normal case too — the DoT already ticking with more than the
            // refresh window left — which read as "never dots" (Saar, Forgiven Dissonance, 2026-10-05).
            var ours = context.TargetingService.GetBestStatusRemainingFromSourceOnAnyEnemy(
                [dotStatusId], context.Player.EntityId, dotAction.Range, context.Player);
            SetDpsState(context, ours > 0f ? $"DoT: up ({ours:0}s left)" : "DoT: no target (none in range, or dying)");
            return;
        }

        var capturedAction = dotAction;
        var behavior = new AbilityBehavior { Action = dotAction };

        scheduler.PushGcd(behavior, target.GameObjectId, priority: 310,
            onDispatched: _ =>
            {
                SetPlannedAction(context, capturedAction.Name);
                SetDpsState(context, "DoT");
            });
    }

    private void TryPushAoEDamage(IAstraeaContext context, RotationScheduler scheduler)
    {
        if (!IsAoEDamageEnabled(context)) return;

        var aoeAction = GetAoEDamageAction(context);
        if (aoeAction == null)
        {
            // Gravity is a job-quest unlock (Lv45): without it the pack counters below never update.
            SetAoEDpsState(context, "No AoE spell learned (Gravity: job quest)");
            return;
        }

        var aoeCastTime = context.HasSwiftcast ? 0f : aoeAction.CastTime;
        if (MechanicCastGate.ShouldBlock(context, aoeCastTime)) { SetAoEDpsState(context, MechanicCastGate.FormatBlockedState(context, aoeCastTime)); return; }

        // Gravity hits 8y around its TARGET. Counting around the Astrologian (as the shared module does
        // for self-centred AoEs) meant a healer standing back never saw a pack, and spread Combust instead.
        var (best, hits) = context.TargetingService.FindBestAoETarget(aoeAction.Radius, aoeAction.Range, context.Player);
        SetAoEDpsEnemyCount(context, hits);
        SetAoEDpsEngagedCount(context, context.TargetingService.CountEnemyPack(aoeAction.Range, context.Player).Engaged);
        if (hits < AoEMinTargets(context) || best == null) { SetAoEDpsState(context, $"{hits} < {AoEMinTargets(context)} min"); return; }

        var targetId = best.GameObjectId;

        var capturedAction = aoeAction;
        var capturedEnemyCount = hits;
        var behavior = new AbilityBehavior { Action = aoeAction };

        scheduler.PushGcd(behavior, targetId, priority: 320,
            onDispatched: _ =>
            {
                SetPlannedAction(context, capturedAction.Name);
                SetDpsState(context, $"AoE ({capturedEnemyCount} targets)");
                SetAoEDpsState(context, $"{capturedEnemyCount} enemies");
            });
    }

    private void TryPushSingleTargetDamage(IAstraeaContext context, RotationScheduler scheduler, bool isMoving)
    {
        if (!IsDamageEnabled(context)) { SetDpsState(context, "Damage disabled"); return; }
        if (isMoving && !context.HasLightspeed) return;

        var action = GetSingleTargetAction(context, isMoving);
        var stCastTime = context.HasSwiftcast || context.HasLightspeed ? 0f : action.CastTime;
        if (MechanicCastGate.ShouldBlock(context, stCastTime)) { SetDpsState(context, MechanicCastGate.FormatBlockedState(context, stCastTime)); return; }

        var target = context.TargetingService.FindEnemy(
            context.Configuration.Targeting.EnemyStrategy, action.Range, context.Player);
        if (target == null) { SetDpsState(context, "No enemy found"); return; }

        var capturedAction = action;
        var behavior = new AbilityBehavior { Action = action };

        scheduler.PushGcd(behavior, target.GameObjectId, priority: 330,
            onDispatched: _ =>
            {
                SetPlannedAction(context, capturedAction.Name);
                SetDpsState(context, capturedAction.Name);
            });
    }
}
