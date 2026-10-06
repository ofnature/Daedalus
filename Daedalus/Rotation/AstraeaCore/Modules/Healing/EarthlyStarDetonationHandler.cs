using Daedalus.Services.Action;
using System;
using Daedalus.Config;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.ApolloCore.Helpers;
using Daedalus.Rotation.AstraeaCore.Abilities;
using Daedalus.Rotation.AstraeaCore.Context;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Services.Training;

namespace Daedalus.Rotation.AstraeaCore.Modules.Healing;

public sealed class EarthlyStarDetonationHandler : IHealingHandler
{
    public int Priority => 40;
    public string Name => "EarthlyStarDetonation";

    public bool TryExecute(IAstraeaContext context, bool isMoving) => false;

    /// <summary>
    /// Injured count that detonates: the configured count, but never more than the party-size rule allows
    /// (2 in a 4-man).
    /// </summary>
    internal static int MinInjuredToDetonate(int configured, int partyRule) => Math.Min(configured, partyRule);

    public void CollectCandidates(IAstraeaContext context, RotationScheduler scheduler, bool isMoving)
    {
        var config = context.Configuration.Astrologian;
        var player = context.Player;

        if (!config.EnableEarthlyStar) return;
        if (!context.IsStarPlaced) return;
        if (!ActionAvailability.MeetsLevelAndLearned(player.Level, context.ActionService, ASTActions.StellarDetonation)) return;
        if (!context.ActionService.IsActionReady(ASTActions.StellarDetonation.ActionId)) return;

        var (avgHp, _, injured) = context.PartyHealthMetrics;
        bool isMature = context.IsStarMature;

        // CLAUDE.md: never require 3 injured in 4-man content. Same party-size rule as the AoE heals.
        var minInjured = MinInjuredToDetonate(
            config.EarthlyStarMinTargets,
            Daedalus.Rotation.Common.Helpers.AoEHealTargetHelper.GetEffectiveMinTargets(
                context.Configuration.Healing, context.PartyHelper.GetPartySize(player)));

        var raidwideImminent = TimelineHelper.IsRaidwideImminent(
            context.TimelineService, context.BossMechanicDetector, context.Configuration, out _);

        bool shouldDetonate = false;

        if (isMature)
        {
            if (avgHp <= config.EarthlyStarDetonateThreshold || injured >= minInjured || raidwideImminent)
                shouldDetonate = true;
        }
        else if (!config.WaitForGiantDominance)
        {
            if (avgHp <= config.EarthlyStarDetonateThreshold || injured >= minInjured || raidwideImminent)
                shouldDetonate = true;
        }
        else
        {
            if (avgHp <= config.EarthlyStarEmergencyThreshold)
                shouldDetonate = true;
        }

        if (!shouldDetonate) return;

        var action = ASTActions.StellarDetonation;
        var capturedAvgHp = avgHp;
        var capturedInjured = injured;
        var capturedIsMature = isMature;
        var capturedRaidwideImminent = raidwideImminent;

        scheduler.PushOgcd(AstraeaAbilities.StellarDetonation, player.GameObjectId, priority: Priority,
            onDispatched: _ =>
            {
                context.EarthlyStarService.OnStarDetonated();

                context.Debug.PlannedAction = action.Name;
                context.Debug.EarthlyStarState = capturedIsMature ? "Detonated (Mature)" : "Detonated (Immature)";
                context.LogEarthlyStarDecision("Detonated", capturedIsMature ? "Mature star, party needs healing" : "Emergency detonate");

                if (context.TrainingService?.IsTrainingEnabled == true)
                {
                    string trigger;
                    if (capturedRaidwideImminent) trigger = "Raidwide imminent";
                    else if (capturedAvgHp <= config.EarthlyStarEmergencyThreshold) trigger = "Emergency HP";
                    else trigger = $"Party HP low ({capturedAvgHp:P0})";

                    var shortReason = capturedIsMature
                        ? $"Giant Dominance detonated - {trigger}"
                        : $"Immature Star detonated - {trigger}";

                    var factors = new[]
                    {
                        capturedIsMature ? "Star MATURE (Giant Dominance)" : "Star immature",
                        $"Party avg HP: {capturedAvgHp:P0}",
                        $"Injured count: {capturedInjured}",
                        trigger,
                        capturedIsMature ? "720 potency heal + 720 damage" : "360 potency heal + 360 damage",
                    };

                    var alternatives = new[]
                    {
                        capturedIsMature ? "Detonation is optimal when mature" : "Wait for maturation (if safe)",
                        "Let star expire naturally",
                        "Use other oGCDs first",
                    };

                    context.TrainingService.RecordDecision(new ActionExplanation
                    {
                        Timestamp = DateTime.UtcNow,
                        ActionId = action.ActionId,
                        ActionName = "Stellar Detonation",
                        Category = "Healing",
                        TargetName = "Party",
                        ShortReason = shortReason,
                        DetailedReason = $"Detonated Earthly Star ({(capturedIsMature ? "Giant Dominance, 720 potency" : "immature, 360 potency")}). Party avg HP at {capturedAvgHp:P0} with {capturedInjured} injured. {trigger}. {(capturedIsMature ? "Mature star provides maximum healing value!" : "Detonated early due to urgent healing need.")}",
                        Factors = factors,
                        Alternatives = alternatives,
                        Tip = capturedIsMature
                            ? "Perfect timing! Mature Earthly Star is AST's biggest AoE heal. Always aim for Giant Dominance when possible."
                            : "Sometimes you have to detonate early. An immature heal is better than letting the party die!",
                        ConceptId = AstConcepts.EarthlyStarMaturation,
                        Priority = capturedIsMature ? ExplanationPriority.High : ExplanationPriority.Normal,
                    });

                    context.TrainingService?.RecordConceptApplication(AstConcepts.EarthlyStarMaturation, wasSuccessful: capturedIsMature, capturedIsMature ? "Giant Dominance detonation" : "Early detonation");
                }
            });
    }
}
