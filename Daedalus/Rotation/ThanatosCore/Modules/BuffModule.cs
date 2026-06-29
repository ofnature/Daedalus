using Daedalus.Data;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.ThanatosCore.Abilities;
using Daedalus.Rotation.ThanatosCore.Context;
using Daedalus.Rotation.ThanatosCore.Helpers;
using Daedalus.Services;
using Daedalus.Services.Content;
using Daedalus.Services.Targeting;
using Daedalus.Services.Training;

namespace Daedalus.Rotation.ThanatosCore.Modules;

/// <summary>
/// Handles the Reaper buff management (scheduler-driven).
/// Manages Arcane Circle (party buff) and Enshroud (burst state).
/// </summary>
public sealed class BuffModule : IThanatosModule
{
    public int Priority => 20;
    public string Name => "Buff";

    private readonly IBurstWindowService? _burstWindowService;
    private readonly IDutyContentService? _dutyContentService;

    public BuffModule(IBurstWindowService? burstWindowService = null, IDutyContentService? dutyContentService = null)
    {
        _burstWindowService = burstWindowService;
        _dutyContentService = dutyContentService;
    }

    private bool ShouldHoldForBurst(float thresholdSeconds = 8f) =>
        BurstHoldHelper.ShouldHoldForBurst(_burstWindowService, thresholdSeconds);

    public bool TryExecute(IThanatosContext context, bool isMoving) => false;

    public void UpdateDebugState(IThanatosContext context) { }

    public void CollectCandidates(IThanatosContext context, RotationScheduler scheduler, bool isMoving)
    {
        if (!context.InCombat)
        {
            context.Debug.ArcaneCircleState = "Not in combat";
            context.Debug.EnshroudDecision = "Not in combat";
            return;
        }

        TryPushArcaneCircle(context, scheduler);
        TryPushEnshroud(context, scheduler);
    }

    private void TryPushArcaneCircle(IThanatosContext context, RotationScheduler scheduler)
    {
        if (!context.Configuration.Reaper.EnableArcaneCircle) return;
        var player = context.Player;
        if (player.Level < RPRActions.ArcaneCircle.MinLevel) return;
        if (context.HasArcaneCircle)
        {
            context.Debug.ArcaneCircleState = $"Active ({context.ArcaneCircleRemaining:F1}s)";
            return;
        }
        if (!context.ActionService.IsActionReady(RPRActions.ArcaneCircle.ActionId))
        {
            context.Debug.ArcaneCircleState = "On cooldown";
            return;
        }

        if (context.Configuration.Reaper.EnableBurstPooling &&
            ShouldHoldForBurst(context.Configuration.Reaper.ArcaneCircleHoldTime))
        {
            context.Debug.ArcaneCircleState = "Holding for burst window";
            return;
        }
        if (BurstHoldHelper.ShouldHoldForPhaseTransition(context.TimelineService))
        {
            context.Debug.ArcaneCircleState = "Holding (phase soon)";
            return;
        }
        if (!context.HasDeathsDesign)
        {
            context.Debug.ArcaneCircleState = "Waiting for Death's Design";
            return;
        }

        var partyCoord = context.PartyCoordinationService;
        if (partyCoord != null && partyCoord.IsPartyCoordinationEnabled &&
            context.Configuration.PartyCoordination.EnableRaidBuffCoordination)
        {
            if (!partyCoord.IsRaidBuffAligned(RPRActions.ArcaneCircle.ActionId))
                context.Debug.ArcaneCircleState = "Raid buffs desynced, using independently";
            else if (partyCoord.HasPendingRaidBuffIntent(
                context.Configuration.PartyCoordination.RaidBuffAlignmentWindowSeconds))
                context.Debug.ArcaneCircleState = "Aligning with party burst";
            partyCoord.AnnounceRaidBuffIntent(RPRActions.ArcaneCircle.ActionId);
        }

        scheduler.PushOgcd(ThanatosAbilities.ArcaneCircle, player.GameObjectId, priority: 1,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = RPRActions.ArcaneCircle.Name;
                context.Debug.ArcaneCircleState = "Activating";
                partyCoord?.OnRaidBuffUsed(RPRActions.ArcaneCircle.ActionId, 120_000);

                TrainingHelper.Decision(context.TrainingService)
                    .Action(RPRActions.ArcaneCircle.ActionId, RPRActions.ArcaneCircle.Name)
                    .AsMeleeBurst()
                    .Target(player.Name?.TextValue ?? "Self")
                    .Reason("Activating Arcane Circle (+3% party damage for 20s)",
                        "Arcane Circle is RPR's party buff. Grants Bloodsown Circle for personal damage and " +
                        "builds Immortal Sacrifice stacks from party GCDs for Plentiful Harvest.")
                    .Factors(new[] { "Death's Design active", "120s cooldown ready", "Party burst timing" })
                    .Alternatives(new[] { "Hold for phase timing", "Wait for other raid buffs" })
                    .Tip("Arcane Circle grants stacks from party GCDs. Use when the party will be actively attacking.")
                    .Concept("rpr_arcane_circle")
                    .Record();
                context.TrainingService?.RecordConceptApplication("rpr_arcane_circle", true, "Party burst activation");
            });
    }

    private void TryPushEnshroud(IThanatosContext context, RotationScheduler scheduler)
    {
        if (!context.Configuration.Reaper.EnableEnshroud) return;
        var player = context.Player;
        if (player.Level < RPRActions.Enshroud.MinLevel) return;
        // Level-met but the job quest that unlocks Enshroud may not be done (leveling via AutoDuty).
        // IsActionReady doesn't check quest unlock, so without this the row would optimistically show
        // "Ready — queued" while the game silently refuses the cast. Surface it clearly instead.
        if (!context.ActionService.IsActionLearned(RPRActions.Enshroud.ActionId))
        {
            context.Debug.EnshroudDecision = "Not unlocked (job quest)";
            return;
        }
        if (context.IsEnshrouded)
        {
            context.Debug.EnshroudDecision = context.Debug.GetEnshroudState();
            return;
        }
        if (context.HasSoulReaver)
        {
            context.Debug.EnshroudDecision = "In Soul Reaver state";
            return;
        }
        if (context.HasExecutioner)
        {
            context.Debug.EnshroudDecision = "Spending Executioner stacks";
            return;
        }
        // Ideal Host grants a FREE Enshroud (no Shroud cost) — bypass the gauge requirement when it's up.
        if (!context.HasIdealHost && context.Shroud < context.Configuration.Reaper.ShroudMinGauge)
        {
            context.Debug.EnshroudDecision = $"Need {context.Configuration.Reaper.ShroudMinGauge} Shroud ({context.Shroud}/{context.Configuration.Reaper.ShroudMinGauge})";
            return;
        }
        if (!context.ActionService.IsActionReady(RPRActions.Enshroud.ActionId))
        {
            context.Debug.EnshroudDecision = "On cooldown";
            return;
        }

        // Auto-config low-HP gate: in dungeon / open-world content, don't blow Enshroud when everything
        // in range is about to die (the burst would be wasted). Never gates in trials/raids/high-end —
        // boss HP makes it pointless. Uses the HEALTHIEST enemy so a healthy pack never trips it.
        var hpThreshold = context.Configuration.Reaper.EnshroudSkipBelowTargetHpPercent;
        if (hpThreshold > 0f)
        {
            var profile = _dutyContentService?.EffectiveProfile ?? EffectiveDutyProfile.None;
            var best = context.TargetingService.FindEnemyForAction(
                EnemyTargetingStrategy.HighestHp, RPRActions.Slice.ActionId, player);
            var hpFraction = best is { MaxHp: > 0 } ? (float)best.CurrentHp / best.MaxHp : 1f;
            if (ThanatosEnshroudPolicy.ShouldSkipForLowHp(profile, hpFraction, hpThreshold))
            {
                context.Debug.EnshroudDecision = $"Target dying ({hpFraction * 100f:F0}% < {hpThreshold:F0}%, dungeon)";
                return;
            }
        }

        if (context.Configuration.Reaper.EnableBurstPooling
            && context.Configuration.Reaper.SaveShroudForBurst
            && ShouldHoldForBurst(context.Configuration.Reaper.ArcaneCircleHoldTime)
            && context.Shroud < 90)
        {
            context.Debug.EnshroudDecision = "Holding for burst window";
            return;
        }

        // By here we have the Shroud gauge (or Ideal Host) and we are NOT pooling for a real burst
        // window (the ShouldHoldForBurst check above respects the solo fallback), so Enshroud fires.
        // NOTE: Enshroud is intentionally NOT gated on Death's Design. DD is great to have up during
        // the burst, and the rotation maintains it separately, but requiring it to ENTER Enshroud
        // blocked the burst whenever the current target briefly lacked the DoT (target swaps in packs)
        // — and forcing DD high-priority to satisfy that gate made it re-apply every swap, starving the
        // Soul Reaver spenders that build Shroud. Decoupling fixes both the burst and Shroud generation.

        // All gates passed — Enshroud is queued. Set the row here (not only in onDispatched) so when it
        // queues but loses the oGCD weave / never dispatches we see "Ready — queued" instead of a stale
        // bail reason from an earlier frame. onDispatched overwrites this with "Entering Enshroud".
        context.Debug.EnshroudDecision = $"Ready — queued (Shroud {context.Shroud}, DD {context.DeathsDesignRemaining:F0}s)";

        scheduler.PushOgcd(ThanatosAbilities.Enshroud, player.GameObjectId, priority: 2,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = RPRActions.Enshroud.Name;
                context.Debug.EnshroudDecision = "Entering Enshroud";

                var reason = context.HasArcaneCircle ? "Arcane Circle active" :
                             context.Shroud >= 90 ? "Shroud gauge nearly full" :
                             "Death's Design has good duration";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(RPRActions.Enshroud.ActionId, RPRActions.Enshroud.Name)
                    .AsMeleeBurst()
                    .Target(player.Name?.TextValue ?? "Self")
                    .Reason($"Entering Enshroud ({reason})",
                        "Enshroud transforms your rotation into high-damage Void/Cross Reaping GCDs. " +
                        "Grants 5 Lemure Shroud stacks. Build Void Shroud with Reaping GCDs for Lemure's Slice. " +
                        "Finish with Communio → Perfectio for maximum burst.")
                    .Factors(new[] { $"Shroud: {context.Shroud}/50", reason, $"Death's Design: {context.DeathsDesignRemaining:F1}s" })
                    .Alternatives(new[] { "Wait for Arcane Circle", "Save for burst window" })
                    .Tip("Enshroud is your primary burst phase. Prioritize during Arcane Circle window.")
                    .Concept("rpr_enshroud")
                    .Record();
                context.TrainingService?.RecordConceptApplication("rpr_enshroud", true, "Burst phase entry");
            });
    }
}
