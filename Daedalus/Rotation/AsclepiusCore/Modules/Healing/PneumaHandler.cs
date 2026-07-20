using System;
using Daedalus.Config;
using Daedalus.Data;
using Daedalus.Rotation.AsclepiusCore.Abilities;
using Daedalus.Rotation.AsclepiusCore.Context;
using Daedalus.Rotation.AsclepiusCore.Helpers;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Services.Training;

namespace Daedalus.Rotation.AsclepiusCore.Modules.Healing;

public sealed class PneumaHandler : IHealingHandler
{
    private static readonly string[] _pneumaAlternatives =
    {
        "Save for better timing",
        "Use for pure DPS (skip if party healthy)",
        "Ixochole + Dosis (separate heal and damage)",
    };

    public int Priority => 10;
    public string Name => "Pneuma";

    public void CollectCandidates(IAsclepiusContext context, RotationScheduler scheduler, bool isMoving)
    {
        if (isMoving) return;

        var config = context.Configuration.Sage;
        var player = context.Player;

        if (!config.EnablePneuma) return;
        if (player.Level < SGEActions.Pneuma.MinLevel) { context.Debug.PneumaState = "Level too low"; return; }
        if (!context.ActionService.IsActionReady(SGEActions.Pneuma.ActionId)) { context.Debug.PneumaState = "On CD"; return; }

        var enemy = context.TargetingService.FindEnemy(
            context.Configuration.Targeting.EnemyStrategy,
            SGEActions.Pneuma.Range,
            player);
        if (enemy == null) { context.Debug.PneumaState = "No enemy"; return; }

        var (avgHp, _, injuredCount) = AsclepiusPartyMetrics.GetAoEHealMetrics(context.PartyHelper, player);

        // AoE emergency ("everyone to 1 HP" recovery): 600-potency party heal + damage in one
        // GCD — the best possible recovery GCD, so it outranks Prognosis and the damage queue.
        var emergency = AsclepiusPartyMetrics.IsAoEEmergency(
            context.PartyHelper, player, context.Configuration.Healing);
        if (!emergency)
        {
            var minTargets = AoEHealTargetHelper.GetEffectiveMinTargets(
                context.Configuration.Healing, context.PartyHelper.GetPartySize(player));
            if (avgHp > config.PneumaThreshold && injuredCount < minTargets)
            {
                context.Debug.PneumaState = $"Party HP {avgHp:P0}";
                return;
            }
        }

        var capturedAvgHp = avgHp;
        var capturedInjuredCount = injuredCount;
        if (emergency)
            context.Debug.PneumaState = "AoE EMERGENCY";

        scheduler.PushGcd(AsclepiusAbilities.Pneuma, enemy.GameObjectId,
            priority: emergency ? AsclepiusPartyMetrics.AoEEmergencyPriority : Priority,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = SGEActions.Pneuma.Name;
                context.Debug.PlanningState = "Pneuma";
                context.Debug.PneumaState = "Executing";

                if (context.TrainingService?.IsTrainingEnabled == true)
                {
                    var shortReason = $"Pneuma - {capturedInjuredCount} injured, enemy in range";
                    var factors = new[]
                    {
                        $"Party avg HP: {capturedAvgHp:P0}",
                        $"Injured count: {capturedInjuredCount}",
                        "330 potency damage line AoE",
                        "600 potency party heal",
                        "120s cooldown",
                    };

                    context.TrainingService.RecordDecision(new ActionExplanation
                    {
                        Timestamp = DateTime.UtcNow,
                        ActionId = SGEActions.Pneuma.ActionId,
                        ActionName = "Pneuma",
                        Category = "Healing",
                        TargetName = "Enemy/Party",
                        ShortReason = shortReason,
                        DetailedReason = $"Pneuma used with {capturedInjuredCount} injured party members and enemy in range. Deals 330 potency damage in a line AND heals party for 600 potency. This is SGE's signature ability - massive healing that also does damage! Perfect timing when party needs healing and you can hit enemies.",
                        Factors = factors,
                        Alternatives = _pneumaAlternatives,
                        Tip = "Pneuma is INSANE value when you need healing! It's a 600 potency party heal that ALSO deals damage. Time it so you can hit enemies while the party needs healing. Don't hold it too long - 2 minute cooldown is still short!",
                        ConceptId = SgeConcepts.PneumaUsage,
                        Priority = ExplanationPriority.High,
                    });
                }
            });
    }
}
