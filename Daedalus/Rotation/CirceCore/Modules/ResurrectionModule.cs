using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using Daedalus.Data;
using Daedalus.Rotation.CirceCore.Abilities;
using Daedalus.Rotation.CirceCore.Context;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Services.Party;

namespace Daedalus.Rotation.CirceCore.Modules;

/// <summary>
/// RDM resurrection module (scheduler-driven).
/// Handles Verraise using Dualcast or Swiftcast for instant casts.
/// RDM hardcast Jolt → Dualcast proc → instant Verraise.
/// </summary>
public sealed class ResurrectionModule : ICirceModule
{
    public int Priority => 15;
    public string Name => "Resurrection";

    private const ushort RaiseStatusId = 148;

    public bool TryExecute(ICirceContext context, bool isMoving) => false;

    public void UpdateDebugState(ICirceContext context) { }

    public void CollectCandidates(ICirceContext context, RotationScheduler scheduler, bool isMoving)
    {
        if (!context.Configuration.RedMage.EnableVerraise) return;
        var player = context.Player;
        if (player.Level < RDMActions.Verraise.MinLevel) return;
        if (player.CurrentMp < 2400) return;

        var deadTarget = FindDeadPartyMember(context);
        if (deadTarget == null) return;

        var partyCoord = context.PartyCoordinationService;
        if (partyCoord?.IsRaiseTargetReservedByOther((uint)deadTarget.GameObjectId) == true)
        {
            context.Debug.PlanningState = "Raise reserved by other";
            return;
        }

        var useDualcast = context.Configuration.RedMage.UseDualcastForVerraise;

        // Swiftcast is used when: no instant available AND (UseDualcastForVerraise=false OR no Dualcast available)
        if (!context.HasDualcast && !context.HasSwiftcast && context.SwiftcastReady && !useDualcast)
        {
            scheduler.PushOgcd(CirceAbilities.Swiftcast, player.GameObjectId, priority: 1,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = "Swiftcast (for Verraise)";
                });
        }

        // Verraise fires when instant is available
        // With UseDualcastForVerraise=true, only fire when Dualcast is up (not Swiftcast)
        var canRaise = useDualcast ? context.HasDualcast : (context.HasDualcast || context.HasSwiftcast);
        if (canRaise)
        {
            if (partyCoord?.ReserveRaiseTarget((uint)deadTarget.GameObjectId, RDMActions.Verraise.ActionId, 0, usingSwiftcast: true) == false)
            {
                context.Debug.PlanningState = "Failed to reserve raise target";
                return;
            }

            scheduler.PushGcd(CirceAbilities.Verraise, deadTarget.GameObjectId, priority: 1,
                onDispatched: _ =>
                {
                    var targetName = deadTarget.Name?.TextValue ?? "Unknown";
                    var method = context.HasDualcast ? "Dualcast" : "Swiftcast";
                    context.Debug.PlannedAction = $"Verraise ({method})";
                    context.Debug.PlanningState = $"Raising {targetName}";
                });
        }
    }

    private IBattleChara? FindDeadPartyMember(ICirceContext context)
    {
        var player = context.Player;
        var rangeSquared = RDMActions.Verraise.RangeSquared;

        // Raid triage order (2026-07-18): healers → tanks → DPS, not party-list order.
        IBattleChara? best = null;
        var bestRank = int.MaxValue;
        foreach (var member in context.PartyHelper.GetAllPartyMembers(player, includeDead: true))
        {
            if (member.EntityId == player.EntityId) continue;
            if (!member.IsDead) continue;
            if (HasRaiseStatus(member)) continue;
            if (Vector3.DistanceSquared(player.Position, member.Position) > rangeSquared) continue;

            var jobId = Daedalus.Rotation.Common.Helpers.TrustPartyRoleHelper.ResolveJobId(member, context.PartyList);
            var rank = JobRegistry.IsHealer(jobId) ? 0 : JobRegistry.IsTank(jobId) ? 1 : 2;
            if (rank < bestRank)
            {
                best = member;
                bestRank = rank;
                if (rank == 0) break;
            }
        }
        return best;
    }

    private static bool HasRaiseStatus(IBattleChara chara)
    {
        if (chara.StatusList == null) return false;
        foreach (var status in chara.StatusList)
            if (status.StatusId == RaiseStatusId) return true;
        return false;
    }
}
