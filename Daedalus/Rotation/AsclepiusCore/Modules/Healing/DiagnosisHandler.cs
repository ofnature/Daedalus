using System;
using Dalamud.Game.ClientState.Objects.Types;
using Daedalus.Config;
using Daedalus.Data;
using Daedalus.Rotation.AsclepiusCore.Abilities;
using Daedalus.Rotation.AsclepiusCore.Context;
using Daedalus.Rotation.AsclepiusCore.Helpers;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Services.Training;

namespace Daedalus.Rotation.AsclepiusCore.Modules.Healing;

public sealed class DiagnosisHandler : IHealingHandler
{
    public int Priority => 40;
    public string Name => "Diagnosis";

    public void CollectCandidates(IAsclepiusContext context, RotationScheduler scheduler, bool isMoving)
    {
        // Swiftcast makes Diagnosis instant, so it can still fire as an emergency heal while moving.
        if (isMoving && !context.HasSwiftcast) return;

        var config = context.Configuration.Sage;
        if (!config.EnableDiagnosis) return;

        var player = context.Player;

        var target = context.Configuration.Healing.UseDamageIntakeTriage
            ? context.PartyHelper.FindMostEndangeredPartyMember(
                player, context.DamageIntakeService, 0, context.DamageTrendService, context.ShieldTrackingService)
            : context.PartyHelper.FindLowestHpPartyMember(player);
        if (target == null) return;

        if (context.HealingCoordination.IsTargetReserved(target.EntityId, context.PartyCoordinationService)) return;

        var hpPercent = target.MaxHp > 0 ? (float)target.CurrentHp / target.MaxHp : 1f;
        // HoT-aware threshold (RSR parity): relax when the target is already covered by a regen.
        var diagnosisThreshold = AsclepiusStatusHelper.ApplyHotAwareness(config.DiagnosisThreshold, target);
        if (hpPercent > diagnosisThreshold) return;

        // GCD-heal gating: with a co-healer covering the party, leave non-critical single-target
        // healing to oGCDs (and the co-healer) and keep DPS uptime. Critical targets still get a
        // GCD heal (the check below this only fires above the GCD-emergency threshold).
        if (CoHealerAwarenessHelper.ShouldDeferGcdHeals(
                context.Configuration.Healing.HealerRole,
                config.RestrictGcdHealsWithCoHealer,
                context.CoHealerDetectionService?.HasCoHealer == true)
            && hpPercent > context.Configuration.Healing.GcdEmergencyThreshold)
        {
            return;
        }

        // Diagnosis is a damage-GCD fallback. Whenever a free, MP-restoring Addersgall oGCD
        // (Druochole / Taurochole) can actually cover this target, defer to it: those heals are
        // strictly better and preserve DPS uptime. Only hardcast Diagnosis once the target is
        // critical (at/below the GCD-emergency threshold, where a weave is no longer a safe bet) or
        // Addersgall is unavailable for this target.
        if (hpPercent > context.Configuration.Healing.GcdEmergencyThreshold
            && OgcdWillCover(context, target, hpPercent))
        {
            return;
        }

        if (CoHealerAwarenessHelper.CoHealerWillCover(
                context.Configuration.Healing.EnableCoHealerAwareness,
                context.CoHealerDetectionService,
                target,
                context.Configuration.Healing.CoHealerPendingHealThreshold))
            return;

        var action = SGEActions.Diagnosis;
        var capturedTarget = target;
        var capturedHpPercent = hpPercent;
        var capturedHealAmount = action.HealPotency * 10;
        var capturedCastTimeMs = (int)(action.CastTime * 1000);

        scheduler.PushGcd(AsclepiusAbilities.Diagnosis, target.GameObjectId, priority: Priority,
            onDispatched: _ =>
            {
                context.HealingCoordination.TryReserveTarget(
                    capturedTarget.EntityId, context.PartyCoordinationService, capturedHealAmount, action.ActionId, capturedCastTimeMs);

                context.Debug.PlannedAction = action.Name;
                context.Debug.PlanningState = "Diagnosis";
                context.LogHealDecision(capturedTarget.Name?.TextValue ?? "Unknown", capturedHpPercent, action.Name, action.HealPotency, "Low HP");

                if (context.TrainingService?.IsTrainingEnabled == true)
                {
                    var targetName = capturedTarget.Name?.TextValue ?? "Unknown";

                    context.TrainingService.RecordDecision(new ActionExplanation
                    {
                        Timestamp = DateTime.UtcNow,
                        ActionId = action.ActionId,
                        ActionName = "Diagnosis",
                        Category = "Healing",
                        TargetName = targetName,
                        ShortReason = $"Diagnosis on {targetName} at {capturedHpPercent:P0} (GCD heal)",
                        DetailedReason = $"Diagnosis cast on {targetName} at {capturedHpPercent:P0} HP. This is SGE's basic GCD single-target heal - a fallback when Addersgall heals aren't available. Has a cast time, so prefer oGCDs when possible.",
                        Factors = new[]
                        {
                            $"Target HP: {capturedHpPercent:P0}",
                            "450 potency heal",
                            "1.5s cast time",
                            "700 MP cost",
                        },
                        Alternatives = new[]
                        {
                            "Druochole (oGCD, instant, restores MP)",
                            "Taurochole (oGCD for tanks, adds mit)",
                            "E.Diagnosis (instant shield)",
                            "Kardia passive healing",
                        },
                        Tip = "Diagnosis is your fallback single-target heal. You should rarely need it because Druochole (oGCD, restores MP!) is almost always better. Only use Diagnosis when Addersgall is empty and Rhizomata is on cooldown.",
                        ConceptId = SgeConcepts.EmergencyHealing,
                        Priority = ExplanationPriority.Normal,
                    });
                }
            });
    }

    /// <summary>
    /// True when a free Addersgall oGCD heal (Druochole, or Taurochole for the tank) is enabled AND
    /// has a spendable stack for this target. Mirrors SingleTargetOgcdHandler's spend rule: the
    /// emergency reserve is bypassed only when the target is in the oGCD-emergency band, so we never
    /// defer Diagnosis to an oGCD that will refuse to fire (which would leave the target unhealed).
    /// </summary>
    private static bool OgcdWillCover(IAsclepiusContext context, IBattleChara target, float hpPercent)
    {
        var config = context.Configuration.Sage;

        var emergency = hpPercent <= context.Configuration.Healing.OgcdEmergencyThreshold;
        var spendable = context.AddersgallStacks > config.AddersgallReserve
                        || (context.AddersgallStacks >= 1 && emergency);
        if (!spendable) return false;

        var tank = context.PartyHelper.FindTankInParty(context.Player);
        var isTank = tank != null && target.GameObjectId == tank.GameObjectId;
        return isTank
            ? config.EnableTaurochole || config.EnableDruochole
            : config.EnableDruochole;
    }
}
