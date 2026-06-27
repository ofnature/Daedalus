using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Modules;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.HephaestusCore.Abilities;
using Daedalus.Rotation.HephaestusCore.Context;
using Daedalus.Services;
using Daedalus.Services.Training;

namespace Daedalus.Rotation.HephaestusCore.Modules;

/// <summary>
/// Handles the Gunbreaker buff management.
/// Manages tank stance, No Mercy, and Bloodfest.
/// </summary>
public sealed class BuffModule : BaseTankBuffModule<IHephaestusContext>, IHephaestusModule
{
    private readonly IBurstWindowService? _burstWindowService;

    public BuffModule(IBurstWindowService? burstWindowService = null)
    {
        _burstWindowService = burstWindowService;
    }

    private bool ShouldHoldForBurst(float thresholdSeconds = 8f) =>
        BurstHoldHelper.ShouldHoldForBurst(_burstWindowService, thresholdSeconds);

    #region Abstract Method Implementations

    protected override ActionDefinition GetTankStanceAction() => GNBActions.RoyalGuard;

    protected override bool HasJobTankStance(IHephaestusContext context) => context.HasRoyalGuard;

    protected override void SetBuffState(IHephaestusContext context, string state)
        => context.Debug.BuffState = state;

    protected override void SetPlannedAction(IHephaestusContext context, string action)
        => context.Debug.PlannedAction = action;

    #endregion

    public override bool TryExecute(IHephaestusContext context, bool isMoving) => false;

    #region Job-Specific Overrides

    protected override bool TryJobSpecificBuffs(IHephaestusContext context) => false;

    protected override bool TryJobSpecificResourceGeneration(IHephaestusContext context) => false;

    #endregion

    #region CollectCandidates

    public void CollectCandidates(IHephaestusContext context, RotationScheduler scheduler, bool isMoving)
    {
        if (!context.InCombat)
        {
            context.Debug.BuffState = "Not in combat";
            return;
        }

        // Royal Guard (tank stance) — was never pushed (this override skipped it, unlike the other tanks),
        // so GNB never auto-enabled its stance. Push before the damage gate so stance applies even if
        // damage is disabled.
        TryPushTankStance(context, scheduler);

        if (!IsDamageEnabled(context))
        {
            context.Debug.BuffState = "Damage disabled";
            return;
        }

        TryPushNoMercy(context, scheduler);
        TryPushBloodfest(context, scheduler);
    }

    private void TryPushTankStance(IHephaestusContext context, RotationScheduler scheduler)
    {
        var player = context.Player;
        if (player.Level < GNBActions.RoyalGuard.MinLevel) return;
        if (!context.Configuration.Tank.AutoTankStance)
        {
            context.Debug.BuffState = "AutoTankStance disabled";
            return;
        }
        if (context.HasRoyalGuard) return;
        if (!context.ActionService.IsActionReady(GNBActions.RoyalGuard.ActionId)) return;

        scheduler.PushOgcd(GnbAbilities.RoyalGuard, player.GameObjectId, priority: 1,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = GNBActions.RoyalGuard.Name;
                context.Debug.BuffState = "Enabling Royal Guard";
            });
    }

    private void TryPushNoMercy(IHephaestusContext context, RotationScheduler scheduler)
    {
        if (!context.Configuration.Tank.EnableNoMercy) return;

        var player = context.Player;
        var level = player.Level;

        if (level < GNBActions.NoMercy.MinLevel)
            return;

        if (context.HasNoMercy)
        {
            context.Debug.BuffState = $"No Mercy ({context.NoMercyRemaining:F1}s)";
            return;
        }

        if (level >= GNBActions.DoubleDown.MinLevel && context.Cartridges < 1)
        {
            context.Debug.BuffState = "No Mercy: waiting for cartridges";
            return;
        }

        if (!context.ActionService.IsActionReady(GNBActions.NoMercy.ActionId))
            return;

        if (ShouldHoldForBurst(8f))
        {
            context.Debug.BuffState = "Holding No Mercy for burst";
            return;
        }

        scheduler.PushOgcd(
            GnbAbilities.NoMercy,
            player.GameObjectId,
            priority: 1,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = GNBActions.NoMercy.Name;
                context.Debug.BuffState = "No Mercy activated";

                TrainingHelper.Decision(context.TrainingService)
                    .Action(GNBActions.NoMercy.ActionId, GNBActions.NoMercy.Name)
                    .AsTankBurst()
                    .Target("Self")
                    .Reason(
                        $"Activating No Mercy with {context.Cartridges} cartridge(s)",
                        "No Mercy is GNB's main damage buff (+20% for 20 seconds, 60s cooldown). " +
                        "The goal is to fit as many high-potency abilities into this window as possible: " +
                        "Double Down, Gnashing Fang combo, Sonic Break, Blasting Zone, and Bow Shock.")
                    .Factors($"Have {context.Cartridges} cartridge(s) to spend", "60s cooldown ready", "Will enable +20% damage for 20 seconds")
                    .Alternatives("Wait for more cartridges (risk holding too long)", "Wait for Gnashing Fang CD (minor optimization)")
                    .Tip("No Mercy is your burst window - plan to have Double Down (2 carts) and Gnashing Fang ready when you press it.")
                    .Concept("gnb_no_mercy")
                    .Record();
                context.TrainingService?.RecordConceptApplication("gnb_no_mercy", true, "Burst window activation");
            });
    }

    private void TryPushBloodfest(IHephaestusContext context, RotationScheduler scheduler)
    {
        if (!context.Configuration.Tank.EnableBloodfest) return;

        var player = context.Player;
        var level = player.Level;

        if (level < GNBActions.Bloodfest.MinLevel)
            return;

        // Diagnostic: surface Bloodfest's live readiness so we can see whether its 60s cooldown is being
        // read at all (it was being re-dispatched every GCD / "rejected").
        var bfReady = context.ActionService.IsActionReady(GNBActions.Bloodfest.ActionId);
        var bfCd = context.ActionService.GetCooldownRemaining(GNBActions.Bloodfest.ActionId);
        var bfStatus = context.ActionService.GetActionStatusCode(GNBActions.Bloodfest.ActionId);
        context.Debug.BloodfestDiag = $"ready={bfReady} cd={bfCd:F0}s status={bfStatus}";

        if (!context.ActionService.IsActionReady(GNBActions.Bloodfest.ActionId))
            return;

        // Bloodfest is flagged CanTargetSelf=false / CanTargetHostile=true in the game data: despite being a
        // self-buff (grants cartridges), UseAction silently no-ops unless it's dispatched at a hostile target.
        // Dispatching with player.GameObjectId left it perpetually "ready" but never casting. Require an enemy.
        var bloodfestTarget = context.CurrentTarget;
        if (bloodfestTarget is null)
        {
            context.Debug.BuffState = "Bloodfest: no enemy target";
            return;
        }

        // Patch 7.4: Bloodfest is a 60s cooldown (down from 120s) that grants a temporary 6-cartridge
        // cap for 30s, so it can no longer overcap the gauge, and at Lv100 it grants Ready to Reign.
        // It therefore belongs in *every* No Mercy window regardless of current cartridge count. The
        // old "spend down to <=1 cartridge first" gate (maxBenefit < 2) is obsolete and was causing
        // Bloodfest (and the Reign of Beasts combo it enables) to be skipped when entering No Mercy
        // with 2-3 cartridges. Use it while No Mercy is active or imminent so Ready to Reign and the
        // bonus cartridges are spent inside the buff window.
        var nmCooldown = context.ActionService.GetCooldownRemaining(GNBActions.NoMercy.ActionId);
        var noMercyReadyOrImminent = context.HasNoMercy || nmCooldown < 7f;
        if (!noMercyReadyOrImminent)
        {
            context.Debug.BuffState = $"Bloodfest: holding for No Mercy ({nmCooldown:F0}s)";
            return;
        }

        if (!context.HasNoMercy && ShouldHoldForBurst(8f))
        {
            context.Debug.BuffState = "Holding Bloodfest for burst";
            return;
        }

        var duringNoMercy = context.HasNoMercy;
        scheduler.PushOgcd(
            GnbAbilities.Bloodfest,
            bloodfestTarget.GameObjectId,
            priority: 2,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = GNBActions.Bloodfest.Name;
                context.Debug.BuffState = $"Bloodfest (+{GNBActions.BloodfestCartridges} cartridges)";

                TrainingHelper.Decision(context.TrainingService)
                    .Action(GNBActions.Bloodfest.ActionId, GNBActions.Bloodfest.Name)
                    .AsTankResource(context.Cartridges)
                    .Reason(
                        duringNoMercy
                            ? $"Bloodfest during No Mercy ({context.Cartridges} -> 3 cartridges)"
                            : $"Bloodfest to refill cartridges ({context.Cartridges} -> 3)",
                        "Bloodfest instantly grants 3 cartridges (60s cooldown). " +
                        "At Lv.100+, also grants Ready to Reign for the Reign of Beasts combo. " +
                        "Best used when cartridges are low (0-1) during No Mercy to maximize burst damage.")
                    .Factors(
                        $"Currently at {context.Cartridges} cartridges",
                        $"Will gain {GNBActions.BloodfestCartridges - context.Cartridges} net cartridges",
                        duringNoMercy ? "No Mercy active - can spend immediately" : "Refilling for next burst window",
                        player.Level >= 100 ? "Grants Ready to Reign at Lv.100" : "Below Lv.100 - no Reign combo")
                    .Alternatives("Wait for No Mercy (might delay too long)", "Use basic combo to generate cartridges slowly")
                    .Tip("Bloodfest aligns with No Mercy every other burst window. Plan your cartridge spending so you're at 0-1 when Bloodfest is ready.")
                    .Concept("gnb_bloodfest")
                    .Record();
                context.TrainingService?.RecordConceptApplication("gnb_cartridge_gauge", true, "Cartridge refill");
            });
    }

    #endregion

}
