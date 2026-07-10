using System;
using Daedalus.Data;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.ThemisCore.Abilities;
using Daedalus.Rotation.ThemisCore.Context;
using Daedalus.Services.Party;
using Daedalus.Services.Training;

namespace Daedalus.Rotation.ThemisCore.Modules;

/// <summary>
/// Handles Paladin enmity management: coordinated tank swaps (via <see cref="TankSwapDriver"/>) plus
/// reactive emergency Provoke and proactive off-tank Shirk. Coordinates with other Daedalus tanks
/// via IPC/LAN.
/// </summary>
public sealed class EnmityModule : IThemisModule
{
    public int Priority => 5; // Highest priority - enmity management is critical
    public string Name => "Enmity";

    private DateTime _lastProvokeTime = DateTime.MinValue;
    private readonly TankSwapSequencer _swapSequencer = new();

    public bool TryExecute(IThemisContext context, bool isMoving) => false;

    public void UpdateDebugState(IThemisContext context)
    {
        // Debug state updated during CollectCandidates
    }

    #region CollectCandidates (scheduler path)

    public void CollectCandidates(IThemisContext context, RotationScheduler scheduler, bool isMoving)
    {
        if (!context.InCombat)
        {
            context.Debug.EnmityState = "Not in combat";
            _swapSequencer.Reset();
            return;
        }

        var player = context.Player;
        var target = context.TargetingService.FindEnemy(
            context.Configuration.Targeting.EnemyStrategy, 25f, player);

        // Deliberate coordinated swap runs first; when it's driving, skip the reactive branches so
        // the two tanks never cross-Provoke.
        if (target != null)
        {
            var coTank = context.PartyHelper.FindCoTank(player);
            if (TankSwapDriver.TryDriveSwap(
                    context, scheduler, _swapSequencer, target, coTank,
                    ThemisAbilities.Provoke, ThemisAbilities.Shirk))
            {
                context.Debug.EnmityState = $"Coordinated swap ({_swapSequencer.Phase})";
                return;
            }
        }

        TryPushEmergencyProvoke(context, scheduler, target);
        TryPushProactiveShirk(context, scheduler);
    }

    /// <summary>Reactive reclaim only — a NON-tank ripped the boss and we should be tanking it.</summary>
    private void TryPushEmergencyProvoke(IThemisContext context, RotationScheduler scheduler,
        Dalamud.Game.ClientState.Objects.Types.IBattleNpc? target)
    {
        var player = context.Player;
        if (player.Level < RoleActions.Provoke.MinLevel) return;
        if (!context.Configuration.Tank.AutoProvoke)
        {
            context.Debug.EnmityState = "AutoProvoke disabled";
            return;
        }
        if (target == null)
        {
            context.Debug.EnmityState = "No target";
            return;
        }

        var partyCoord = context.PartyCoordinationService;
        var targetEntityId = (uint)target.GameObjectId;

        if (!context.EnmityService.IsLosingAggro(target, player.EntityId))
        {
            var position = context.EnmityService.GetEnmityPosition(target, player.EntityId);
            context.Debug.EnmityState = position == 1 ? "Main tank" : $"Position {position}";
            return;
        }

        // Anti-ping-pong: don't reclaim right after handing aggro away, and the designated off-tank
        // never reactively grabs the boss (it only takes aggro through the deliberate swap).
        if (partyCoord?.WasRecentSwapGiver(targetEntityId) == true)
        {
            context.Debug.EnmityState = "Post-swap hold";
            return;
        }
        if (partyCoord?.LocalTankSwapRole == TankSwapRole.DesignatedOffTank)
        {
            context.Debug.EnmityState = "Off-tank (no reactive Provoke)";
            return;
        }

        // Boss on the co-tank = a swap situation, not an emergency — let the deliberate path own it.
        if (context.EnmityService.HasCoTankAggro(target, player.EntityId))
        {
            context.Debug.EnmityState = "Co-tank has aggro (swap)";
            return;
        }

        var timeSinceLastProvoke = (DateTime.UtcNow - _lastProvokeTime).TotalSeconds;
        if (timeSinceLastProvoke < context.Configuration.Tank.ProvokeDelay)
        {
            context.Debug.EnmityState = $"Provoke cooldown ({context.Configuration.Tank.ProvokeDelay - timeSinceLastProvoke:F1}s)";
            return;
        }

        if (!context.ActionService.IsActionReady(RoleActions.Provoke.ActionId))
        {
            context.Debug.EnmityState = "Provoke on CD";
            return;
        }

        var targetName = target.Name?.TextValue;
        scheduler.PushOgcd(ThemisAbilities.Provoke, target.GameObjectId, priority: 1,
            onDispatched: _ =>
            {
                _lastProvokeTime = DateTime.UtcNow;
                context.Debug.PlannedAction = RoleActions.Provoke.Name;
                context.Debug.EnmityState = "Provoking (losing aggro)";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(RoleActions.Provoke.ActionId, RoleActions.Provoke.Name)
                    .AsEnmity()
                    .Target(targetName)
                    .Reason(
                        "Emergency Provoke - losing aggro to a non-tank.",
                        "Provoke instantly puts you at top of enmity list. Use it when losing aggro unexpectedly to reclaim the boss.")
                    .Factors("Lost aggro to non-tank", "Boss about to attack party", $"Target: {targetName}")
                    .Alternatives("Let co-tank take it (risky if they're not ready)", "Use more enmity combos (too slow in emergencies)")
                    .Tip("If losing aggro as main tank, Provoke immediately.")
                    .Concept("pld_provoke")
                    .Record();
                context.TrainingService?.RecordConceptApplication("pld_provoke", true, "Emergency aggro recovery");
            });
    }

    /// <summary>Proactive off-tank enmity shed (opt-in AutoShirk) — deliberate swaps go through the driver.</summary>
    private void TryPushProactiveShirk(IThemisContext context, RotationScheduler scheduler)
    {
        var player = context.Player;
        if (player.Level < RoleActions.Shirk.MinLevel) return;
        if (!context.Configuration.Tank.AutoShirk) return;

        var target = context.TargetingService.FindEnemyForAction(
            context.Configuration.Targeting.EnemyStrategy, PLDActions.FastBlade.ActionId, player);
        if (target == null) return;

        if (!context.EnmityService.HasCoTankAggro(target, player.EntityId)) return;

        var coTank = context.PartyHelper.FindCoTank(player);
        if (coTank == null)
        {
            context.Debug.EnmityState = "No co-tank found";
            return;
        }

        var dx = player.Position.X - coTank.Position.X;
        var dy = player.Position.Y - coTank.Position.Y;
        var dz = player.Position.Z - coTank.Position.Z;
        var distance = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (distance > 25f)
        {
            context.Debug.EnmityState = "Co-tank too far for Shirk";
            return;
        }

        if (!context.ActionService.IsActionReady(RoleActions.Shirk.ActionId))
        {
            context.Debug.EnmityState = "Shirk on CD";
            return;
        }

        if (context.EnmityService.GetEnmityPosition(target, player.EntityId) != 2)
        {
            context.Debug.EnmityState = "Not off-tanking";
            return;
        }

        var coTankName = coTank.Name?.TextValue;
        scheduler.PushOgcd(ThemisAbilities.Shirk, coTank.GameObjectId, priority: 1,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = RoleActions.Shirk.Name;
                context.Debug.EnmityState = "Shirking to co-tank";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(RoleActions.Shirk.ActionId, RoleActions.Shirk.Name)
                    .AsEnmity()
                    .Target(coTankName)
                    .Reason(
                        "Proactive Shirk - off-tank position, building enmity. Shirk helps main tank.",
                        "Shirk transfers 25% of your enmity. As off-tank, use it to prevent accidentally pulling aggro.")
                    .Factors("Off-tank position (#2)", "Building enmity from DPS rotation", $"Co-tank: {coTankName}")
                    .Alternatives("Stop DPSing (massive damage loss)", "Let main tank use Provoke (wastes their cooldown)")
                    .Tip("As off-tank, Shirk periodically to stay comfortable below the main tank.")
                    .Concept("pld_shirk")
                    .Record();
                context.TrainingService?.RecordConceptApplication("pld_shirk", true, "Off-tank enmity management");
            });
    }

    #endregion
}
