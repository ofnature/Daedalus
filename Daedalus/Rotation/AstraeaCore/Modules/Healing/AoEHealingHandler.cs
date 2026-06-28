using System;
using Daedalus.Config;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.ApolloCore.Helpers;
using Daedalus.Rotation.AstraeaCore.Abilities;
using Daedalus.Rotation.AstraeaCore.Context;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Services.Training;

namespace Daedalus.Rotation.AstraeaCore.Modules.Healing;

public sealed class AoEHealingHandler : IHealingHandler
{
    public int Priority => 30;
    public string Name => "AoEHealing";

    private static readonly string[] _alternatives =
    {
        "Celestial Opposition (oGCD, free)",
        "Earthly Star detonation (if placed)",
        "Horoscope detonation (if active)",
    };

    public bool TryExecute(IAstraeaContext context, bool isMoving) => false;

    public void CollectCandidates(IAstraeaContext context, RotationScheduler scheduler, bool isMoving)
    {
        if (isMoving) return;

        var config = context.Configuration.Astrologian;
        var player = context.Player;

        if (!config.EnableHelios && !config.EnableAspectedHelios) return;

        var (count, _) = context.PartyHelper.CountPartyMembersNeedingAoEHeal(player, 0);
        var (avgHp, _, _) = context.PartyHealthMetrics;

        var raidwideImminent = TimelineHelper.IsRaidwideImminent(
            context.TimelineService, context.BossMechanicDetector, context.Configuration, out _);

        var minTargets = AoEHealTargetHelper.GetEffectiveMinTargets(
            context.Configuration.Healing, context.PartyHelper.GetPartySize(player));
        var shouldUse = (avgHp <= config.AoEHealThreshold && count >= minTargets) || raidwideImminent;
        if (!shouldUse) return;

        // GCD-heal gating (RSR GCDHeal parity): with a co-healer present, defer non-critical AoE GCD heals
        // (Helios line) to oGCDs and the co-healer to keep DPS uptime. Healer-role aware — a Main healer
        // never defers; a Co healer defers to the Main. Unless the party is critical (below the
        // GCD-emergency threshold) or a raidwide is imminent.
        if (CoHealerAwarenessHelper.ShouldDeferGcdHeals(
                context.Configuration.Healing.HealerRole,
                config.RestrictGcdHealsWithCoHealer,
                context.CoHealerDetectionService?.HasCoHealer == true)
            && avgHp > context.Configuration.Healing.GcdEmergencyThreshold
            && !raidwideImminent)
            return;

        ActionDefinition? action = null;
        AbilityBehavior? behavior = null;

        if (config.EnableAspectedHelios && player.Level >= ASTActions.HeliosConjunction.MinLevel)
        {
            action = ASTActions.HeliosConjunction;
            behavior = AstraeaAbilities.HeliosConjunction;
        }
        else if (config.EnableAspectedHelios && player.Level >= ASTActions.AspectedHelios.MinLevel)
        {
            action = ASTActions.AspectedHelios;
            behavior = AstraeaAbilities.AspectedHelios;
        }
        else if (config.EnableHelios && player.Level >= ASTActions.Helios.MinLevel)
        {
            action = ASTActions.Helios;
            behavior = AstraeaAbilities.Helios;
        }

        if (action == null || behavior == null) return;

        var castTimeMs = (int)(action.CastTime * 1000);
        if (!context.HealingCoordination.TryReserveAoEHeal(
            context.PartyCoordinationService, action.ActionId, action.HealPotency, castTimeMs))
        {
            context.Debug.AoEHealState = "Skipped (remote AOE reserved)";
            return;
        }

        var capturedAction = action;

        scheduler.PushGcd(behavior, player.GameObjectId, priority: Priority,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = capturedAction.Name;
                context.Debug.AoEHealState = "Casting";

                if (context.TrainingService?.IsTrainingEnabled == true)
                {
                    var (avgHpDispatched, _, injured) = context.PartyHealthMetrics;
                    var hasRegen = capturedAction == ASTActions.AspectedHelios || capturedAction == ASTActions.HeliosConjunction;
                    var shortReason = $"{capturedAction.Name} - {injured} injured at {avgHpDispatched:P0}";
                    var factors = new[]
                    {
                        $"Party avg HP: {avgHpDispatched:P0}",
                        $"Injured count: {injured}",
                        $"Action: {capturedAction.Name}",
                        hasRegen ? "Includes 15s regen" : "Direct heal only",
                        "GCD heal - uses a GCD",
                    };

                    context.TrainingService.RecordDecision(new ActionExplanation
                    {
                        Timestamp = DateTime.UtcNow,
                        ActionId = capturedAction.ActionId,
                        ActionName = capturedAction.Name,
                        Category = "Healing",
                        TargetName = "Party",
                        ShortReason = shortReason,
                        DetailedReason = $"{capturedAction.Name} cast on {injured} injured party members at {avgHpDispatched:P0} average HP. {(hasRegen ? "Provides direct healing plus a 15s regen for sustained recovery." : "Direct healing with no regen.")} Remember: oGCD heals like Celestial Opposition are 'free' - use them first before GCD heals when possible!",
                        Factors = factors,
                        Alternatives = _alternatives,
                        Tip = hasRegen
                            ? "Aspected Helios/Helios Conjunction adds a regen - great value! But always check if oGCD heals can handle it first."
                            : "Basic Helios is pure healing. Consider using Aspected Helios for the regen if available.",
                        ConceptId = AstConcepts.AspectedHeliosUsage,
                        Priority = ExplanationPriority.Normal,
                    });

                    context.TrainingService?.RecordConceptApplication(AstConcepts.AspectedHeliosUsage, wasSuccessful: true, hasRegen ? "AoE heal with regen" : "AoE heal");
                }
            });
    }
}
