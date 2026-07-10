using System;
using Dalamud.Game.ClientState.Objects.Types;
using Daedalus.Models.Action;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Services.Party;

namespace Daedalus.Rotation.Common.Helpers;

/// <summary>
/// Shared driver for the coordinated tank-swap handshake — the four tank EnmityModules call this so
/// the Provoke/Shirk choreography lives in one place. Builds a <see cref="TankSwapInputs"/> snapshot
/// from live state, runs the job-agnostic <see cref="TankSwapSequencer"/>, and executes the resulting
/// action (pre-mitigate + request / Provoke / confirm / Shirk) through the scheduler + coordination
/// service. Returns true when a deliberate swap is in flight this frame, so the caller skips its
/// reactive lost-aggro / off-tank-drop branches (and never cross-Provokes).
/// </summary>
public static class TankSwapDriver
{
    /// <summary>Deliberate swaps out-rank the reactive lost-aggro Provoke (priority 1).</summary>
    private const int SwapPriority = 2;

    public static bool TryDriveSwap(
        ITankRotationContext context,
        RotationScheduler scheduler,
        TankSwapSequencer sequencer,
        IBattleChara target,
        IBattleChara? coTank,
        AbilityBehavior provoke,
        AbilityBehavior shirk)
    {
        var partyCoord = context.PartyCoordinationService;
        var config = context.Configuration.Tank;

        if (partyCoord == null || !config.EnableTankSwap || target == null)
        {
            sequencer.Reset();
            return false;
        }

        var player = context.Player;
        var targetEntityId = (uint)target.GameObjectId;
        var pending = partyCoord.GetPendingTankSwapRequest(targetEntityId);

        var inputs = new TankSwapInputs
        {
            SwapEnabled = true,
            TargetEntityId = targetEntityId,
            LocalHoldsAggro = context.EnmityService.IsMainTankOn(target, player.EntityId),
            CoTankHoldsAggro = context.EnmityService.HasCoTankAggro(target, player.EntityId),
            HasRemoteTank = partyCoord.HasRemoteTank,
            IsDesignatedOffTank = partyCoord.LocalTankSwapRole == TankSwapRole.DesignatedOffTank,
            ManualTriggerActive = partyCoord.IsManualSwapArmed(),
            AutoTriggerActive = config.AutoTankSwap && HasStackTrigger(coTank, config.TankSwapStackCount),
            PendingTakeRequestFromCoTank = pending is { IntendToTakeAggro: true },
            HasConfirmation = partyCoord.HasSwapConfirmation(targetEntityId),
            WasRecentGiver = partyCoord.WasRecentSwapGiver(targetEntityId),
            SwapInProgress = partyCoord.IsTankSwapInProgress(targetEntityId),
            ProvokeReady = player.Level >= provoke.Action.MinLevel
                           && context.ActionService.IsActionReady(provoke.Action.ActionId),
            ShirkReady = player.Level >= shirk.Action.MinLevel
                         && context.ActionService.IsActionReady(shirk.Action.ActionId),
        };

        var now = DateTime.UtcNow;
        var decision = sequencer.Evaluate(inputs, now);

        switch (decision)
        {
            case TankSwapDecision.PreMitigateAndRequest:
                if (config.PreSwapMitigation)
                    context.TankCooldownService.RequestSwapMitigation();
                partyCoord.RequestTankSwap(targetEntityId, wantToTakeAggro: true, priority: SwapPriority);
                sequencer.NotifyRequested(targetEntityId, now);
                return true;

            case TankSwapDecision.Confirm:
                partyCoord.ConfirmTankSwap(targetEntityId);
                sequencer.NotifyConfirmed(targetEntityId, now);
                return true;

            case TankSwapDecision.Provoke:
                scheduler.PushOgcd(provoke, target.GameObjectId, SwapPriority, onDispatched: _ =>
                {
                    partyCoord.RecordSwapCompleted(targetEntityId, tookAggro: true);
                    partyCoord.ClearTankSwapReservation(targetEntityId);
                    sequencer.NotifyProvoked();
                });
                return true;

            case TankSwapDecision.Shirk:
                if (coTank == null)
                    return true; // stay in AwaitingFlip; the flip timeout will retire it
                scheduler.PushOgcd(shirk, coTank.GameObjectId, SwapPriority, onDispatched: _ =>
                {
                    partyCoord.RecordSwapCompleted(targetEntityId, tookAggro: false);
                    partyCoord.ClearTankSwapReservation(targetEntityId);
                    sequencer.NotifyShirked();
                });
                return true;

            default:
                // Nothing to do this frame, but if a sequence is mid-flight suppress the caller's
                // reactive branches so they can't cross-Provoke while we await confirm / flip.
                return sequencer.IsActive;
        }
    }

    /// <summary>
    /// True when the co-tank carries any status stacked to the configured threshold — the fallback
    /// auto-swap trigger for content without a BMR buster prediction. No detrimental-only filter in
    /// v1 (opt-in + threshold keep the risk with the operator).
    /// </summary>
    private static bool HasStackTrigger(IBattleChara? coTank, int threshold)
    {
        if (coTank?.StatusList == null)
            return false;

        foreach (var status in coTank.StatusList)
        {
            // Param carries the stack count for stackable statuses (same read as Soteria stacks).
            if (status != null && status.Param >= threshold)
                return true;
        }

        return false;
    }
}
