using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using Daedalus.Data;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.PersephoneCore.Abilities;
using Daedalus.Rotation.PersephoneCore.Context;

namespace Daedalus.Rotation.PersephoneCore.Modules;

/// <summary>
/// SMN resurrection module (raid audit 2026-07-18 — Summoner had NO raise handling at all,
/// despite being the classic battle-rez third in raid comps). Swiftcast → Resurrection (173)
/// on the triage-ordered dead member (healers → tanks → DPS); hardcast per the global
/// Resurrection config; IPC raise reservation prevents double-raises across boxes.
/// </summary>
public sealed class ResurrectionModule : IPersephoneModule
{
    public int Priority => 15;
    public string Name => "Resurrection";

    private const ushort RaiseStatusId = 148;

    public bool TryExecute(IPersephoneContext context, bool isMoving) => false;

    public void UpdateDebugState(IPersephoneContext context) { }

    public void CollectCandidates(IPersephoneContext context, RotationScheduler scheduler, bool isMoving)
    {
        var config = context.Configuration.Resurrection;
        if (!config.EnableRaise) return;

        var player = context.Player;
        if (player.Level < RoleActions.Resurrection.MinLevel) return;
        if (player.CurrentMp < RoleActions.Resurrection.MpCost) return;
        if (player.MaxMp > 0 && (float)player.CurrentMp / player.MaxMp < config.RaiseMpThreshold) return;

        var deadTarget = FindDeadPartyMember(context);
        if (deadTarget == null) return;

        var partyCoord = context.PartyCoordinationService;
        if (partyCoord?.IsRaiseTargetReservedByOther((uint)deadTarget.GameObjectId) == true)
        {
            context.Debug.PlanningState = "Raise reserved by other";
            return;
        }

        // Swiftcast first — the raid-standard battle rez. SMN has no Dualcast; without
        // Swiftcast the only path is the 8s hardcast (opt-in below).
        if (!context.HasSwiftcast && context.SwiftcastReady)
        {
            scheduler.PushOgcd(PersephoneAbilities.Swiftcast, player.GameObjectId, priority: 1,
                onDispatched: _ => context.Debug.PlannedAction = "Swiftcast (for Resurrection)");
        }

        if (!context.HasSwiftcast && config.AllowHardcastRaise && isMoving)
        {
            // Hold movement for the hardcast (BMR AI micro-follow otherwise never stops).
            Daedalus.Services.Positional.RaiseCastHold.Request(10f);
            context.Debug.PlanningState = "Stopping to hardcast raise";
            return;
        }
        var canRaiseNow = context.HasSwiftcast || config.AllowHardcastRaise;
        if (!canRaiseNow)
        {
            context.Debug.PlanningState = "Raise waiting for Swiftcast";
            return;
        }

        // Hardcast only when Swiftcast is far away (base-module parity: a <10s Swiftcast is
        // worth waiting for over an 8-second stand-still cast).
        if (!context.HasSwiftcast
            && context.ActionService.GetCooldownRemaining(RoleActions.Swiftcast.ActionId) <= 10f)
        {
            context.Debug.PlanningState = "Raise waiting for Swiftcast (<10s)";
            return;
        }

        var swift = context.HasSwiftcast;
        var reservedTargetId = (uint)deadTarget.GameObjectId;
        scheduler.PushGcd(PersephoneAbilities.Resurrection, deadTarget.GameObjectId, priority: 1,
            onDispatched: _ =>
            {
                // Reserve at DISPATCH, not at push (2026-07-19): reserving while merely a CANDIDATE
                // let a box that kept failing to dispatch re-reserve every frame and lock every
                // other box out of the corpse (500ms-post-completion expiry never lapsed).
                partyCoord?.ReserveRaiseTarget(reservedTargetId, RoleActions.Resurrection.ActionId,
                    swift ? 0 : 8000, usingSwiftcast: swift);
                if (!swift) Daedalus.Services.Positional.RaiseCastHold.Request(9f); // cover the 8s cast
                var targetName = deadTarget.Name?.TextValue ?? "Unknown";
                context.Debug.PlannedAction = swift ? "Resurrection (Swiftcast)" : "Resurrection (Hardcast)";
                context.Debug.PlanningState = $"Raising {targetName}";
            });
    }

    private IBattleChara? FindDeadPartyMember(IPersephoneContext context)
    {
        var player = context.Player;
        var rangeSquared = RoleActions.Resurrection.RangeSquared;

        // Raid triage order: healers → tanks → DPS.
        IBattleChara? best = null;
        var bestRank = int.MaxValue;
        // PersephonePartyHelper yields every party PC including dead ones (no dead filter).
        foreach (var member in context.PartyHelper.GetAllPartyMembers(player))
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

        // Alliance fallback (raid, 2026-07-19): raise spells can target any alliance member.
        if (best == null && context.Configuration.Resurrection.RaiseAllianceMembers)
            best = Daedalus.Rotation.Common.Helpers.HealerPartyHelper.FindDeadAllianceMemberNeedingRaise(
                context.ObjectTable, player, rangeSquared);
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
