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

public sealed class HolosHandler : IHealingHandler
{
    private static readonly string[] _holosAlternatives =
    {
        "Ixochole (AoE heal, 30s CD)",
        "Kerachole (AoE regen + mit, 45s CD)",
        "Panhaima (AoE multi-hit shields, 120s CD)",
    };

    public int Priority => 30;
    public string Name => "Holos";

    public void CollectCandidates(IAsclepiusContext context, RotationScheduler scheduler, bool isMoving)
    {
        var config = context.Configuration.Sage;
        var player = context.Player;

        if (!config.EnableHolos) return;

        var partyCoord = context.PartyCoordinationService;
        var coordConfig = context.Configuration.PartyCoordination;
        if (coordConfig.EnableCooldownCoordination &&
            partyCoord?.WasPartyMitigationUsedRecently(coordConfig.CooldownOverlapWindowSeconds) == true)
        {
            context.Debug.HolosState = "Skipped (remote mit)";
            return;
        }

        if (coordConfig.EnableHealerBurstAwareness && coordConfig.DelayMitigationsDuringBurst && partyCoord != null)
        {
            var (avgHpCheck, _, _) = context.PartyHelper.CalculatePartyHealthMetrics(player);
            var burstState = partyCoord.GetBurstWindowState();
            if (burstState.IsActive && avgHpCheck > context.Configuration.Healing.GcdEmergencyThreshold)
            {
                context.Debug.HolosState = $"Delayed (burst active)";
                return;
            }
        }

        if (player.Level < SGEActions.Holos.MinLevel) return;
        if (!context.ActionService.IsActionReady(SGEActions.Holos.ActionId)) { context.Debug.HolosState = "On CD"; return; }

        var (avgHp, lowestHp, injuredCount) = AsclepiusPartyMetrics.GetAoEHealMetrics(context.PartyHelper, player);

        // Non-Addersgall emergency: when Addersgall is dry there is no Druochole/Taurochole to weave,
        // so let the free Holos cooldown (heal + shield + 10% mit) cover a critically low ally even if
        // the usual AoE injured-count isn't met (e.g. a single low tank in a dungeon).
        var addersgallEmergency = context.AddersgallStacks < 1
            && lowestHp <= context.Configuration.Healing.GcdEmergencyThreshold;

        // AoE emergency ("everyone to 1 HP" recovery): the panic button is exactly what the
        // 120s cooldown is saved for — bypass the gates and outrank single-target triage.
        var aoeEmergency = AsclepiusPartyMetrics.IsAoEEmergency(
            context.PartyHelper, player, context.Configuration.Healing);

        if (!addersgallEmergency && !aoeEmergency)
        {
            var minTargets = AoEHealTargetHelper.GetEffectiveMinTargets(
                context.Configuration.Healing, context.PartyHelper.GetPartySize(player));
            if (lowestHp > config.HolosThreshold) { context.Debug.HolosState = $"Lowest HP {lowestHp:P0}"; return; }
            if (injuredCount < minTargets) { context.Debug.HolosState = $"{injuredCount} injured"; return; }
        }

        var capturedAvgHp = avgHp;
        var capturedLowestHp = lowestHp;
        var capturedInjuredCount = injuredCount;
        var action = SGEActions.Holos;
        if (aoeEmergency)
            context.Debug.HolosState = "AoE EMERGENCY";

        scheduler.PushOgcd(AsclepiusAbilities.Holos, player.GameObjectId,
            priority: aoeEmergency ? AsclepiusPartyMetrics.AoEEmergencyPriority + 2 : Priority,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = action.Name;
                context.Debug.PlanningState = "Holos";
                context.Debug.HolosState = "Executing";
                partyCoord?.OnCooldownUsed(action.ActionId, 120_000);

                if (context.TrainingService?.IsTrainingEnabled == true)
                {
                    var shortReason = $"Holos - emergency heal ({capturedLowestHp:P0} lowest, {capturedInjuredCount} injured)";
                    var factors = new[]
                    {
                        $"Lowest HP: {capturedLowestHp:P0}",
                        $"Threshold: {config.HolosThreshold:P0}",
                        $"Injured count: {capturedInjuredCount}",
                        "300 potency heal + shield + 10% mit (20s)",
                        "120s cooldown - big emergency button",
                    };

                    context.TrainingService.RecordDecision(new ActionExplanation
                    {
                        Timestamp = DateTime.UtcNow,
                        ActionId = action.ActionId,
                        ActionName = "Holos",
                        Category = "Healing",
                        TargetName = "Party",
                        ShortReason = shortReason,
                        DetailedReason = $"Holos used as emergency response. Party at {capturedAvgHp:P0} avg HP with lowest at {capturedLowestHp:P0}. Provides 300 potency heal + 300 potency shield + 10% damage reduction for 20 seconds. This is SGE's panic button - save it for real emergencies!",
                        Factors = factors,
                        Alternatives = _holosAlternatives,
                        Tip = "Holos is your 2-minute panic button! It does everything: heals, shields, AND mitigates. Save it for when things go wrong, or use proactively for massive incoming damage you know about.",
                        ConceptId = SgeConcepts.HolosUsage,
                        Priority = ExplanationPriority.Critical,
                    });
                }
            });
    }
}
