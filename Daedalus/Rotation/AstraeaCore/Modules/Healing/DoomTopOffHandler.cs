using System;
using Dalamud.Game.ClientState.Objects.Types;
using Daedalus.Data;
using Daedalus.Rotation.AstraeaCore.Abilities;
using Daedalus.Rotation.AstraeaCore.Context;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;

namespace Daedalus.Rotation.AstraeaCore.Modules.Healing;

/// <summary>
/// Heals a Doomed party member to FULL, ahead of every other heal. Doom (the Necromancer's Deep Freeze puts it on its
/// own caster for 10s) clears only at 100% HP, so the member at 85% is the one about to die -- and every other handler
/// here gates on the target's real HP against a threshold, which a Doomed tank at 85% never crosses. Korha,
/// 2026-09-26, died to it twice while the Sage cast Dosis. Also answers a heavy priority DoT and a top-off announced
/// from another box (<see cref="HealerPartyHelper.NeedsPriorityHealing"/>).
/// <para>Essential Dignity, else Celestial Intersection; with neither ready, Benefic II standing still or Aspected Benefic on the move.</para>
/// </summary>
public sealed class DoomTopOffHandler(Func<IBattleChara, bool>? needsTopOff = null) : IHealingHandler
{
    public int Priority => 1;
    public string Name => "Doom top-off";

    public bool TryExecute(IAstraeaContext context, bool isMoving) => false;

    public void CollectCandidates(IAstraeaContext context, RotationScheduler scheduler, bool isMoving)
    {
        var player = context.Player;
        var target = HealerPartyHelper.FindMemberNeedingTopOff(
            player, context.PartyHelper.GetAllPartyMembers(player), ASTActions.BeneficII.Range, needsTopOff);
        if (target is null) return;

        var level = player.Level;
        var ready = context.ActionService;
        var who = target.Name?.TextValue ?? "party member";
        Action<Daedalus.Rotation.Common.IRotationContext> planned(string heal) => _ => context.Debug.PlannedAction = $"{heal} (Doom top-off: {who})";

        if (level >= ASTActions.EssentialDignity.MinLevel && ready.IsActionReady(ASTActions.EssentialDignity.ActionId))
        {
            scheduler.PushOgcd(AstraeaAbilities.EssentialDignity, target.GameObjectId, priority: Priority, onDispatched: planned("Essential Dignity"));
            return;
        }

        if (level >= ASTActions.CelestialIntersection.MinLevel && ready.IsActionReady(ASTActions.CelestialIntersection.ActionId))
        {
            scheduler.PushOgcd(AstraeaAbilities.CelestialIntersection, target.GameObjectId, priority: Priority, onDispatched: planned("Celestial Intersection"));
            return;
        }

        if (!isMoving && level >= ASTActions.BeneficII.MinLevel)
            scheduler.PushGcd(AstraeaAbilities.BeneficII, target.GameObjectId, priority: Priority, onDispatched: planned("Benefic II"));
        else if (isMoving && level >= ASTActions.AspectedBenefic.MinLevel)
            scheduler.PushGcd(AstraeaAbilities.AspectedBenefic, target.GameObjectId, priority: Priority, onDispatched: planned("Aspected Benefic"));
    }
}
