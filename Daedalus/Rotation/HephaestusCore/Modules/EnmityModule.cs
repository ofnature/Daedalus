using System;
using Daedalus.Data;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.HephaestusCore.Abilities;
using Daedalus.Rotation.HephaestusCore.Context;
using Daedalus.Services.Party;
using Daedalus.Services.Training;

namespace Daedalus.Rotation.HephaestusCore.Modules;

/// <summary>
/// Handles Gunbreaker enmity management: coordinated tank swaps (via <see cref="TankSwapDriver"/>)
/// plus reactive emergency Provoke and proactive off-tank Shirk.
/// </summary>
public sealed class EnmityModule : IHephaestusModule
{
    public int Priority => 5;
    public string Name => "Enmity";

    private DateTime _lastProvokeTime = DateTime.MinValue;
    private readonly TankSwapSequencer _swapSequencer = new();

    public bool TryExecute(IHephaestusContext context, bool isMoving) => false;

    public void UpdateDebugState(IHephaestusContext context) { }

    public void CollectCandidates(IHephaestusContext context, RotationScheduler scheduler, bool isMoving)
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

        if (target != null)
        {
            var coTank = context.PartyHelper.FindCoTank(player);
            if (TankSwapDriver.TryDriveSwap(
                    context, scheduler, _swapSequencer, target, coTank,
                    GnbAbilities.Provoke, GnbAbilities.Shirk))
            {
                context.Debug.EnmityState = $"Coordinated swap ({_swapSequencer.Phase})";
                return;
            }
        }

        TryPushEmergencyProvoke(context, scheduler, target);
        TryPushProactiveShirk(context, scheduler);
    }

    private void TryPushEmergencyProvoke(IHephaestusContext context, RotationScheduler scheduler,
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
        scheduler.PushOgcd(GnbAbilities.Provoke, target.GameObjectId, priority: 1,
            onDispatched: _ =>
            {
                _lastProvokeTime = DateTime.UtcNow;
                context.Debug.PlannedAction = RoleActions.Provoke.Name;
                context.Debug.EnmityState = "Provoking (losing aggro)";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(RoleActions.Provoke.ActionId, RoleActions.Provoke.Name)
                    .AsEnmity()
                    .Target(targetName)
                    .Reason("Emergency Provoke - losing aggro to a non-tank.", "Provoke instantly puts you at top of enmity list.")
                    .Factors("Lost aggro to non-tank", $"Target: {targetName}")
                    .Alternatives("Let co-tank take it", "Use enmity combo")
                    .Tip("If losing aggro, Provoke immediately.")
                    .Concept("gnb_provoke")
                    .Record();
                context.TrainingService?.RecordConceptApplication("gnb_provoke", true, "Emergency aggro recovery");
            });
    }

    private void TryPushProactiveShirk(IHephaestusContext context, RotationScheduler scheduler)
    {
        var player = context.Player;
        if (player.Level < RoleActions.Shirk.MinLevel) return;
        if (!context.Configuration.Tank.AutoShirk) return;

        var target = context.TargetingService.FindEnemyForAction(
            context.Configuration.Targeting.EnemyStrategy, GNBActions.KeenEdge.ActionId, player);
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
        scheduler.PushOgcd(GnbAbilities.Shirk, coTank.GameObjectId, priority: 1,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = RoleActions.Shirk.Name;
                context.Debug.EnmityState = "Shirking to co-tank";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(RoleActions.Shirk.ActionId, RoleActions.Shirk.Name)
                    .AsEnmity()
                    .Target(coTankName)
                    .Reason("Proactive Shirk - off-tank position.", "Shirk transfers 25% of enmity.")
                    .Factors("Off-tank position (#2)", $"Co-tank: {coTankName}")
                    .Alternatives("Stop DPSing", "Let main tank Provoke")
                    .Tip("As off-tank, Shirk periodically.")
                    .Concept("gnb_shirk")
                    .Record();
                context.TrainingService?.RecordConceptApplication("gnb_shirk", true, "Off-tank enmity management");
            });
    }
}
