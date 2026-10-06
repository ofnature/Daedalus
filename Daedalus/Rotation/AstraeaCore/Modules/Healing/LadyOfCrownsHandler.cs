using Daedalus.Services.Action;
using System;
using Daedalus.Config;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.AstraeaCore.Abilities;
using Daedalus.Rotation.AstraeaCore.Context;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Services.Training;

namespace Daedalus.Rotation.AstraeaCore.Modules.Healing;

public sealed class LadyOfCrownsHandler : IHealingHandler
{
    public int Priority => 60;
    public string Name => "LadyOfCrowns";

    private static readonly string[] _alternatives =
    {
        "Lord of Crowns (400 potency damage)",
        "Celestial Opposition (if available)",
        "Save Lady for bigger emergency",
    };

    public bool TryExecute(IAstraeaContext context, bool isMoving) => false;

    /// <summary>
    /// When to play Lady. Every strategy plays her for a real group heal (party under the threshold, 2+
    /// injured) — she used to fire only under EmergencyOnly, so on the default (OnCooldown) she never did.
    /// Outside EmergencyOnly she is also played, if anyone is hurt, before the next draw replaces her.
    /// </summary>
    internal static bool ShouldPlayLady(MinorArcanaUsageStrategy strategy, float avgHp, float threshold,
        int injured, float drawCooldownRemaining, float expireBeforeDrawSeconds)
    {
        if (avgHp <= threshold && injured >= 2)
            return true;
        return strategy != MinorArcanaUsageStrategy.EmergencyOnly
               && injured >= 1
               && drawCooldownRemaining <= expireBeforeDrawSeconds;
    }

    public void CollectCandidates(IAstraeaContext context, RotationScheduler scheduler, bool isMoving)
    {
        var config = context.Configuration.Astrologian;
        var player = context.Player;

        if (!config.EnableMinorArcana) return;
        if (!context.CardService.HasLady) return;
        if (!ActionAvailability.MeetsLevelAndLearned(player.Level, context.ActionService, ASTActions.LadyOfCrowns)) return;

        var (avgHp, _, injured) = context.PartyHealthMetrics;
        if (!ShouldPlayLady(config.MinorArcanaStrategy, avgHp, config.LadyOfCrownsThreshold, injured,
                context.CardService.GetDrawCooldownRemaining(), config.ExpireCardsBeforeDrawSeconds))
            return;

        var action = ASTActions.LadyOfCrowns;
        var capturedAvgHp = avgHp;
        var capturedInjured = injured;

        scheduler.PushOgcd(AstraeaAbilities.LadyOfCrowns, player.GameObjectId, priority: Priority,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = action.Name;
                context.Debug.CardState = "Lady Used";

                if (context.TrainingService?.IsTrainingEnabled == true)
                {
                    var shortReason = $"Lady of Crowns - emergency AoE heal at {capturedAvgHp:P0}";
                    var factors = new[]
                    {
                        $"Party avg HP: {capturedAvgHp:P0}",
                        $"Injured count: {capturedInjured}",
                        $"Threshold: {config.LadyOfCrownsThreshold:P0}",
                        "400 potency AoE heal",
                        "Uses Minor Arcana card",
                    };

                    context.TrainingService.RecordDecision(new ActionExplanation
                    {
                        Timestamp = DateTime.UtcNow,
                        ActionId = action.ActionId,
                        ActionName = "Lady of Crowns",
                        Category = "Healing",
                        TargetName = "Party",
                        ShortReason = shortReason,
                        DetailedReason = $"Lady of Crowns used for emergency AoE healing. Party at {capturedAvgHp:P0} average HP with {capturedInjured} injured. Lady provides 400 potency AoE heal - saving this from Minor Arcana instead of using Lord for damage is a healing gain when the party needs it!",
                        Factors = factors,
                        Alternatives = _alternatives,
                        Tip = "Minor Arcana gives either Lord (damage) or Lady (heal). Lady is free AoE healing when you need it! In farm content, you might always use Lord for damage, but in prog or hard content, Lady can save the day.",
                        ConceptId = AstConcepts.MinorArcanaUsage,
                        Priority = ExplanationPriority.High,
                    });

                    context.TrainingService?.RecordConceptApplication(AstConcepts.MinorArcanaUsage, wasSuccessful: true, "Lady of Crowns emergency AoE heal");
                }
            });
    }
}
