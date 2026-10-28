using System;
using Daedalus.Config;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.ApolloCore.Helpers;
using Daedalus.Rotation.AthenaCore.Abilities;
using Daedalus.Rotation.AthenaCore.Context;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Services.Training;

namespace Daedalus.Rotation.AthenaCore.Modules.Healing;

public sealed class RecitationHandler : IHealingHandler
{
    public int Priority => 10;
    public string Name => "Recitation";

    private static readonly string[] _recitationAlternatives =
    {
        "Use Recitation with different follow-up",
        "Save for emergency (guaranteed crit heal)",
        "Hold for raidwide (Recitation + Indom)",
    };

    public void CollectCandidates(IAthenaContext context, RotationScheduler scheduler, bool isMoving)
    {
        var config = context.Configuration.Scholar;
        var player = context.Player;

        if (!config.EnableRecitation) return;
        if (player.Level < SCHActions.Recitation.MinLevel) return;
        if (context.StatusHelper.HasRecitation(player)) return;
        if (!context.ActionService.IsActionReady(SCHActions.Recitation.ActionId)) return;

        bool shouldUseRecitation = config.RecitationPriority switch
        {
            RecitationPriority.Excogitation => ShouldUseExcogitation(context),
            RecitationPriority.Indomitability => ShouldUseIndomitability(context),
            RecitationPriority.Adloquium => ShouldUseSingleTargetHeal(context),
            RecitationPriority.Succor => ShouldUseAoEHeal(context),
            _ => false
        };

        if (!shouldUseRecitation) return;

        var action = SCHActions.Recitation;

        scheduler.PushOgcd(AthenaAbilities.Recitation, player.GameObjectId, priority: Priority,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = action.Name;
                context.Debug.PlanningState = "Recitation";

                if (context.TrainingService?.IsTrainingEnabled == true)
                {
                    var followUp = config.RecitationPriority switch
                    {
                        RecitationPriority.Excogitation => "Excogitation",
                        RecitationPriority.Indomitability => "Indomitability",
                        RecitationPriority.Adloquium => "Adloquium",
                        RecitationPriority.Succor => "Succor",
                        _ => "Unknown"
                    };

                    var shortReason = $"Recitation for guaranteed crit {followUp}";
                    var factors = new[]
                    {
                        $"Next ability: {followUp} (configured priority)",
                        "Guarantees critical heal on next applicable spell",
                        "No Aetherflow cost when paired with Aetherflow abilities",
                        $"Aetherflow stacks: {context.AetherflowService.CurrentStacks}/3",
                    };

                    context.TrainingService.RecordDecision(new ActionExplanation
                    {
                        Timestamp = DateTime.UtcNow,
                        ActionId = action.ActionId,
                        ActionName = "Recitation",
                        Category = "Healing",
                        TargetName = null,
                        ShortReason = shortReason,
                        DetailedReason = $"Recitation guarantees a critical heal on the next Adloquium, Succor, Indomitability, or Excogitation. Also removes Aetherflow cost. Planning to follow with {followUp} for maximum value.",
                        Factors = factors,
                        Alternatives = _recitationAlternatives,
                        Tip = "Recitation is best used before Excogitation (crit Excog) or before raidwides with Indomitability. The free Aetherflow cost is a nice bonus!",
                        ConceptId = SchConcepts.RecitationUsage,
                        Priority = ExplanationPriority.Normal,
                    });

                    context.TrainingService.RecordConceptApplication(SchConcepts.RecitationUsage, wasSuccessful: true);
                }
            });
    }

    private static bool ShouldUseExcogitation(IAthenaContext context)
    {
        var config = context.Configuration.Scholar;
        var player = context.Player;

        // Only when the follow-up can go out inside the 15s buff: Recitation (60s) and Excog (45s) drift apart
        // (RSR ShouldUseRecitation, the branch RSR 7f857722 fixed).
        if (!config.EnableExcogitation) return false;
        if (player.Level < SCHActions.Excogitation.MinLevel) return false;
        if (!context.ActionService.IsActionReady(SCHActions.Excogitation.ActionId)) return false;

        var target = context.PartyHelper.FindExcogitationTarget(player);
        if (target == null) return false;

        var hpPercent = context.PartyHelper.GetHpPercent(target);
        return hpPercent <= config.ExcogitationThreshold;
    }

    private static bool ShouldUseIndomitability(IAthenaContext context)
    {
        var config = context.Configuration.Scholar;
        var player = context.Player;

        if (!config.EnableIndomitability) return false;
        if (player.Level < SCHActions.Indomitability.MinLevel) return false;
        if (!context.ActionService.IsActionReady(SCHActions.Indomitability.ActionId)) return false;

        var (avgHp, _, injuredCount) = context.PartyHelper.CalculatePartyHealthMetrics(player);
        var minTargets = AoEHealTargetHelper.GetEffectiveMinTargets(
            context.Configuration.Healing, context.PartyHelper.GetPartySize(player));
        return avgHp <= config.AoEHealThreshold && injuredCount >= minTargets;
    }

    private static bool ShouldUseSingleTargetHeal(IAthenaContext context)
    {
        var config = context.Configuration.Scholar;
        var player = context.Player;

        var target = context.PartyHelper.FindLowestHpPartyMember(player);
        if (target == null) return false;

        var hpPercent = context.PartyHelper.GetHpPercent(target);
        return hpPercent <= config.AdloquiumThreshold;
    }

    private static bool ShouldUseAoEHeal(IAthenaContext context)
    {
        var config = context.Configuration.Scholar;
        var player = context.Player;

        var (count, _) = context.PartyHelper.CountPartyMembersNeedingAoEHeal(player, 0);
        var (avgHp, _, _) = context.PartyHelper.CalculatePartyHealthMetrics(player);

        var raidwideImminent = TimelineHelper.IsRaidwideImminent(
            context.TimelineService, context.BossMechanicDetector, context.Configuration, out _);

        var minTargets = AoEHealTargetHelper.GetEffectiveMinTargets(
            context.Configuration.Healing, context.PartyHelper.GetPartySize(player));
        return (avgHp <= config.AoEHealThreshold && count >= minTargets) || raidwideImminent;
    }
}
