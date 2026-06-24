using System;
using Dalamud.Game.ClientState.Objects.Types;
using Olympus.Config;
using Olympus.Data;
using Olympus.Rotation.AsclepiusCore.Abilities;
using Olympus.Rotation.AsclepiusCore.Context;
using Olympus.Rotation.AsclepiusCore.Helpers;
using Olympus.Rotation.Common.Helpers;
using Olympus.Rotation.Common.Scheduling;
using Olympus.Services.Training;

namespace Olympus.Rotation.AsclepiusCore.Modules.Healing;

/// <summary>
/// Handles single-target oGCD heals for Sage: Druochole and Taurochole.
/// Tank takes priority over lowest-HP DPS when the tank needs emergency healing.
/// </summary>
public sealed class SingleTargetOgcdHandler : IHealingHandler
{
    private const int EmergencyPriority = 5;
    private const int NormalPriority = 12;

    /// <summary>
    /// HP ceiling for dumping Addersgall via Druochole when a new stack is about to cap.
    /// We still have headroom here, so only dump onto a clearly injured ally.
    /// </summary>
    private const float AddersgallDumpThreshold = 0.90f;

    /// <summary>
    /// HP ceiling for dumping Addersgall via Druochole when already at a hard cap (regen frozen).
    /// Above 1.0 so the dump fires regardless of target HP: at a hard cap the stack and its 20s of
    /// regen are being wasted outright, and Druochole still refunds 700 MP and un-sticks the timer
    /// even on a topped-off ally. This is what "Prevent Addersgall Cap" promises — otherwise stacks
    /// sit pinned at 3 during calm phases when nobody is below ~99%.
    /// </summary>
    private const float HardCapDumpThreshold = 1.01f;

    private static readonly string[] _druocholeAlternatives =
    {
        "Taurochole (if tank, adds 10% mit)",
        "Diagnosis (GCD, save Addersgall)",
        "Kardia healing (passive)",
    };

    private static readonly string[] _taurocholeAlternatives =
    {
        "Druochole (no mit, no shared CD)",
        "Kerachole (AoE version)",
        "Haima (multi-hit shield)",
    };

    public int Priority => 10;
    public string Name => "SingleTargetOgcd";

    public void CollectCandidates(IAsclepiusContext context, RotationScheduler scheduler, bool isMoving)
    {
        TryPushTaurochole(context, scheduler);
        TryPushDruochole(context, scheduler);
    }

    private void TryPushDruochole(IAsclepiusContext context, RotationScheduler scheduler)
    {
        var config = context.Configuration.Sage;
        if (!config.EnableDruochole) return;

        var player = context.Player;
        if (player.Level < SGEActions.Druochole.MinLevel) return;
        if (context.AddersgallStacks < 1) { context.Debug.DruocholeState = "No Addersgall"; return; }

        var target = ResolveSingleTargetHeal(context);
        if (target == null) { context.Debug.DruocholeState = "No target"; return; }

        var hpPercent = GetHpPercent(target);

        var addersgall = context.AddersgallService;
        var dumpCeiling = addersgall.IsAtMax ? HardCapDumpThreshold : AddersgallDumpThreshold;
        var capDump = config.PreventAddersgallCap
                      && addersgall.ShouldPreventCap(config.AddersgallCapPreventWindow)
                      && hpPercent <= dumpCeiling;

        if (!CanSpendAddersgall(context, target, hpPercent) && !capDump)
        {
            context.Debug.DruocholeState = $"Reserved ({config.AddersgallReserve})";
            return;
        }

        if (HealerPartyHelper.HasNoHealStatus(target)) { context.Debug.DruocholeState = "Skipped (invuln/delayed heal)"; return; }
        if (context.HealingCoordination.IsTargetReserved(target.EntityId, context.PartyCoordinationService)) { context.Debug.DruocholeState = "Skipped (reserved)"; return; }

        var threshold = GetDruocholeThreshold(context, target);

        // Addersgall cap prevention: when stacks are capped (passive regen is paused) or about to
        // cap, dump Druochole on the most-injured ally so neither the stack nor its 700 MP refund
        // is wasted. At a hard cap the timer is frozen and the stack is fully stuck, so spend it even
        // on a topped-off ally (ceiling > 1.0); while merely about to cap we still have headroom, so
        // only dump onto a clearly injured ally. This self-limits to ~once per regen cycle: spending
        // leaves the cap, the timer restarts, and ShouldPreventCap only re-triggers near the next cap.
        if (hpPercent > threshold && !capDump)
        {
            context.Debug.DruocholeState = $"{hpPercent:P0} > {threshold:P0}";
            return;
        }

        if (capDump && hpPercent > threshold)
            context.Debug.DruocholeState = $"Dump (cap) @ {hpPercent:P0}";

        var capturedTarget = target;
        var capturedHpPercent = hpPercent;
        var capturedStacks = context.AddersgallStacks;
        var action = SGEActions.Druochole;
        var pushPriority = IsEmergency(context, capturedTarget, capturedHpPercent) ? EmergencyPriority : NormalPriority;

        scheduler.PushOgcd(AsclepiusAbilities.Druochole, target.GameObjectId, priority: pushPriority,
            onDispatched: _ =>
            {
                var healAmount = action.HealPotency * 10;
                context.HealingCoordination.TryReserveTarget(
                    capturedTarget.EntityId, context.PartyCoordinationService, healAmount, action.ActionId, 0);

                context.Debug.PlannedAction = action.Name;
                context.Debug.PlanningState = "Druochole";
                context.Debug.DruocholeState = "Executing";
                context.LogAddersgallDecision(action.Name, capturedStacks, $"Target at {capturedHpPercent:P0}");

                if (context.TrainingService?.IsTrainingEnabled == true)
                {
                    var targetName = capturedTarget.Name?.TextValue ?? "Unknown";
                    var shortReason = $"Druochole on {targetName} at {capturedHpPercent:P0} ({capturedStacks} stacks)";
                    var factors = new[]
                    {
                        $"Target HP: {capturedHpPercent:P0}",
                        $"Threshold: {threshold:P0}",
                        $"Addersgall stacks: {capturedStacks}",
                        "600 potency oGCD heal",
                        "Restores 7% MP (700 MP)",
                    };

                    context.TrainingService.RecordDecision(new ActionExplanation
                    {
                        Timestamp = DateTime.UtcNow,
                        ActionId = action.ActionId,
                        ActionName = "Druochole",
                        Category = "Healing",
                        TargetName = targetName,
                        ShortReason = shortReason,
                        DetailedReason = $"Druochole used on {targetName} at {capturedHpPercent:P0} HP with {capturedStacks} Addersgall stacks. 600 potency oGCD heal plus 7% MP restoration. This is SGE's primary Addersgall single-target heal - efficient and free (restores MP!). Use freely when Addersgall is available.",
                        Factors = factors,
                        Alternatives = _druocholeAlternatives,
                        Tip = "Druochole is your bread-and-butter heal! It costs Addersgall but RESTORES MP, making it very efficient. Don't hoard Addersgall - use it! Stacks regenerate automatically.",
                        ConceptId = SgeConcepts.DruocholeUsage,
                        Priority = capturedHpPercent < 0.3f ? ExplanationPriority.Critical : ExplanationPriority.High,
                    });
                }
            });
    }

    private void TryPushTaurochole(IAsclepiusContext context, RotationScheduler scheduler)
    {
        var config = context.Configuration.Sage;
        var player = context.Player;

        if (!config.EnableTaurochole) return;
        if (player.Level < SGEActions.Taurochole.MinLevel) return;
        if (context.AddersgallStacks < 1) { context.Debug.TaurocholeState = "No Addersgall"; return; }
        if (!context.ActionService.IsActionReady(SGEActions.Taurochole.ActionId)) { context.Debug.TaurocholeState = "On CD"; return; }

        var tank = context.PartyHelper.FindTankInParty(player);
        if (tank == null) { context.Debug.TaurocholeState = "No tank"; return; }
        if (HealerPartyHelper.HasNoHealStatus(tank)) { context.Debug.TaurocholeState = "Skipped (invuln)"; return; }
        if (context.HealingCoordination.IsTargetReserved(tank.EntityId, context.PartyCoordinationService)) { context.Debug.TaurocholeState = "Skipped (reserved)"; return; }

        var hpPercent = GetHpPercent(tank);
        var threshold = GetTaurocholeThreshold(context);
        if (hpPercent > threshold) { context.Debug.TaurocholeState = $"Tank at {hpPercent:P0}"; return; }
        if (AsclepiusStatusHelper.HasKerachole(tank) && !IsEmergency(context, tank, hpPercent))
        {
            context.Debug.TaurocholeState = "Already has mit";
            return;
        }

        var capturedTank = tank;
        var capturedHpPercent = hpPercent;
        var capturedStacks = context.AddersgallStacks;
        var action = SGEActions.Taurochole;
        var pushPriority = IsEmergency(context, capturedTank, capturedHpPercent) ? EmergencyPriority - 1 : NormalPriority - 1;

        scheduler.PushOgcd(AsclepiusAbilities.Taurochole, tank.GameObjectId, priority: pushPriority,
            onDispatched: _ =>
            {
                var healAmount = action.HealPotency * 10;
                context.HealingCoordination.TryReserveTarget(
                    capturedTank.EntityId, context.PartyCoordinationService, healAmount, action.ActionId, 0);

                context.Debug.PlannedAction = action.Name;
                context.Debug.PlanningState = "Taurochole";
                context.Debug.TaurocholeState = "Executing";
                context.LogAddersgallDecision(action.Name, capturedStacks, $"Tank at {capturedHpPercent:P0}");

                if (context.TrainingService?.IsTrainingEnabled == true)
                {
                    var tankName = capturedTank.Name?.TextValue ?? "Unknown";
                    var shortReason = $"Taurochole on {tankName} at {capturedHpPercent:P0} - heal + 10% mit";
                    var factors = new[]
                    {
                        $"Tank HP: {capturedHpPercent:P0}",
                        $"Threshold: {threshold:P0}",
                        $"Addersgall stacks: {capturedStacks}",
                        "700 potency heal + 10% mit (15s)",
                        "Shares 45s CD with Kerachole",
                    };

                    context.TrainingService.RecordDecision(new ActionExplanation
                    {
                        Timestamp = DateTime.UtcNow,
                        ActionId = action.ActionId,
                        ActionName = "Taurochole",
                        Category = "Healing",
                        TargetName = tankName,
                        ShortReason = shortReason,
                        DetailedReason = $"Taurochole used on tank {tankName} at {capturedHpPercent:P0} HP with {capturedStacks} Addersgall stacks. 700 potency heal PLUS 10% damage reduction for 15 seconds. Perfect for tank healing + tankbuster mitigation. Shares a 45s CD with Kerachole - plan which you need more!",
                        Factors = factors,
                        Alternatives = _taurocholeAlternatives,
                        Tip = "Taurochole is your best tank heal! The 10% mitigation is fantastic for tankbusters. Remember it shares a 45s cooldown with Kerachole - if you need party mitigation, save for Kerachole instead.",
                        ConceptId = SgeConcepts.TaurocholeUsage,
                        Priority = ExplanationPriority.High,
                    });
                }
            });
    }

    private static IBattleChara? ResolveSingleTargetHeal(IAsclepiusContext context)
    {
        var player = context.Player;
        var tank = context.PartyHelper.FindTankInParty(player);
        if (tank != null && !tank.IsDead)
        {
            var tankHp = GetHpPercent(tank);
            if (tankHp <= GetDruocholeThreshold(context, tank))
                return tank;
        }

        return context.PartyHelper.FindLowestHpPartyMember(player);
    }

    private static bool CanSpendAddersgall(IAsclepiusContext context, IBattleChara target, float hpPercent)
    {
        if (context.AddersgallStacks <= context.Configuration.Sage.AddersgallReserve)
            return IsEmergency(context, target, hpPercent);

        return true;
    }

    private static float GetDruocholeThreshold(IAsclepiusContext context, IBattleChara target)
    {
        var config = context.Configuration;
        var tank = context.PartyHelper.FindTankInParty(context.Player);
        var baseThreshold = tank != null && target.GameObjectId == tank.GameObjectId
            ? Math.Min(config.Sage.DruocholeThreshold, config.Healing.OgcdEmergencyThreshold)
            : config.Sage.DruocholeThreshold;

        // Druochole is a free, MP-restoring oGCD that strictly dominates the Diagnosis GCD heal as a
        // reactive top-up. Never leave a band where Diagnosis would fire but Druochole would not:
        // float the oGCD threshold up to the Diagnosis threshold so the weave-friendly free heal is
        // always the first choice and Addersgall keeps draining instead of capping while the bot
        // hardcasts the weaker, DPS-costing GCD heal.
        baseThreshold = Math.Max(baseThreshold, config.Sage.DiagnosisThreshold);

        // HoT-aware (RSR parity): a target already covered by Kerachole/Physis regen is less urgent,
        // so relax the threshold proportionally to remaining HoT time.
        return AsclepiusStatusHelper.ApplyHotAwareness(baseThreshold, target);
    }

    private static float GetTaurocholeThreshold(IAsclepiusContext context)
        => Math.Min(context.Configuration.Sage.TaurocholeThreshold, context.Configuration.Healing.OgcdEmergencyThreshold);

    private static bool IsEmergency(IAsclepiusContext context, IBattleChara target, float hpPercent)
    {
        if (hpPercent <= context.Configuration.Healing.OgcdEmergencyThreshold)
            return true;

        var tank = context.PartyHelper.FindTankInParty(context.Player);
        return tank != null
               && target.GameObjectId == tank.GameObjectId
               && hpPercent <= context.Configuration.Sage.TaurocholeThreshold;
    }

    private static float GetHpPercent(IBattleChara target)
        => target.MaxHp > 0 ? (float)target.CurrentHp / target.MaxHp : 1f;
}
