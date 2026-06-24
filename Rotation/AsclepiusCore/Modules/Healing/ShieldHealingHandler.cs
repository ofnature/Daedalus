using System;
using Olympus.Config;
using Olympus.Data;
using Olympus.Rotation.ApolloCore.Helpers;
using Olympus.Rotation.AsclepiusCore.Abilities;
using Olympus.Rotation.AsclepiusCore.Context;
using Olympus.Rotation.AsclepiusCore.Helpers;
using Olympus.Rotation.Common.Helpers;
using Olympus.Rotation.Common.Scheduling;
using Olympus.Services.Sage;
using Olympus.Services.Training;

namespace Olympus.Rotation.AsclepiusCore.Modules.Healing;

/// <summary>
/// Handles Eukrasian shield healing for Sage: E.Diagnosis and E.Prognosis.
/// Eukrasia activation bypasses the scheduler (direct dispatch) because the original
/// pattern fires Eukrasia (oGCD) during the GCD pass, which the scheduler's
/// CanExecuteOgcd gate would block. See CLAUDE.md "SGE Eukrasia timing".
/// </summary>
public sealed class ShieldHealingHandler : IHealingHandler
{
    public int Priority => 20;
    public string Name => "ShieldHealing";

    public void CollectCandidates(IAsclepiusContext context, RotationScheduler scheduler, bool isMoving)
    {
        if (isMoving) return;

        var config = context.Configuration.Sage;
        var player = context.Player;

        if (player.Level < SGEActions.Eukrasia.MinLevel) return;

        // RSR: do not press Eukrasia for E.Diagnosis when Addersting is already full — the shield
        // break would waste a proc. Gate at MaxStacks (not just IsAtMax) so we stop at 2→3 edge too
        // when the gauge reads one frame late.
        if (context.AdderstingService.CurrentStacks >= AdderstingTrackingService.MaxStacks)
        {
            context.Debug.EukrasiaState = "Yield (Addersting cap)";
            return;
        }

        var (avgHp, lowestHp, injuredCount) = AsclepiusPartyMetrics.GetAoEHealMetrics(context.PartyHelper, player);
        var minAoETargets = AoEHealTargetHelper.GetEffectiveMinTargets(
            context.Configuration.Healing, context.PartyHelper.GetPartySize(player));
        var (shouldActivateForAoE, shouldActivateForSt) = EvaluateShieldNeed(
            context, config, avgHp, lowestHp, injuredCount, minAoETargets);

        if (context.HasEukrasia)
        {
            // DoT maintenance takes priority — don't spend Eukrasia on shields while the
            // strategy target still needs Eukrasian Dosis apply/refresh.
            if (EnemyNeedsEukrasianDosis(context))
            {
                context.Debug.EukrasiaState = "Reserved for DoT";
                return;
            }

            // Eukrasia is already up — but only spend it on a shield when one is actually warranted.
            // Otherwise leave the Eukrasia for the DoT (DamageModule presses Eukrasia for Eukrasian
            // Dosis) or another Eukrasian spell. Without this gate the shield hijacks every Eukrasia,
            // looping Eukrasia → E.Diagnosis, eating the weave slot (starving Druochole) and never
            // letting the DoT apply.
            if (!shouldActivateForAoE && !shouldActivateForSt)
            {
                context.Debug.EukrasiaState = "Idle (no shield needed)";
                return;
            }

            TryPushEukrasianHealSpell(context, scheduler, preferAoE: shouldActivateForAoE);
            return;
        }

        if (!shouldActivateForAoE && !shouldActivateForSt) return;

        // Let DamageModule activate Eukrasia for DoT when uptime is missing.
        if (EnemyNeedsEukrasianDosis(context))
        {
            context.Debug.EukrasiaState = "Yield (DoT refresh)";
            return;
        }

        // Yield the single weave slot to a life-saving oGCD heal. Direct-dispatching Eukrasia consumes
        // the weave, so when someone is in the oGCD-emergency band AND we actually have an Addersgall
        // heal to weave (Druochole/Taurochole), let that heal land first — the shield can follow on the
        // next weave. When no Addersgall heal is available the shield is the best protection we have, so
        // do not yield.
        if (lowestHp <= context.Configuration.Healing.OgcdEmergencyThreshold
            && context.AddersgallStacks >= 1
            && (config.EnableDruochole || config.EnableTaurochole))
        {
            context.Debug.EukrasiaState = "Yield (emergency heal)";
            return;
        }

        // Direct-dispatch Eukrasia. The scheduler can't dispatch oGCDs during the GCD pass
        // (CanExecuteOgcd is false), but the game accepts ExecuteOgcd called directly because
        // Eukrasia has its own animation timing. See CLAUDE.md "SGE Eukrasia timing" note.
        if (context.ActionService.ExecuteOgcd(SGEActions.Eukrasia, player.GameObjectId))
        {
            context.Debug.PlannedAction = SGEActions.Eukrasia.Name;
            context.Debug.PlanningState = "Eukrasia";
            context.Debug.EukrasiaState = "Activating";
        }
    }

    /// <summary>
    /// Decides whether an Eukrasian shield is currently warranted, split into AoE (E.Prognosis) and
    /// single-target (E.Diagnosis) need. In mitigation mode shields are gated on an incoming
    /// raidwide/tankbuster (plus a low-HP backstop); in reactive mode they fall back to the legacy HP
    /// thresholds. Shared by both the activation path and the "Eukrasia already up" path so the shield
    /// never consumes an Eukrasia that was pressed for something else (e.g. the DoT).
    /// </summary>
    private static (bool aoe, bool st) EvaluateShieldNeed(
        IAsclepiusContext context, SageConfig config, float avgHp, float lowestHp, int injuredCount, int minAoETargets)
    {
        if (config.EukrasianShieldsForMitigation)
        {
            // Only a TIMELINE-confirmed mechanic drives a proactive shield. Pattern-based prediction is
            // too noisy in dungeon/trust content (routine tank melee reads as "tankbusters"), and a
            // false positive here loops Eukrasia → E.Diagnosis, capping Addersting and starving the
            // Druochole cap-dump. Dungeons (no timeline) therefore shield only on the HP backstop,
            // matching the auto-duty-content model (dungeons reactive, raids/trials proactive).
            var raidwideImminent = TimelineHelper.IsRaidwideImminent(
                context.TimelineService, context.BossMechanicDetector, context.Configuration, out var raidwideSource)
                && raidwideSource == "Timeline";
            var tankBusterImminent = TimelineHelper.IsTankBusterImminent(
                context.TimelineService, context.BossMechanicDetector, context.Configuration, out var busterSource)
                && busterSource == "Timeline";

            var aoe = config.EnableEukrasianPrognosis &&
                      (raidwideImminent ||
                       (injuredCount >= minAoETargets && avgHp <= config.EukrasianShieldHpBackstop));
            var st = config.EnableEukrasianDiagnosis &&
                     (tankBusterImminent || lowestHp <= config.EukrasianShieldHpBackstop);
            return (aoe, st);
        }

        // Reactive model (legacy): shield whenever HP dips below the shield threshold.
        var aoeReactive = config.EnableEukrasianPrognosis &&
                          injuredCount >= minAoETargets &&
                          avgHp < config.AoEHealThreshold;
        var stReactive = config.EnableEukrasianDiagnosis &&
                         lowestHp < config.EukrasianDiagnosisThreshold;
        return (aoeReactive, stReactive);
    }

    /// <summary>
    /// True when DoT is enabled and no in-range enemy (or recent cast) has healthy Eukrasian Dosis uptime.
    /// </summary>
    private static bool EnemyNeedsEukrasianDosis(IAsclepiusContext context)
    {
        if (!AsclepiusDotHelper.IsEnabled(context)) return false;

        var player = context.Player;
        if (player.Level < SGEActions.EukrasianDosis.MinLevel) return false;

        var dotAction = SGEActions.GetDotForLevel(player.Level);
        return !AsclepiusDotHelper.HasHealthyDotUptime(context, dotAction.Range);
    }

    private void TryPushEukrasianHealSpell(IAsclepiusContext context, RotationScheduler scheduler, bool preferAoE)
    {
        var config = context.Configuration.Sage;
        var player = context.Player;

        var (avgHp, lowestHp, injuredCount) = AsclepiusPartyMetrics.GetAoEHealMetrics(context.PartyHelper, player);

        // Prefer AoE shield when the AoE condition triggered; otherwise fall through to single-target.
        if (preferAoE && config.EnableEukrasianPrognosis)
        {
            var aoeAction = player.Level >= SGEActions.EukrasianPrognosisII.MinLevel
                ? SGEActions.EukrasianPrognosisII
                : SGEActions.EukrasianPrognosis;
            var aoeBehavior = player.Level >= SGEActions.EukrasianPrognosisII.MinLevel
                ? AsclepiusAbilities.EukrasianPrognosisII
                : AsclepiusAbilities.EukrasianPrognosis;

            if (!context.HealingCoordination.TryReserveAoEHeal(
                context.PartyCoordinationService, aoeAction.ActionId, aoeAction.HealPotency, 0))
            {
                context.Debug.EukrasianPrognosisState = "Skipped (remote AOE reserved)";
                return;
            }

            var capturedAvgHp = avgHp;
            var capturedInjuredCount = injuredCount;
            var capturedAction = aoeAction;

            scheduler.PushGcd(aoeBehavior, player.GameObjectId, priority: Priority,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = capturedAction.Name;
                    context.Debug.PlanningState = "E.Prognosis";
                    context.Debug.EukrasianPrognosisState = "Executing";

                    if (context.TrainingService?.IsTrainingEnabled == true)
                    {
                        context.TrainingService.RecordDecision(new ActionExplanation
                        {
                            Timestamp = DateTime.UtcNow,
                            ActionId = capturedAction.ActionId,
                            ActionName = capturedAction.Name,
                            Category = "Healing",
                            TargetName = "Party",
                            ShortReason = $"E.Prognosis - {capturedInjuredCount} need shields at {capturedAvgHp:P0}",
                            DetailedReason = $"Eukrasian Prognosis placed shields on party. {capturedInjuredCount} members injured at {capturedAvgHp:P0} average HP. Provides instant shield that protects against incoming damage. The Eukrasia → E.Prognosis combo is instant cast!",
                            Factors = new[]
                            {
                                $"Party avg HP: {capturedAvgHp:P0}",
                                $"Injured count: {capturedInjuredCount}",
                                "100 potency heal + 320 potency shield",
                                "Instant cast (via Eukrasia)",
                                "1000 MP cost",
                            },
                            Alternatives = new[]
                            {
                                "Kerachole (oGCD regen + mit)",
                                "Ixochole (oGCD instant heal)",
                                "Prognosis (GCD heal, no shield)",
                            },
                            Tip = "E.Prognosis is your GCD party shield! Apply BEFORE damage hits for maximum value. The shield absorbs damage, making it more efficient than healing after the fact.",
                            ConceptId = SgeConcepts.EukrasianPrognosisUsage,
                            Priority = ExplanationPriority.Normal,
                        });
                    }
                });
            return;
        }

        // Single-target shield — RSR: Addersting < 3 before E.Diagnosis (shield break generates a stack).
        if (config.EnableEukrasianDiagnosis)
        {
            if (context.AdderstingService.CurrentStacks >= AdderstingTrackingService.MaxStacks)
            {
                context.Debug.EukrasianDiagnosisState = "Skipped (Addersting cap)";
                return;
            }

            var target = context.PartyHelper.FindLowestHpPartyMember(player);
            if (target == null) return;
            if (context.HealingCoordination.IsTargetReserved(target.EntityId, context.PartyCoordinationService))
            {
                context.Debug.EukrasianDiagnosisState = "Skipped (reserved)";
                return;
            }
            if (AsclepiusStatusHelper.HasEukrasianDiagnosisShield(target))
            {
                context.Debug.EukrasianDiagnosisState = "Already shielded";
                return;
            }

            var hpPercent = target.MaxHp > 0 ? (float)target.CurrentHp / target.MaxHp : 1f;
            var action = SGEActions.EukrasianDiagnosis;

            var capturedTarget = target;
            var capturedHpPercent = hpPercent;

            scheduler.PushGcd(AsclepiusAbilities.EukrasianDiagnosis, target.GameObjectId, priority: Priority,
                onDispatched: _ =>
                {
                    var healAmount = action.HealPotency * 10;
                    context.HealingCoordination.TryReserveTarget(
                        capturedTarget.EntityId, context.PartyCoordinationService, healAmount, action.ActionId, 0);

                    context.Debug.PlannedAction = action.Name;
                    context.Debug.PlanningState = "E.Diagnosis";
                    context.Debug.EukrasianDiagnosisState = "Executing";

                    if (context.TrainingService?.IsTrainingEnabled == true)
                    {
                        var targetName = capturedTarget.Name?.TextValue ?? "Unknown";

                        context.TrainingService.RecordDecision(new ActionExplanation
                        {
                            Timestamp = DateTime.UtcNow,
                            ActionId = action.ActionId,
                            ActionName = "Eukrasian Diagnosis",
                            Category = "Healing",
                            TargetName = targetName,
                            ShortReason = $"E.Diagnosis on {targetName} at {capturedHpPercent:P0}",
                            DetailedReason = $"Eukrasian Diagnosis placed on {targetName} at {capturedHpPercent:P0} HP. Provides 300 potency heal + 540 potency shield. The shield absorbs incoming damage, making this very efficient for tank healing before busters!",
                            Factors = new[]
                            {
                                $"Target HP: {capturedHpPercent:P0}",
                                "300 potency heal + 540 potency shield",
                                "Instant cast (via Eukrasia)",
                                "900 MP cost",
                            },
                            Alternatives = new[]
                            {
                                "Druochole (oGCD heal, Addersgall cost)",
                                "Taurochole (oGCD heal + mit for tanks)",
                                "Diagnosis (GCD heal, no shield)",
                            },
                            Tip = "E.Diagnosis is amazing for tanks before busters! The shield absorbs the hit, and any leftover becomes healing when it expires. Generates Addersting when the shield breaks!",
                            ConceptId = SgeConcepts.EukrasianDiagnosisUsage,
                            Priority = ExplanationPriority.Normal,
                        });
                    }
                });
        }
    }
}
