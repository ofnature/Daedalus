using Dalamud.Game.ClientState.Objects.Types;
using Daedalus.Config;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Services.Healing.Models;

namespace Daedalus.Services.Healing.Strategies;

/// <summary>
/// Tiered priority heal selection strategy.
/// Uses a fixed tier order: oGCDs > Lily heals > Regen > GCD heals
/// This is the default, battle-tested approach.
/// </summary>
public sealed class TieredHealSelectionStrategy : IHealSelectionStrategy
{
    /// <summary>
    /// Below this HP fraction an overheal-rejected GCD heal fires anyway (see the low-HP
    /// override in tier 3) — an overheal beats no heal once the target is genuinely hurt.
    /// </summary>
    public const float ForcedGcdHealHpPercent = 0.65f;

    public string StrategyName => "Tier-Based";

    /// <inheritdoc/>
    public (ActionDefinition? action, int healAmount, string selectionReason) SelectBestSingleHeal(
        HealSelectionContext context,
        SpellCandidateEvaluator evaluator)
    {
        // Skip if target doesn't need healing
        if (context.MissingHp <= 0)
        {
            return (null, 0, "Target doesn't need healing (missingHp <= 0)");
        }

        ActionDefinition? selectedAction = null;
        int selectedHealAmount = 0;
        string selectionReason = "No valid candidates";

        // Note: Benediction and Tetragrammaton are now handled as oGCDs in Apollo.TryExecuteOgcd()
        // with proper priority ordering (emergency heals first, then regular oGCDs)

        // === TIER 1: Lily GCD (Afflatus Solace) ===
        // Blood Lily optimization: prefer lily heals based on strategy to generate Blood Lilies
        // MP conservation: aggressively prefer lily heals when MP is low
        var shouldUseLily = context.LilyCount > 0 &&
            (ShouldPreferLilyHeal(context) ||
             (context.IsInMpConservationMode && context.Config.PreferLiliesInConservationMode));

        if (shouldUseLily)
        {
            var result = evaluator.EvaluateSingleTarget(
                WHMActions.AfflatusSolace,
                context.Player.Level,
                context.Mind, context.Det, context.Wd,
                context.Target);

            if (result.IsValid && result.Action is not null)
            {
                selectedAction = result.Action;
                selectedHealAmount = result.HealAmount;
                var mpNote = context.IsInMpConservationMode ? ", MP conservation" : "";
                selectionReason = $"Tier 1: Lily heal ({context.LilyCount} lilies, {context.BloodLilyCount}/3 Blood, {context.LilyStrategy}{mpNote})";
            }
        }
        else if (context.LilyCount == 0)
        {
            evaluator.TrackRejected(WHMActions.AfflatusSolace, 0, "No lilies available");
        }
        else
        {
            evaluator.TrackRejected(WHMActions.AfflatusSolace, 0,
                $"Strategy {context.LilyStrategy}: Blood {context.BloodLilyCount}/3, HP {context.HpPercent:P0}");
        }

        // === TIER 2: Regen (HoT) ===
        // Regen is the only spell with an HP threshold
        // Apply if target is below threshold HP and doesn't have Regen or it's about to expire
        // Threshold is dynamic based on damage rate: higher damage = apply earlier
        if (selectedAction is null)
        {
            // Dynamic Regen threshold based on damage rate
            // High damage (>300 DPS): Apply earlier at 75% HP
            // Low damage (<100 DPS): Defer to 45% HP
            // Normal: Use default 60%
            var dynamicRegenThreshold = context.DamageRate switch
            {
                > 300f => 0.75f,  // High damage - apply early
                < 100f => 0.45f,  // Low damage - defer
                _ => FFXIVConstants.RegenHpThreshold  // Normal (0.60f)
            };
            var targetBelowThreshold = context.HpPercent < dynamicRegenThreshold;
            var needsRegen = (!context.HasRegen || context.RegenRemaining < FFXIVConstants.RegenRefreshThreshold)
                && targetBelowThreshold;

            if (needsRegen)
            {
                var result = evaluator.EvaluateSingleTarget(
                    WHMActions.Regen,
                    context.Player.Level,
                    context.Mind, context.Det, context.Wd,
                    context.Target);

                if (result.IsValid && result.Action is not null)
                {
                    selectedAction = result.Action;
                    selectedHealAmount = result.HealAmount;
                    selectionReason = context.HasRegen ? "Tier 2: Regen (refresh)" : "Tier 2: Regen";
                }
            }
            else if (!targetBelowThreshold)
            {
                evaluator.TrackRejected(WHMActions.Regen, 0,
                    $"HP {context.HpPercent:P0} >= {dynamicRegenThreshold:P0} threshold (DPS: {context.DamageRate:F0})");
            }
            else
            {
                evaluator.TrackRejected(WHMActions.Regen, 0,
                    $"Already has Regen ({context.RegenRemaining:F1}s remaining)");
            }
        }

        // === TIER 3: Regular GCD (Cure II; Cure only below Lv.30) ===
        // Apply overheal prevention to GCD heals to avoid wasting MP.
        // Cure I is NEVER used once Cure II is unlocked: it's 500p vs 800p on the same GCD, and
        // Freecure (the proc that justified fishing with Cure) was REMOVED from the game in 7.0 —
        // the old conservation "fish for Freecure" branch was dead logic. Crucially, Cure I is also
        // no longer the overheal fallback: when Cure II would overheal (target ~85%, Regen rolling),
        // the right answer is NO GCD heal — lilies/oGCDs/Regen cover small deficits and the GCD goes
        // to Glare. (Validated log 2026-07-02: Cure I on an 85% tank at Lv.92, twice.)
        if (selectedAction is null)
        {
            // "Unlocked" must be SYNC-AWARE, not a raw MinLevel check (field report 2026-07-23:
            // a Lv.34 CNJ synced into a low-level dungeon kept selecting Cure II — synced OUT and
            // undispatchable — so the scheduler skipped it every frame and Stone fired instead;
            // the tank sat at 19% unhealed). IsActionLearned reads the ActionManager, which
            // reports synced-out actions as not learned, so the Cure I branch engages under sync.
            var cureIiUnlocked = context.Player.Level >= WHMActions.CureII.MinLevel
                && evaluator.IsActionLearned(WHMActions.CureII.ActionId);
            var gcdHeal = cureIiUnlocked ? WHMActions.CureII : WHMActions.Cure;
            var tierLabel = cureIiUnlocked ? "Tier 3: Cure II" : "Tier 3: Cure (pre-Lv.30 or synced)";

            var result = evaluator.EvaluateSingleTarget(
                gcdHeal,
                context.Player.Level,
                context.Mind, context.Det, context.Wd,
                context.Target,
                context.MissingHp,
                context.Config.SingleTargetOverhealTolerance);

            if (result.IsValid && result.Action is not null)
            {
                selectedAction = result.Action;
                selectedHealAmount = result.HealAmount;
                selectionReason = tierLabel;
            }
            else if (context.HpPercent <= ForcedGcdHealHpPercent)
            {
                // LOW-HP OVERRIDE (field report 2026-07-23: a Lv.34 CNJ never healed in dungeons).
                // The overheal veto assumes lilies/oGCD heals cover small deficits — below Lv.50
                // NO such tools exist, and at low levels Cure II restores a huge fraction of a
                // tiny HP pool, so EVERY heal was "overheal" and the healer just DPSed while the
                // tank dropped. Below this threshold an overhealing GCD heal beats no heal at
                // any level: re-evaluate with the tolerance wide open.
                var forced = evaluator.EvaluateSingleTarget(
                    gcdHeal,
                    context.Player.Level,
                    context.Mind, context.Det, context.Wd,
                    context.Target,
                    context.MissingHp,
                    overhealTolerancePercent: 100f);

                if (forced.IsValid && forced.Action is not null)
                {
                    selectedAction = forced.Action;
                    selectedHealAmount = forced.HealAmount;
                    selectionReason = tierLabel + " (forced — low HP, overheal accepted)";
                }
            }
        }

        // Mark selected action
        if (selectedAction is not null)
        {
            evaluator.MarkAsSelected(selectedAction.ActionId);
        }

        return (selectedAction, selectedHealAmount, selectionReason);
    }

    /// <inheritdoc/>
    public (ActionDefinition? action, int healAmount, IBattleChara? cureIIITarget, string selectionReason) SelectBestAoEHeal(
        AoEHealSelectionContext context,
        SpellCandidateEvaluator evaluator)
    {
        // Check if we have enough targets
        var hasSelfCenteredTargets = context.InjuredCount >= context.Config.AoEHealMinTargets;
        var hasCureIIITargets = context.CureIIITargetCount >= context.Config.AoEHealMinTargets;

        if (!hasSelfCenteredTargets && !hasCureIIITargets)
        {
            return (null, 0, null,
                $"Not enough targets (self:{context.InjuredCount}, CureIII:{context.CureIIITargetCount} < {context.Config.AoEHealMinTargets} min)");
        }

        // Note: Assize is handled as a DPS oGCD in Apollo.TryExecuteOgcd, not here.
        // It's used on cooldown for damage, with healing as a bonus.

        ActionDefinition? selectedAction = null;
        int selectedHealAmount = 0;
        string selectionReason = "No valid candidates";
        IBattleChara? selectedCureIIITarget = null;

        // === TIER 1: Lily AoE (Afflatus Rapture) ===
        // Blood Lily optimization: prefer lily heals based on strategy to generate Blood Lilies
        // MP conservation: aggressively prefer lily heals when MP is low
        // For AoE heals, we use 0 for HP percent since we're healing multiple targets
        var shouldUseLilyAoE = context.LilyCount > 0 && hasSelfCenteredTargets &&
            (ShouldPreferLilyHealForAoE(context) ||
             (context.IsInMpConservationMode && context.Config.PreferLiliesInConservationMode));

        if (shouldUseLilyAoE)
        {
            var result = evaluator.EvaluateAoE(
                WHMActions.AfflatusRapture,
                context.Player.Level,
                context.Mind, context.Det, context.Wd);

            if (result.IsValid && result.Action is not null)
            {
                selectedAction = result.Action;
                selectedHealAmount = result.HealAmount;
                var mpNote = context.IsInMpConservationMode ? ", MP conservation" : "";
                selectionReason = $"Tier 1: Lily AoE heal ({context.LilyCount} lilies, {context.BloodLilyCount}/3 Blood, {context.LilyStrategy}{mpNote})";
            }
        }
        else if (context.LilyCount == 0)
        {
            evaluator.TrackRejected(WHMActions.AfflatusRapture, 0, "No lilies available");
        }
        else if (!hasSelfCenteredTargets)
        {
            evaluator.TrackRejected(WHMActions.AfflatusRapture, 0,
                $"Not enough self-centered targets ({context.InjuredCount})");
        }
        else
        {
            evaluator.TrackRejected(WHMActions.AfflatusRapture, 0,
                $"Strategy {context.LilyStrategy}: Blood {context.BloodLilyCount}/3");
        }

        // === TIER 1.5: Cure III (targeted AoE, when party is stacked) ===
        // Higher potency (600) than Medica (400), but requires stacked party within 10y
        // No overheal check: the AoEHealHpThreshold already gates who qualifies; adding an
        // overheal check on top creates a dead zone where members are "injured enough to count"
        // but "too healthy to receive healing" at level-100 potencies.
        if (selectedAction is null && hasCureIIITargets && context.CureIIITarget is not null)
        {
            var result = evaluator.EvaluateAoE(
                WHMActions.CureIII,
                context.Player.Level,
                context.Mind, context.Det, context.Wd,
                context.AverageMissingHp,
                false,
                context.Config.AoEOverhealTolerance);

            if (result.IsValid && result.Action is not null)
            {
                selectedAction = result.Action;
                selectedHealAmount = result.HealAmount;
                selectedCureIIITarget = context.CureIIITarget;
                selectionReason = $"Tier 1.5: Cure III ({context.CureIIITargetCount} stacked around {context.CureIIITarget.Name})";
            }
        }
        else if (selectedAction is null && !hasCureIIITargets)
        {
            evaluator.TrackRejected(WHMActions.CureIII, 0,
                $"Not enough stacked targets ({context.CureIIITargetCount} < {context.Config.AoEHealMinTargets})");
        }
        else if (selectedAction is null && context.CureIIITarget is null)
        {
            evaluator.TrackRejected(WHMActions.CureIII, 0, "No valid Cure III target");
        }

        // === TIER 2: Medica III (HoT, highest potency) ===
        if (selectedAction is null && hasSelfCenteredTargets)
        {
            if (context.AnyHaveRegen)
            {
                evaluator.TrackRejected(WHMActions.MedicaIII, 0, "Would overwrite existing regen");
            }
            else
            {
                var result = evaluator.EvaluateAoE(
                    WHMActions.MedicaIII,
                    context.Player.Level,
                    context.Mind, context.Det, context.Wd,
                    context.AverageMissingHp,
                    false,
                    context.Config.AoEOverhealTolerance);

                if (result.IsValid && result.Action is not null)
                {
                    selectedAction = result.Action;
                    selectedHealAmount = result.HealAmount;
                    selectionReason = "Tier 2: Medica III (HoT)";
                }
            }
        }

        // === TIER 3: Medica II (HoT) ===
        if (selectedAction is null && hasSelfCenteredTargets)
        {
            if (context.AnyHaveRegen)
            {
                evaluator.TrackRejected(WHMActions.MedicaII, 0, "Would overwrite existing regen");
            }
            else
            {
                var result = evaluator.EvaluateAoE(
                    WHMActions.MedicaII,
                    context.Player.Level,
                    context.Mind, context.Det, context.Wd,
                    context.AverageMissingHp,
                    false,
                    context.Config.AoEOverhealTolerance);

                if (result.IsValid && result.Action is not null)
                {
                    selectedAction = result.Action;
                    selectedHealAmount = result.HealAmount;
                    selectionReason = "Tier 3: Medica II (HoT)";
                }
            }
        }

        // === TIER 4: Medica (fallback) ===
        if (selectedAction is null && hasSelfCenteredTargets)
        {
            var result = evaluator.EvaluateAoE(
                WHMActions.Medica,
                context.Player.Level,
                context.Mind, context.Det, context.Wd,
                context.AverageMissingHp,
                false,
                context.Config.AoEOverhealTolerance);

            if (result.IsValid && result.Action is not null)
            {
                selectedAction = result.Action;
                selectedHealAmount = result.HealAmount;
                selectionReason = "Tier 4: Medica (fallback)";
            }
        }

        // === TIER 5: Lily AoE fallback ===
        // If the lily strategy didn't prefer Rapture at Tier 1 but everything else failed
        // (e.g. EnableAoEOverhealCheck is on and Medicas were blocked), use Rapture anyway.
        // Rapture has no overheal check and costs no MP — better than casting nothing.
        if (selectedAction is null && context.LilyCount > 0 && hasSelfCenteredTargets)
        {
            var result = evaluator.EvaluateAoE(
                WHMActions.AfflatusRapture,
                context.Player.Level,
                context.Mind, context.Det, context.Wd);

            if (result.IsValid && result.Action is not null)
            {
                selectedAction = result.Action;
                selectedHealAmount = result.HealAmount;
                selectionReason = $"Tier 5: Lily AoE fallback ({context.LilyCount} lilies, strategy {context.LilyStrategy})";
            }
        }

        // Mark selected action
        if (selectedAction is not null)
        {
            evaluator.MarkAsSelected(selectedAction.ActionId);
        }

        return (selectedAction, selectedHealAmount, selectedCureIIITarget, selectionReason);
    }

    /// <summary>
    /// Determines whether lily heals (Afflatus Solace) should be preferred
    /// over MP-based alternatives based on the configured Blood Lily strategy.
    /// Now considers combat duration for smarter lily flushing and damage rate
    /// for instant-cast advantage during heavy damage.
    /// </summary>
    private static bool ShouldPreferLilyHeal(HealSelectionContext context)
    {
        if (context.LilyCount == 0)
            return false;

        // === HIGH PRIORITY: Always prefer lilies in these situations ===

        // Time-aware lily flush: If combat has been going for a while and we have
        // blood lilies ready, prefer lily heals to ensure we get Afflatus Misery off.
        // Lily regenerates every 20 seconds, so after ~60s we should be spending freely.
        if (context.CombatDuration > 60f && context.BloodLilyCount >= 2 && context.LilyCount > 0)
            return true;

        // If we have full blood lilies (3/3) and lilies available, we should spend
        // lilies even though blood lily won't increase (prevents lily waste)
        if (context.BloodLilyCount >= 3 && context.LilyCount > 0)
            return true;

        // Aggressive lily flush: when at 2 blood lilies, always prefer lily heals
        // to build the third blood lily for Afflatus Misery before combat ends
        if (context.Config.EnableAggressiveLilyFlush && context.BloodLilyCount >= 2)
            return true;

        // === DAMAGE-RATE-AWARE SELECTION ===
        // Lily heals are instant-cast GCDs - perfect for high-damage situations
        // where cast time could mean the target dies mid-cast

        if (context.Config.EnableDamageAwareLilySelection && context.DamageRate > 0)
        {
            // High damage: Always prefer lily heals for instant response
            if (context.DamageRate >= context.Config.AggressiveLilyDamageRate)
                return true;

            // Moderate damage: Relax HP thresholds for lily usage
            // Use lilies at higher HP to get ahead of damage
            if (context.DamageRate >= context.Config.ModerateLilyDamageRate)
            {
                // Dynamic threshold: higher damage = higher HP threshold for lilies
                // At moderate rate, threshold is 75%; scales up to 85% at aggressive rate
                var damageRatio = (context.DamageRate - context.Config.ModerateLilyDamageRate) /
                    (context.Config.AggressiveLilyDamageRate - context.Config.ModerateLilyDamageRate);
                var damageAdjustedThreshold = context.Config.ConservativeLilyHpThreshold +
                    (damageRatio * 0.10f);

                if (context.HpPercent < damageAdjustedThreshold && context.BloodLilyCount < 3)
                    return true;
            }
        }

        // === STANDARD STRATEGY-BASED SELECTION ===

        return context.LilyStrategy switch
        {
            LilyGenerationStrategy.Aggressive => true,
            LilyGenerationStrategy.Balanced => context.BloodLilyCount < 3,
            LilyGenerationStrategy.Conservative => context.BloodLilyCount < 3 &&
                context.HpPercent < context.Config.ConservativeLilyHpThreshold,
            LilyGenerationStrategy.Disabled => false,
            _ => context.BloodLilyCount < 3 // Default to Balanced behavior
        };
    }

    /// <summary>
    /// Determines whether lily heals (Afflatus Rapture) should be preferred for AoE.
    /// For AoE heals, HP percent is not relevant since we're healing multiple targets.
    /// Now considers party damage rate for instant-cast advantage during heavy damage.
    /// </summary>
    private static bool ShouldPreferLilyHealForAoE(AoEHealSelectionContext context)
    {
        if (context.LilyCount == 0)
            return false;

        // === HIGH PRIORITY: Always prefer lilies in these situations ===

        // Time-aware lily flush
        if (context.CombatDuration > 60f && context.BloodLilyCount >= 2 && context.LilyCount > 0)
            return true;

        // Full blood lilies
        if (context.BloodLilyCount >= 3 && context.LilyCount > 0)
            return true;

        // Aggressive lily flush
        if (context.Config.EnableAggressiveLilyFlush && context.BloodLilyCount >= 2)
            return true;

        // === DAMAGE-RATE-AWARE SELECTION ===
        // Afflatus Rapture is instant-cast - perfect for heavy party damage
        // Use higher threshold for party (2x) since party damage is spread across members

        if (context.Config.EnableDamageAwareLilySelection && context.PartyDamageRate > 0)
        {
            // High party damage: Always prefer instant AoE lily
            // Party threshold is 2x single-target since damage is distributed
            if (context.PartyDamageRate >= context.Config.AggressiveLilyDamageRate * 2)
                return true;

            // Moderate party damage: Prefer lily when building blood lilies
            if (context.PartyDamageRate >= context.Config.ModerateLilyDamageRate * 2 &&
                context.BloodLilyCount < 3)
                return true;
        }

        // === STANDARD STRATEGY-BASED SELECTION ===

        return context.LilyStrategy switch
        {
            LilyGenerationStrategy.Aggressive => true,
            LilyGenerationStrategy.Balanced => context.BloodLilyCount < 3,
            LilyGenerationStrategy.Conservative => context.BloodLilyCount < 3,
            LilyGenerationStrategy.Disabled => false,
            _ => context.BloodLilyCount < 3
        };
    }
}
