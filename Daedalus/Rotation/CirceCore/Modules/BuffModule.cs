using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using System;
using Daedalus.Models;
using Daedalus.Data;
using Daedalus.Rotation.CirceCore.Abilities;
using Daedalus.Rotation.CirceCore.Context;
using Daedalus.Rotation.CirceCore.Helpers;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.RoleActionHelpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Services;
using Daedalus.Services.Party;
using Daedalus.Services.Training;

namespace Daedalus.Rotation.CirceCore.Modules;

/// <summary>
/// Handles Red Mage oGCD buffs and abilities (scheduler-driven).
/// Manages Fleche, Contre Sixte, Embolden, Manafication, Corps-a-corps, Engagement,
/// Acceleration, Vice of Thorns, Prefulgence, Lucid Dreaming.
/// </summary>
public sealed class BuffModule : ICirceModule
{
    public int Priority => 20;
    public string Name => "Buff";

    private readonly IBurstWindowService? _burstWindowService;

    public BuffModule(IBurstWindowService? burstWindowService = null)
    {
        _burstWindowService = burstWindowService;
    }

    private bool ShouldHoldForBurst(float thresholdSeconds = 8f) =>
        BurstHoldHelper.ShouldHoldForBurst(_burstWindowService, thresholdSeconds);

    public bool TryExecute(ICirceContext context, bool isMoving) => false;

    public void UpdateDebugState(ICirceContext context) { }

    public void CollectCandidates(ICirceContext context, RotationScheduler scheduler, bool isMoving)
    {
        if (!context.InCombat)
        {
            context.Debug.BuffState = "Not in combat";
            return;
        }

        var player = context.Player;
        var target = context.TargetingService.FindEnemy(
            context.Configuration.Targeting.EnemyStrategy,
            FFXIVConstants.CasterTargetingRange,
            player);

        TryPushFleche(context, scheduler, target);
        TryPushContreSixte(context, scheduler, target);
        TryPushViceOfThorns(context, scheduler, target);
        TryPushPrefulgence(context, scheduler, target);
        TryPushManafication(context, scheduler, target);
        TryPushEmbolden(context, scheduler, target);
        TryPushPostComboRetreat(context, scheduler, target);
        TryPushCorpsACorps(context, scheduler, target);
        TryPushEngagement(context, scheduler, target);
        TryPushAcceleration(context, scheduler);
        TryPushLucidDreaming(context, scheduler);
    }

    private void TryPushFleche(ICirceContext context, RotationScheduler scheduler, IBattleChara? target)
    {
        if (!context.Configuration.RedMage.EnableFleche) return;
        if (target == null) return;
        if (context.Player.Level < RDMActions.Fleche.MinLevel) return;
        if (!context.FlecheReady) return;
        if (RdmSoloBurstHelper.ShouldHoldFillerOgcdsForEmbolden(context, _burstWindowService))
        {
            context.Debug.BuffState = "Holding Fleche (Embolden next weave)";
            return;
        }

        scheduler.PushOgcd(CirceAbilities.Fleche, target.GameObjectId, priority: 1,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = RDMActions.Fleche.Name;
                context.Debug.BuffState = "Fleche";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(RDMActions.Fleche.ActionId, RDMActions.Fleche.Name)
                    .AsCasterDamage().Target(target.Name?.TextValue)
                    .Reason("Fleche - high damage oGCD",
                        "Fleche is your primary single-target oGCD with a 25s cooldown. Use it on cooldown " +
                        "to maximize damage, weaving between GCDs. It doesn't interact with procs or mana.")
                    .Factors($"Black Mana: {context.BlackMana}", $"White Mana: {context.WhiteMana}", "25s cooldown")
                    .Alternatives("Hold for burst window", "Wait for better target")
                    .Tip("Always use Fleche on cooldown - it's free damage with no resource cost.")
                    .Concept(RdmConcepts.OgcdWeaving)
                    .Record();
                context.TrainingService?.RecordConceptApplication(RdmConcepts.OgcdWeaving, true, "Fleche used on cooldown");
            });
    }

    private void TryPushContreSixte(ICirceContext context, RotationScheduler scheduler, IBattleChara? target)
    {
        if (!context.Configuration.RedMage.EnableContreSixte) return;
        if (target == null) return;
        if (context.Player.Level < RDMActions.ContreSixte.MinLevel) return;
        if (!context.ContreSixteReady) return;
        if (RdmSoloBurstHelper.ShouldHoldFillerOgcdsForEmbolden(context, _burstWindowService))
        {
            context.Debug.BuffState = "Holding Contre Sixte (Embolden next weave)";
            return;
        }

        scheduler.PushOgcd(CirceAbilities.ContreSixte, target.GameObjectId, priority: 1,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = RDMActions.ContreSixte.Name;
                context.Debug.BuffState = "Contre Sixte";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(RDMActions.ContreSixte.ActionId, RDMActions.ContreSixte.Name)
                    .AsCasterDamage().Target(target.Name?.TextValue)
                    .Reason("Contre Sixte - AoE oGCD",
                        "Contre Sixte is your AoE oGCD with a 35s cooldown. It does good damage on single target " +
                        "and hits all enemies in range.")
                    .Factors($"Black Mana: {context.BlackMana}", $"White Mana: {context.WhiteMana}", "35s cooldown")
                    .Alternatives("Hold for burst window", "Wait for more enemies")
                    .Tip("Use Contre Sixte on cooldown even on single target.")
                    .Concept(RdmConcepts.OgcdWeaving)
                    .Record();
                context.TrainingService?.RecordConceptApplication(RdmConcepts.OgcdWeaving, true, "Contre Sixte used on cooldown");
            });
    }

    private void TryPushViceOfThorns(ICirceContext context, RotationScheduler scheduler, IBattleChara? target)
    {
        if (!context.Configuration.RedMage.EnableViceOfThorns) return;
        if (target == null) return;
        if (context.Player.Level < RDMActions.ViceOfThorns.MinLevel) return;
        if (!context.HasThornedFlourish) return;

        scheduler.PushOgcd(CirceAbilities.ViceOfThorns, target.GameObjectId, priority: 1,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = RDMActions.ViceOfThorns.Name;
                context.Debug.BuffState = "Vice of Thorns";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(RDMActions.ViceOfThorns.ActionId, RDMActions.ViceOfThorns.Name)
                    .AsCasterBurst().Target(target.Name?.TextValue)
                    .Reason("Vice of Thorns - finisher proc",
                        "Vice of Thorns becomes available after using Verflare or Verholy, granting the " +
                        "Thorned Flourish buff. Use it immediately as part of your finisher sequence.")
                    .Factors("Thorned Flourish active")
                    .Alternatives("Continue finisher sequence")
                    .Tip("Always use Vice of Thorns when available.")
                    .Concept(RdmConcepts.FinisherProcs)
                    .Record();
                context.TrainingService?.RecordConceptApplication(RdmConcepts.FinisherProcs, true, "Vice of Thorns proc consumed");
            });
    }

    private void TryPushPrefulgence(ICirceContext context, RotationScheduler scheduler, IBattleChara? target)
    {
        if (!context.Configuration.RedMage.EnablePrefulgence) return;
        if (target == null) return;
        if (context.Player.Level < RDMActions.Prefulgence.MinLevel) return;
        if (!context.HasPrefulgenceReady) return;
        if (RdmSoloBurstHelper.ShouldHoldFillerOgcdsForEmbolden(context, _burstWindowService))
        {
            context.Debug.BuffState = "Holding Prefulgence for Embolden";
            return;
        }

        // Priority 0: the 900p Embolden-window nuke must beat Fleche/Contre Sixte (also
        // priority 1, and List.Sort ties are unstable) to the scarce post-burst weave slots —
        // it lost both slots to Fleche and the proc timed out with the pull (Mistwake
        // 2026-07-02). Prefulgence Ready is a 30s proc; fillers can always take the next slot.
        scheduler.PushOgcd(CirceAbilities.Prefulgence, target.GameObjectId, priority: 0,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = RDMActions.Prefulgence.Name;
                context.Debug.BuffState = "Prefulgence";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(RDMActions.Prefulgence.ActionId, RDMActions.Prefulgence.Name)
                    .AsCasterBurst().Target(target.Name?.TextValue)
                    .Reason("Prefulgence - finisher proc",
                        "Prefulgence becomes available after using Manafication with 6 stacks consumed.")
                    .Factors("Prefulgence Ready active")
                    .Alternatives("Continue burst phase")
                    .Tip("Use Prefulgence immediately when it procs.")
                    .Concept(RdmConcepts.FinisherProcs)
                    .Record();
                context.TrainingService?.RecordConceptApplication(RdmConcepts.FinisherProcs, true, "Prefulgence proc consumed");
            });
    }

    private void TryPushEmbolden(ICirceContext context, RotationScheduler scheduler, IBattleChara? target)
    {
        if (!context.Configuration.RedMage.EnableEmbolden) return;
        var player = context.Player;
        if (player.Level < RDMActions.Embolden.MinLevel) return;
        if (!context.EmboldenReady) return;
        if (context.HasEmbolden) return;
        if (BurstHoldHelper.ShouldHoldForPhaseTransition(context.TimelineService))
        {
            context.Debug.BuffState = "Holding Embolden (phase soon)";
            return;
        }

        var rdmCfg = context.Configuration.RedMage;
        var soloBurst = RdmSoloBurstHelper.IsSoloBurstMode(_burstWindowService);

        if (soloBurst)
        {
            // Manafication already fired: the chain is committed — follow with Embolden NOW,
            // without re-checking pack viability. The pack state that justified Manafication can
            // thin out one GCD later (mobs die), and re-litigating it strands Manafication
            // unbuffed while Embolden drifts (Mistwake log 2026-07-02).
            if (context.HasManafication)
            {
                // Priority 1: the follower must win the next weave slot outright — hardcast
                // stretches only surface ~one slot per dualcast.
                PushEmbolden(context, scheduler, player, priority: 1, partyCoord: null);
                return;
            }

            if (!RdmSoloBurstHelper.IsBurstPackViable(context, target, player))
            {
                context.Debug.BuffState = "Hold Embolden (pack dying/small)";
                return;
            }

            // Chain order is Manafication → Embolden. Firing Embolden first deadlocks the pair:
            // Manafication then waits on a 120s Embolden CD it can never satisfy.
            if (RdmSoloBurstHelper.ShouldHoldEmboldenForManafication(context, rdmCfg.SoloBurstPairCooldownSeconds))
            {
                context.Debug.BuffState = "Hold Embolden for Manafication";
                return;
            }

            PushEmbolden(context, scheduler, player, priority: 3, partyCoord: null);
            return;
        }

        if (ShouldHoldForBurst(rdmCfg.EmboldenHoldTime))
        {
            context.Debug.BuffState = "Holding Embolden for burst";
            return;
        }
        if (!context.CanStartMeleeCombo)
        {
            if (context.LowerMana < 40)
            {
                context.Debug.BuffState = "Hold Embolden for melee combo";
                return;
            }
        }

        var partyCoord = context.PartyCoordinationService;
        if (partyCoord != null && partyCoord.IsPartyCoordinationEnabled
            && context.Configuration.PartyCoordination.EnableRaidBuffCoordination)
        {
            if (!partyCoord.IsRaidBuffAligned(RDMActions.Embolden.ActionId))
                context.Debug.BuffState = "Raid buffs desynced, using independently";
            else if (partyCoord.HasPendingRaidBuffIntent(
                context.Configuration.PartyCoordination.RaidBuffAlignmentWindowSeconds))
                context.Debug.BuffState = "Aligning with party burst";
            partyCoord.AnnounceRaidBuffIntent(RDMActions.Embolden.ActionId);
        }

        PushEmbolden(context, scheduler, player, priority: 2, partyCoord);
    }

    private void PushEmbolden(
        ICirceContext context,
        RotationScheduler scheduler,
        IPlayerCharacter player,
        int priority,
        IPartyCoordinationService? partyCoord)
    {
        scheduler.PushOgcd(CirceAbilities.Embolden, player.GameObjectId, priority,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = RDMActions.Embolden.Name;
                context.Debug.BuffState = "Embolden (burst)";
                partyCoord?.OnRaidBuffUsed(RDMActions.Embolden.ActionId, 120_000);

                TrainingHelper.Decision(context.TrainingService)
                    .Action(RDMActions.Embolden.ActionId, RDMActions.Embolden.Name)
                    .AsRaidBuff()
                    .Reason("Embolden - party damage buff",
                        "Embolden increases damage dealt by you and nearby party members by 5% for 20 seconds.")
                    .Factors($"Black Mana: {context.BlackMana}", $"White Mana: {context.WhiteMana}",
                            context.CanStartMeleeCombo ? "Melee combo ready" : "Building mana")
                    .Alternatives("Wait for melee combo entry", "Hold for phase transition")
                    .Tip("Align Embolden with your melee combo for maximum damage.")
                    .Concept(RdmConcepts.Embolden)
                    .Record();
                context.TrainingService?.RecordConceptApplication(RdmConcepts.Embolden, true, "Party buff activated");
            });
    }

    private void TryPushManafication(ICirceContext context, RotationScheduler scheduler, IBattleChara? target)
    {
        if (!context.Configuration.RedMage.EnableManafication) return;
        var player = context.Player;
        if (player.Level < RDMActions.Manafication.MinLevel) return;
        if (!context.ManaficationReady) return;
        if (context.HasManafication) return;
        if (context.IsInMeleeCombo) return;
        if (BurstHoldHelper.ShouldHoldForPhaseTransition(context.TimelineService))
        {
            context.Debug.BuffState = "Holding Manafication (phase soon)";
            return;
        }

        var rdmCfg = context.Configuration.RedMage;
        var soloBurst = RdmSoloBurstHelper.IsSoloBurstMode(_burstWindowService);

        if (soloBurst)
        {
            // Embolden window already open: fire into it now — every gate below (including pack
            // viability, which can flip as mobs die) only makes the overlap shorter.
            if (context.HasEmbolden)
            {
                scheduler.PushOgcd(CirceAbilities.Manafication, player.GameObjectId, priority: 2,
                    onDispatched: _ => OnManaficationDispatched(context));
                return;
            }

            if (!RdmSoloBurstHelper.IsBurstPackViable(context, target, player))
            {
                context.Debug.BuffState = "Hold Manafication (pack dying/small)";
                return;
            }

            // Embolden desynced (long CD) or disabled: pairing is impossible this window. Fire
            // on cooldown behind the same mana floor as the non-solo path instead of drifting a
            // 110s cooldown behind a 120s one.
            if (!RdmSoloBurstHelper.IsEmboldenImminent(context, rdmCfg.SoloBurstPairCooldownSeconds))
            {
                if (context.LowerMana < 25)
                {
                    context.Debug.BuffState = "Hold Manafication for mana (Embolden desynced)";
                    return;
                }

                scheduler.PushOgcd(CirceAbilities.Manafication, player.GameObjectId, priority: 3,
                    onDispatched: _ => OnManaficationDispatched(context));
                return;
            }

            if (!RdmSoloBurstHelper.IsSoloBurstManaReadyForPairStart(context))
            {
                context.Debug.BuffState = $"Hold Manafication for mana ({context.LowerMana}/{rdmCfg.SoloBurstIdealMinMana})";
                return;
            }

            scheduler.PushOgcd(CirceAbilities.Manafication, player.GameObjectId, priority: 2,
                onDispatched: _ => OnManaficationDispatched(context));
            return;
        }

        // With UseManaficationWithMelee=true, hold until melee combo can start (existing behavior)
        // With UseManaficationWithMelee=false, fire on cooldown without waiting for melee context
        if (rdmCfg.UseManaficationWithMelee && context.CanStartMeleeCombo) return;
        if (context.LowerMana < 25) return;
        if (context.EmboldenReady && context.LowerMana < 40)
        {
            context.Debug.BuffState = "Hold Manafication for Embolden";
            return;
        }

        scheduler.PushOgcd(CirceAbilities.Manafication, player.GameObjectId, priority: 3,
            onDispatched: _ => OnManaficationDispatched(context));
    }

    private static void OnManaficationDispatched(ICirceContext context)
    {
        context.Debug.PlannedAction = RDMActions.Manafication.Name;
        context.Debug.BuffState = $"Manafication (mana: {context.BlackMana}|{context.WhiteMana})";
        TrainingHelper.Decision(context.TrainingService)
            .Action(RDMActions.Manafication.ActionId, RDMActions.Manafication.Name)
            .AsCasterResource("Mana", context.LowerMana)
            .Reason("Manafication - mana boost + damage buff",
                "Manafication adds 50 to both Black and White Mana and grants 6 Manafication stacks " +
                "that empower your melee combo.")
            .Factors($"Black Mana: {context.BlackMana}", $"White Mana: {context.WhiteMana}",
                $"Lower Mana: {context.LowerMana}", "Will add 50|50")
            .Alternatives("Wait for more mana", "Align with Embolden")
            .Tip("Use Manafication at 40-50 mana for optimal value.")
            .Concept(RdmConcepts.Manafication)
            .Record();
        context.TrainingService?.RecordConceptApplication(RdmConcepts.Manafication, true, "Mana boosted for melee combo");
    }

    private void TryPushCorpsACorps(ICirceContext context, RotationScheduler scheduler, IBattleChara? target)
    {
        if (!context.Configuration.RedMage.EnableCorpsACorps) return;
        if (target == null) return;
        var player = context.Player;
        if (player.Level < RDMActions.CorpsACorps.MinLevel) return;
        if (context.CorpsACorpsCharges == 0) return;

        var hpPercent = player.MaxHp > 0 ? (float)player.CurrentHp / player.MaxHp : 1f;
        if (hpPercent < context.Configuration.RedMage.MeleeDashMinHpPercent)
        {
            context.Debug.BuffState = $"Hold Corps-a-corps (HP {hpPercent:P0} low)";
            return;
        }

        if (RdmSoloBurstHelper.ShouldGapCloseForMeleeEntry(context, _burstWindowService, target))
        {
            scheduler.PushOgcd(CirceAbilities.CorpsACorps, target.GameObjectId, priority: 2,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = RDMActions.CorpsACorps.Name;
                    context.Debug.BuffState = "Corps-a-corps (gap close)";
                    context.Debug.DamageState = "Gap close for melee entry (Corps-a-corps)";
                });
            return;
        }

        var inBurst = context.IsInMeleeCombo || context.HasEmbolden || context.HasManafication;
        var capped = context.CorpsACorpsCharges >= 2;
        if (!inBurst && !capped)
        {
            context.Debug.BuffState = "Hold Corps-a-corps for burst";
            return;
        }

        scheduler.PushOgcd(CirceAbilities.CorpsACorps, target.GameObjectId, priority: 4,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = RDMActions.CorpsACorps.Name;
                context.Debug.BuffState = $"Corps-a-corps ({context.CorpsACorpsCharges - 1} charges)";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(RDMActions.CorpsACorps.ActionId, RDMActions.CorpsACorps.Name)
                    .AsMovement().Target(target.Name?.TextValue)
                    .Reason("Corps-a-corps - gap closer oGCD",
                        "Corps-a-corps is a gap closer with 2 charges. Use during burst windows or when capped.")
                    .Factors($"Charges: {context.CorpsACorpsCharges}", inBurst ? "In burst window" : "Capped charges")
                    .Alternatives("Hold for burst window", "Save for gap closing")
                    .Tip("Dump charges during burst windows. Don't cap on charges.")
                    .Concept(RdmConcepts.MeleePositioning)
                    .Record();
                context.TrainingService?.RecordConceptApplication(RdmConcepts.MeleePositioning, true, "Gap closer used in burst");
            });
    }

    private void TryPushEngagement(ICirceContext context, RotationScheduler scheduler, IBattleChara? target)
    {
        if (!context.Configuration.RedMage.EnableEngagement) return;
        if (target == null) return;
        var player = context.Player;
        if (player.Level < RDMActions.Engagement.MinLevel) return;
        if (context.EngagementCharges == 0) return;

        // Engagement is a 3y melee oGCD and Manafication does NOT extend it (only the enchanted
        // sword GCDs reach 25y). Dispatched from caster range, the game queue-accepts the press
        // (UseAction true) then silently drops it — no charge is consumed, so the charge count
        // never falls and the module re-pushed every weave slot forever (7 phantom Engagements
        // in 25s, Mistwake log 2026-07-02).
        if (RdmSoloBurstHelper.IsOutsideMeleeEntryRange(player, target))
        {
            context.Debug.BuffState = "Engagement out of melee range";
            return;
        }

        var hpPercent = player.MaxHp > 0 ? (float)player.CurrentHp / player.MaxHp : 1f;
        if (hpPercent < context.Configuration.RedMage.MeleeDashMinHpPercent)
        {
            context.Debug.BuffState = $"Hold Engagement (HP {hpPercent:P0} low)";
            return;
        }

        var inBurst = context.IsInMeleeCombo || context.HasEmbolden || context.HasManafication;
        var capped = context.EngagementCharges >= 2;
        if (!inBurst && !capped)
        {
            context.Debug.BuffState = "Hold Engagement for burst";
            return;
        }

        scheduler.PushOgcd(CirceAbilities.Engagement, target.GameObjectId, priority: 4,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = RDMActions.Engagement.Name;
                context.Debug.BuffState = $"Engagement ({context.EngagementCharges - 1} charges)";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(RDMActions.Engagement.ActionId, RDMActions.Engagement.Name)
                    .AsMovement().Target(target.Name?.TextValue)
                    .Reason("Engagement - melee range oGCD",
                        "Engagement is a melee-range oGCD with 2 charges. Safer than Displacement which backsteps.")
                    .Factors($"Charges: {context.EngagementCharges}", inBurst ? "In burst window" : "Capped charges")
                    .Alternatives("Hold for burst window", "Use Displacement for backflip")
                    .Tip("Engagement is safer than Displacement.")
                    .Concept(RdmConcepts.MeleePositioning)
                    .Record();
                context.TrainingService?.RecordConceptApplication(RdmConcepts.MeleePositioning, true, "Engagement used safely");
            });
    }

    private void TryPushPostComboRetreat(ICirceContext context, RotationScheduler scheduler, IBattleChara? target)
    {
        if (target == null || !context.Configuration.RedMage.EnableEngagement)
            return;

        if (!WasResolutionJustUsed(context, withinSeconds: 4f))
            return;

        if (!context.Configuration.RedMage.PreferEngagementOverDisplacement
            && context.Player.Level >= RDMActions.Displacement.MinLevel
            && context.EngagementCharges > 0
            && context.ActionService.IsActionReady(RDMActions.Displacement.ActionId))
        {
            scheduler.PushOgcd(CirceAbilities.Displacement, target.GameObjectId, priority: 4,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = RDMActions.Displacement.Name;
                    context.Debug.BuffState = "Displacement (retreat)";
                    context.Debug.DamageState = "Displacement retreat post-combo";
                });
        }
    }

    private static bool WasResolutionJustUsed(ICirceContext context, float withinSeconds)
    {
        var history = context.ActionTracker.GetHistory();
        if (history.Count == 0)
            return false;

        // GetHistory() is newest-first (ring buffer AddFirst): index 0 is the latest attempt.
        var latest = history[0];
        if (latest.Result != ActionResult.Success)
            return false;

        if (latest.ActionId != RDMActions.Resolution.ActionId)
            return false;

        return (DateTime.UtcNow - latest.Timestamp).TotalSeconds <= withinSeconds;
    }

    private void TryPushAcceleration(ICirceContext context, RotationScheduler scheduler)
    {
        if (!context.Configuration.RedMage.EnableAcceleration) return;
        var player = context.Player;
        if (player.Level < RDMActions.Acceleration.MinLevel) return;
        if (context.AccelerationCharges == 0) return;
        if (context.HasAcceleration) return;
        if (context.IsInMeleeCombo) return;
        if (context.HasBothProcs) return;

        var noProcs = !context.HasVerfire && !context.HasVerstone;
        var capped = context.AccelerationCharges >= 2;
        if (!noProcs && !capped) return;

        scheduler.PushOgcd(CirceAbilities.Acceleration, player.GameObjectId, priority: 5,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = RDMActions.Acceleration.Name;
                context.Debug.BuffState = $"Acceleration ({context.AccelerationCharges - 1} charges)";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(RDMActions.Acceleration.ActionId, RDMActions.Acceleration.Name)
                    .AsCasterProc("Acceleration")
                    .Reason("Acceleration - guaranteed proc + instant cast",
                        "Acceleration makes your next Verthunder/Veraero instant and guarantees Verfire/Verstone procs.")
                    .Factors($"Charges: {context.AccelerationCharges}", noProcs ? "No procs active" : "Capped charges")
                    .Alternatives("Wait for proc usage", "Save for movement")
                    .Tip("Use Acceleration when you have no procs to guarantee one.")
                    .Concept(RdmConcepts.Acceleration)
                    .Record();
                context.TrainingService?.RecordConceptApplication(RdmConcepts.Acceleration, true, "Guaranteed proc generation");
            });
    }

    private void TryPushLucidDreaming(ICirceContext context, RotationScheduler scheduler)
    {
        if (!context.Configuration.CasterShared.EnableLucidDreaming) return;

        RoleActionPushers.TryPushLucidDreaming(
            context, scheduler, CirceAbilities.LucidDreaming,
            mpThresholdPct: context.Configuration.CasterShared.LucidDreamingThreshold,
            priority: 6,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = RoleActions.LucidDreaming.Name;
                context.Debug.BuffState = "Lucid Dreaming (MP)";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(RoleActions.LucidDreaming.ActionId, RoleActions.LucidDreaming.Name)
                    .AsCasterResource("MP", context.CurrentMp)
                    .Reason("Lucid Dreaming - MP recovery",
                        "Lucid Dreaming restores MP over time. Use when below 70% MP.")
                    .Factors($"Current MP: {context.CurrentMp}", $"MP%: {context.MpPercent:P0}")
                    .Alternatives("Wait for lower MP", "Ignore if fight ending")
                    .Tip("Use Lucid Dreaming proactively around 70% MP.")
                    .Concept(RdmConcepts.OgcdWeaving)
                    .Record();
                context.TrainingService?.RecordConceptApplication(RdmConcepts.OgcdWeaving, true, "Lucid Dreaming used for MP");
            });
    }
}
