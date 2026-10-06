using Daedalus.Services.Action;
using System;
using Daedalus.Config;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.AstraeaCore.Abilities;
using Daedalus.Rotation.AstraeaCore.Context;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Services.Training;

namespace Daedalus.Rotation.AstraeaCore.Modules.Healing;

public sealed class AspectedBeneficHandler : IHealingHandler
{
    public int Priority => 40;
    public string Name => "AspectedBenefic";

    private static readonly string[] _alternatives =
    {
        "Essential Dignity (oGCD, emergency)",
        "Celestial Intersection (oGCD)",
        "Benefic II (higher potency, has cast time)",
    };

    public bool TryExecute(IAstraeaContext context, bool isMoving) => false;

    public void CollectCandidates(IAstraeaContext context, RotationScheduler scheduler, bool isMoving)
    {
        var config = context.Configuration.Astrologian;
        var player = context.Player;

        if (!config.EnableAspectedBenefic) return;
        if (!ActionAvailability.MeetsLevelAndLearned(player.Level, context.ActionService, ASTActions.AspectedBenefic)) return;

        var target = context.PartyHelper.FindLowestHpPartyMember(player);
        if (target == null) return;
        if (context.HealingCoordination.IsTargetReserved(target.EntityId, context.PartyCoordinationService)) return;

        var hpPercent = context.PartyHelper.GetHpPercent(target);
        var effectiveThreshold = DynamicRegenThresholdHelper.GetEffectiveThreshold(
            context.Configuration.Healing, context.DamageIntakeService, config.AspectedBeneficThreshold);
        if (hpPercent > effectiveThreshold) return;
        if (context.StatusHelper.HasAspectedBenefic(target)) return;

        var action = ASTActions.AspectedBenefic;
        var capturedTarget = target;
        var capturedHpPercent = hpPercent;

        scheduler.PushGcd(AstraeaAbilities.AspectedBenefic, target.GameObjectId, priority: Priority,
            onDispatched: _ =>
            {
                var healAmount = action.HealPotency * 10;
                context.HealingCoordination.TryReserveTarget(
                    capturedTarget.EntityId, context.PartyCoordinationService, healAmount, action.ActionId, 0);

                context.Debug.PlannedAction = action.Name;
                context.Debug.SingleHealState = "Aspected Benefic";

                if (context.TrainingService?.IsTrainingEnabled == true)
                {
                    var targetName = capturedTarget.Name?.TextValue ?? "Unknown";
                    var shortReason = $"Aspected Benefic on {targetName} at {capturedHpPercent:P0}";
                    var factors = new[]
                    {
                        $"Target HP: {capturedHpPercent:P0}",
                        $"Threshold: {config.AspectedBeneficThreshold:P0}",
                        "Instant cast (can use while moving!)",
                        "250 potency heal + 15s regen",
                        "Target didn't have regen already",
                    };

                    context.TrainingService.RecordDecision(new ActionExplanation
                    {
                        Timestamp = DateTime.UtcNow,
                        ActionId = action.ActionId,
                        ActionName = "Aspected Benefic",
                        Category = "Healing",
                        TargetName = targetName,
                        ShortReason = shortReason,
                        DetailedReason = $"Aspected Benefic on {targetName} at {capturedHpPercent:P0} HP. Instant cast GCD heal (250 potency) plus a 15s regen. Great for healing on the move! Target didn't already have the regen, so full value.",
                        Factors = factors,
                        Alternatives = _alternatives,
                        Tip = "Aspected Benefic is instant cast - your go-to heal while moving! The regen is great value. Check that the target doesn't already have the regen before refreshing.",
                        ConceptId = AstConcepts.AspectedBeneficUsage,
                        Priority = ExplanationPriority.Normal,
                    });

                    context.TrainingService?.RecordConceptApplication(AstConcepts.AspectedBeneficUsage, wasSuccessful: true, "Instant heal with regen applied");
                }
            });
    }
}
