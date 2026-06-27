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
        if (player.Level < ASTActions.Oracle.MinLevel) return;
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
        if (player.Level < ASTActions.LordOfCrowns.MinLevel) return;

        var target = context.TargetingService.FindEnemy(
            context.Configuration.Targeting.EnemyStrategy, ASTActions.LordOfCrowns.Range, player);
        if (target == null) return;

        scheduler.PushOgcd(AstraeaAbilities.LordOfCrowns, target.GameObjectId, priority: 290,
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

        if (IsAoEDamageEnabled(context))
        {
            var aoeAction = GetAoEDamageAction(context);
            if (aoeAction != null)
            {
                var pack = context.TargetingService.CountEnemyPack(aoeAction.Radius, context.Player);
                if (pack.AoeRange >= AoEMinTargets(context)) { SetDpsState(context, $"DoT: skipped ({pack.AoeRange} enemies)"); return; }
            }
        }

        var dotCastTime = context.HasSwiftcast ? 0f : dotAction.CastTime;
        if (MechanicCastGate.ShouldBlock(context, dotCastTime)) { SetDpsState(context, "DoT: mechanic imminent"); return; }

        var dotStatusId = GetDoTStatusId(context);
        if (dotStatusId == 0) return;

        var target = context.TargetingService.FindEnemyNeedingDot(dotStatusId, DoTRefreshThreshold(context), dotAction.Range, context.Player);
        if (target == null) { SetDpsState(context, "DoT: no target"); return; }

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
        if (aoeAction == null) return;

        var aoeCastTime = context.HasSwiftcast ? 0f : aoeAction.CastTime;
        if (MechanicCastGate.ShouldBlock(context, aoeCastTime)) { SetAoEDpsState(context, "Holding: mechanic imminent"); return; }

        var pack = context.TargetingService.CountEnemyPack(aoeAction.Radius, context.Player);
        SetAoEDpsEnemyCount(context, pack.AoeRange);
        SetAoEDpsEngagedCount(context, pack.Engaged);
        if (pack.AoeRange < AoEMinTargets(context)) { SetAoEDpsState(context, $"{pack.AoeRange} < {AoEMinTargets(context)} min"); return; }

        var targetId = aoeAction.TargetType == ActionTargetType.Self
            ? context.Player.GameObjectId
            : FindBestAoETarget(context, aoeAction);
        if (targetId == 0) return;

        var capturedAction = aoeAction;
        var capturedEnemyCount = pack.AoeRange;
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
        if (MechanicCastGate.ShouldBlock(context, stCastTime)) { SetDpsState(context, "Holding: mechanic imminent"); return; }

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
