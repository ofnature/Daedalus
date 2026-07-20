using System;
using Daedalus.Config;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.AthenaCore.Abilities;
using Daedalus.Rotation.AthenaCore.Context;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Services.Training;

namespace Daedalus.Rotation.AthenaCore.Modules.Healing;

public sealed class IndomitabilityHandler : IHealingHandler
{
    public int Priority => 25;
    public string Name => "Indomitability";

    private static readonly string[] _indomitabilityAlternatives =
    {
        "Succor (GCD, adds shields)",
        "Whispering Dawn (fairy HoT)",
        "Fey Blessing (fairy burst)",
    };

    public void CollectCandidates(IAthenaContext context, RotationScheduler scheduler, bool isMoving)
    {
        var config = context.Configuration.Scholar;
        var player = context.Player;

        if (!config.EnableIndomitability) return;
        if (player.Level < SCHActions.Indomitability.MinLevel) return;
        if (!context.ActionService.IsActionReady(SCHActions.Indomitability.ActionId)) return;

        // AoE emergency ("everyone to 1 HP" recovery): gates, the Aetherflow reserve, and the
        // cross-healer reservation must not hold back the instant AoE heal — see AoEEmergencyHelper.
        // A stack (or Recitation) is still required to cast at all.
        var emergency = AoEEmergencyHelper.IsAoEEmergency(
            context.PartyHelper, player, context.Configuration.Healing);

        var hasRecitation = context.StatusHelper.HasRecitation(player);
        var stackFloor = emergency ? 0 : config.AetherflowReserve;
        if (!hasRecitation && context.AetherflowService.CurrentStacks <= stackFloor) return;

        var (avgHp, _, injuredCount) = context.PartyHelper.CalculatePartyHealthMetrics(player);
        if (!emergency)
        {
            var minTargets = AoEHealTargetHelper.GetEffectiveMinTargets(
                context.Configuration.Healing, context.PartyHelper.GetPartySize(player));
            if (avgHp > config.AoEHealThreshold || injuredCount < minTargets) return;
        }

        var action = SCHActions.Indomitability;

        if (!context.HealingCoordination.TryReserveAoEHeal(
            context.PartyCoordinationService, action.ActionId, action.HealPotency, 0, force: emergency))
        {
            context.Debug.IndomitabilityState = "Skipped (remote AOE reserved)";
            return;
        }

        var capturedAvgHp = avgHp;
        var capturedInjuredCount = injuredCount;
        var capturedHasRecitation = hasRecitation;
        if (emergency)
            context.Debug.IndomitabilityState = "AoE EMERGENCY";

        // Emergency priority 8: above Excogitation (15) / Lustrate (20) single-target triage.
        scheduler.PushOgcd(AthenaAbilities.Indomitability, player.GameObjectId,
            priority: emergency ? 8 : Priority,
            onDispatched: _ =>
            {
                if (!capturedHasRecitation)
                    context.AetherflowService.ConsumeStack();

                context.Debug.PlannedAction = action.Name;
                context.Debug.PlanningState = "Indomitability";

                if (context.TrainingService?.IsTrainingEnabled == true)
                {
                    var shortReason = $"Indomitability - {capturedInjuredCount} injured, avg HP {capturedAvgHp:P0}";
                    var factors = new[]
                    {
                        $"Party avg HP: {capturedAvgHp:P0}",
                        $"Injured count: {capturedInjuredCount}",
                        capturedHasRecitation ? "Recitation active (guaranteed crit, free)" : $"Aetherflow stacks: {context.AetherflowService.CurrentStacks}/3",
                        "400 potency AoE heal",
                        "oGCD - can weave without clipping",
                    };

                    context.TrainingService.RecordDecision(new ActionExplanation
                    {
                        Timestamp = DateTime.UtcNow,
                        ActionId = action.ActionId,
                        ActionName = "Indomitability",
                        Category = "Healing",
                        TargetName = "Party",
                        ShortReason = shortReason,
                        DetailedReason = $"Indomitability to heal {capturedInjuredCount} party members at {capturedAvgHp:P0} average HP. 400 potency AoE heal, instant oGCD. {(capturedHasRecitation ? "Recitation made this free and guaranteed critical!" : $"Cost 1 Aetherflow stack ({context.AetherflowService.CurrentStacks}/3 remaining).")} Best used after raidwides when multiple party members are injured.",
                        Factors = factors,
                        Alternatives = _indomitabilityAlternatives,
                        Tip = "Indomitability is your primary AoE oGCD heal. Pair with Recitation for burst healing. Use after raidwides rather than before (shields go before, heals go after).",
                        ConceptId = SchConcepts.IndomitabilityUsage,
                        Priority = capturedAvgHp < 0.5f ? ExplanationPriority.High : ExplanationPriority.Normal,
                    });

                    context.TrainingService.RecordConceptApplication(SchConcepts.IndomitabilityUsage, wasSuccessful: true);
                }
            });
    }
}
