using System;
using Dalamud.Game.ClientState.Objects.Types;
using Daedalus.Data;
using Daedalus.Rotation.ApolloCore.Abilities;
using Daedalus.Rotation.ApolloCore.Context;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;

namespace Daedalus.Rotation.ApolloCore.Modules.Healing;

/// <summary>
/// Heals a Doomed party member to FULL, ahead of every other heal. Doom (the Necromancer's Deep Freeze puts it on its
/// own caster for 10s) clears only at 100% HP, so the member at 85% is the one about to die -- and every other handler
/// here gates on the target's real HP against a threshold, which a Doomed tank at 85% never crosses. Korha,
/// 2026-09-26, died to it twice while the Sage cast Dosis. Also answers a heavy priority DoT and a top-off announced
/// from another box (<see cref="HealerPartyHelper.NeedsPriorityHealing"/>).
/// <para>Benediction first -- it heals to full outright -- else Tetragrammaton; with neither ready, Afflatus Solace (instant, a lily) or Cure II standing still.</para>
/// </summary>
public sealed class DoomTopOffHandler(Func<IBattleChara, bool>? needsTopOff = null) : IHealingHandler
{
    public HealingPriority Priority => HealingPriority.DoomTopOff;
    public string Name => "Doom top-off";

    public void CollectCandidates(IApolloContext context, RotationScheduler scheduler, bool isMoving)
    {
        var player = context.Player;
        var target = HealerPartyHelper.FindMemberNeedingTopOff(
            player, context.PartyHelper.GetAllPartyMembers(player), WHMActions.CureII.Range, needsTopOff);
        if (target is null) return;

        var level = player.Level;
        var ready = context.ActionService;
        var who = target.Name?.TextValue ?? "party member";
        Action<Daedalus.Rotation.Common.IRotationContext> planned(string heal) => _ => context.Debug.PlannedAction = $"{heal} (Doom top-off: {who})";

        if (level >= WHMActions.Benediction.MinLevel && ready.IsActionReady(WHMActions.Benediction.ActionId))
        {
            scheduler.PushOgcd(ApolloAbilities.Benediction, target.GameObjectId, priority: (int)Priority, onDispatched: planned("Benediction"));
            return;
        }

        if (level >= WHMActions.Tetragrammaton.MinLevel && ready.IsActionReady(WHMActions.Tetragrammaton.ActionId))
        {
            scheduler.PushOgcd(ApolloAbilities.Tetragrammaton, target.GameObjectId, priority: (int)Priority, onDispatched: planned("Tetragrammaton"));
            return;
        }

        if (level >= WHMActions.AfflatusSolace.MinLevel && context.LilyCount > 0)
            scheduler.PushGcd(ApolloAbilities.AfflatusSolace, target.GameObjectId, priority: (int)Priority, onDispatched: planned("Afflatus Solace"));
        else if (!isMoving)
            scheduler.PushGcd(level >= WHMActions.CureII.MinLevel ? ApolloAbilities.CureII : ApolloAbilities.Cure,
                target.GameObjectId, priority: (int)Priority, onDispatched: planned("Cure"));
    }
}
