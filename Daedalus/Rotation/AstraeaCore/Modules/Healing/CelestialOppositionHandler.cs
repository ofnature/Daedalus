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

public sealed class CelestialOppositionHandler : IHealingHandler
{
    public int Priority => 20;
    public string Name => "CelestialOpposition";

    private static readonly string[] _alternatives =
    {
        "Earthly Star (higher potency if mature)",
        "Helios Conjunction (GCD AoE)",
        "Save for predictable raidwide",
    };

    public bool TryExecute(IAstraeaContext context, bool isMoving) => false;

    public void CollectCandidates(IAstraeaContext context, RotationScheduler scheduler, bool isMoving)
    {
        var config = context.Configuration.Astrologian;
        var player = context.Player;

        if (!config.EnableCelestialOpposition) return;
        if (player.Level < ASTActions.CelestialOpposition.MinLevel) return;
        if (!context.ActionService.IsActionReady(ASTActions.CelestialOpposition.ActionId)) return;

        var (count, _) = context.PartyHelper.CountPartyMembersNeedingAoEHeal(player, 0);
        var (avgHp, _, _) = context.PartyHealthMetrics;

        var raidwideImminent = TimelineHelper.IsRaidwideImminent(
            context.TimelineService, context.BossMechanicDetector, context.Configuration, out _);

        var minTargets = AoEHealTargetHelper.GetEffectiveMinTargets(
            context.Configuration.Healing, context.PartyHelper.GetPartySize(player));

        // AoE emergency ("everyone to 1 HP" recovery): the instant AoE heal + regen must fire
        // through the gates and the cross-healer reservation — see AoEEmergencyHelper.
        var emergency = AoEEmergencyHelper.IsAoEEmergency(
            context.PartyHelper, player, context.Configuration.Healing);

        var shouldUse = (avgHp <= config.AoEHealThreshold && count >= minTargets) || raidwideImminent || emergency;
        if (!shouldUse) return;

        var action = ASTActions.CelestialOpposition;
        if (!context.HealingCoordination.TryReserveAoEHeal(
            context.PartyCoordinationService, action.ActionId, action.HealPotency, 0, force: emergency))
        {
            context.Debug.CelestialOppositionState = "Skipped (remote AOE reserved)";
            return;
        }

        if (emergency)
            context.Debug.CelestialOppositionState = "AoE EMERGENCY";

        // Emergency priority 8: above Essential Dignity (10) / Celestial Intersection (15).
        scheduler.PushOgcd(AstraeaAbilities.CelestialOpposition, player.GameObjectId,
            priority: emergency ? 8 : Priority,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = action.Name;
                context.Debug.CelestialOppositionState = "Used";

                if (context.TrainingService?.IsTrainingEnabled == true)
                {
                    var (avgHpDispatched, _, injured) = context.PartyHealthMetrics;
                    var shortReason = $"Celestial Opposition - {injured} injured at {avgHpDispatched:P0} avg";
                    var factors = new[]
                    {
                        $"Party avg HP: {avgHpDispatched:P0}",
                        $"Injured count: {injured}",
                        "200 potency heal + 15s regen",
                        "60s cooldown",
                        "oGCD - free healing",
                    };

                    context.TrainingService.RecordDecision(new ActionExplanation
                    {
                        Timestamp = DateTime.UtcNow,
                        ActionId = action.ActionId,
                        ActionName = "Celestial Opposition",
                        Category = "Healing",
                        TargetName = "Party",
                        ShortReason = shortReason,
                        DetailedReason = $"Celestial Opposition used on {injured} injured party members at {avgHpDispatched:P0} average HP. Provides 200 potency instant heal plus a 15s regen (100 potency/tick). Free oGCD AoE heal on 60s cooldown - excellent value!",
                        Factors = factors,
                        Alternatives = _alternatives,
                        Tip = "Celestial Opposition is a free AoE heal + regen. Use it liberally! The regen continues ticking even while you DPS. Don't hold it for emergencies - that's what Essential Dignity is for.",
                        ConceptId = AstConcepts.CelestialOppositionUsage,
                        Priority = ExplanationPriority.Normal,
                    });

                    context.TrainingService?.RecordConceptApplication(AstConcepts.CelestialOppositionUsage, wasSuccessful: true, "AoE heal and regen applied");
                }
            });
    }
}
