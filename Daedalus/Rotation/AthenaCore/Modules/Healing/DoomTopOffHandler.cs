using System;
using Dalamud.Game.ClientState.Objects.Types;
using Daedalus.Data;
using Daedalus.Rotation.AthenaCore.Abilities;
using Daedalus.Rotation.AthenaCore.Context;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;

namespace Daedalus.Rotation.AthenaCore.Modules.Healing;

/// <summary>
/// Heals a Doomed party member to FULL, ahead of every other heal. Doom (the Necromancer's Deep Freeze puts it on its
/// own caster for 10s) clears only at 100% HP, so the member at 85% is the one about to die -- and every other handler
/// here gates on the target's real HP against a threshold, which a Doomed tank at 85% never crosses. Korha,
/// 2026-09-26, died to it twice while the Sage cast Dosis. Also answers a heavy priority DoT and a top-off announced
/// from another box (<see cref="HealerPartyHelper.NeedsPriorityHealing"/>).
/// <para>Lustrate with any Aetherflow (the reserve does not apply: this is the heal it is kept for); without, Physick standing still -- Adloquium's shield is not HP, and only HP clears Doom.</para>
/// </summary>
public sealed class DoomTopOffHandler(Func<IBattleChara, bool>? needsTopOff = null) : IHealingHandler
{
    public int Priority => 1;
    public string Name => "Doom top-off";

    public void CollectCandidates(IAthenaContext context, RotationScheduler scheduler, bool isMoving)
    {
        var player = context.Player;
        var target = HealerPartyHelper.FindMemberNeedingTopOff(
            player, context.PartyHelper.GetAllPartyMembers(player), SCHActions.Physick.Range, needsTopOff);
        if (target is null) return;

        var level = player.Level;
        var who = target.Name?.TextValue ?? "party member";

        if (level >= SCHActions.Lustrate.MinLevel && context.AetherflowService.CurrentStacks > 0
            && context.ActionService.IsActionReady(SCHActions.Lustrate.ActionId))
        {
            scheduler.PushOgcd(AthenaAbilities.Lustrate, target.GameObjectId, priority: Priority,
                onDispatched: _ =>
                {
                    context.AetherflowService.ConsumeStack();
                    context.Debug.PlannedAction = $"Lustrate (Doom top-off: {who})";
                });
            return;
        }

        if (!isMoving && level >= SCHActions.Physick.MinLevel)
            scheduler.PushGcd(AthenaAbilities.Physick, target.GameObjectId, priority: Priority,
                onDispatched: _ => context.Debug.PlannedAction = $"Physick (Doom top-off: {who})");
    }
}
