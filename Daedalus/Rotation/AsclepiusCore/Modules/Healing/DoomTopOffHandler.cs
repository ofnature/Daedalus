using System;
using Dalamud.Game.ClientState.Objects.Types;
using Daedalus.Data;
using Daedalus.Rotation.AsclepiusCore.Abilities;
using Daedalus.Rotation.AsclepiusCore.Context;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;

namespace Daedalus.Rotation.AsclepiusCore.Modules.Healing;

/// <summary>
/// Heals a Doomed party member to FULL, ahead of every other heal. Doom (the Necromancer's Deep Freeze puts it on its
/// own caster for 10s) clears only at 100% HP, so the member at 85% is the one about to die -- and every other handler
/// here gates on the target's real HP against a threshold, which a Doomed tank at 85% never crosses. Korha,
/// 2026-09-26, died to it twice while the Sage cast Dosis. Also answers a heavy priority DoT and a top-off announced
/// from another box (<see cref="HealerPartyHelper.NeedsPriorityHealing"/>).
/// <para>Druochole with Addersgall, else Taurochole; with neither, Diagnosis standing still. The Addersgall reserve does not apply: this is the heal it is saved for.</para>
/// </summary>
public sealed class DoomTopOffHandler(Func<IBattleChara, bool>? needsTopOff = null) : IHealingHandler
{
    public int Priority => 1;
    public string Name => "Doom top-off";

    public void CollectCandidates(IAsclepiusContext context, RotationScheduler scheduler, bool isMoving)
    {
        var player = context.Player;
        var target = HealerPartyHelper.FindMemberNeedingTopOff(
            player, context.PartyHelper.GetAllPartyMembers(player), SGEActions.Diagnosis.Range, needsTopOff);
        if (target is null) return;

        var level = player.Level;
        var ready = context.ActionService;
        var who = target.Name?.TextValue ?? "party member";
        Action<Daedalus.Rotation.Common.IRotationContext> planned(string heal) => _ => context.Debug.PlannedAction = $"{heal} (Doom top-off: {who})";

        if (context.AddersgallStacks > 0)
        {
            if (level >= SGEActions.Druochole.MinLevel && ready.IsActionReady(SGEActions.Druochole.ActionId))
            {
                scheduler.PushOgcd(AsclepiusAbilities.Druochole, target.GameObjectId, priority: Priority, onDispatched: planned("Druochole"));
                return;
            }

            if (level >= SGEActions.Taurochole.MinLevel && ready.IsActionReady(SGEActions.Taurochole.ActionId))
            {
                scheduler.PushOgcd(AsclepiusAbilities.Taurochole, target.GameObjectId, priority: Priority, onDispatched: planned("Taurochole"));
                return;
            }
        }

        if (!isMoving)
            scheduler.PushGcd(AsclepiusAbilities.Diagnosis, target.GameObjectId, priority: Priority, onDispatched: planned("Diagnosis"));
    }
}
