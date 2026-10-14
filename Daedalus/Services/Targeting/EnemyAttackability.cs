using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using Daedalus.Compat;
using Daedalus.Data;

namespace Daedalus.Services.Targeting;

/// <summary>
/// Filters battle NPCs that look hostile but cannot be damaged (escort/protect objectives, etc.).
/// Uses the game's action-target validation rather than StatusFlags alone.
/// </summary>
public static class EnemyAttackability
{
    /// <summary>
    /// Probe actions covering melee and ranged single-target damage validation.
    /// </summary>
    private static readonly uint[] DamageProbeActionIds =
    [
        ActionIds.HeavySwing,
        BLMActions.Blizzard.ActionId,
    ];

    public static bool IsExcludedBattleNpcKind(IBattleNpc npc)
    {
        var kind = (byte)npc.BattleNpcKind;
        return kind is BattleNpcKinds.Pet
            or BattleNpcKinds.Chocobo
            or BattleNpcKinds.NpcPartyMember;
    }

    /// <summary>
    /// Is this battle NPC of a kind that can be an enemy at all: an ordinary combatant, a part of a boss, or the
    /// unclassified kind quest and overworld mobs report.
    /// <para>A boss's part is a target of its own. The Lost City of Amdapor, 2026-10-11: the Voracious Maw that
    /// holds whoever the Decaying Gourmand ate is one, every enemy filter turned it away, and with the Maw
    /// targeted by hand the weaponskills still went to the boss while a party member sat inside for 12.8s.</para>
    /// <para>Kind only. Whether it can be hit is still <see cref="IsPlayerAttackable"/>'s question.</para>
    /// </summary>
    public static bool IsHostileBattleNpcKind(IBattleNpc npc)
        => (byte)npc.BattleNpcKind is BattleNpcKinds.Combatant or BattleNpcKinds.Part || npc.SubKind == 0;

    /// <summary>
    /// Probe-free hostility check for MOVEMENT decisions: a live, targetable, hostile-kind
    /// BattleNpc is enough to walk toward. Deliberately does NOT run the
    /// <see cref="CanUseDamageActionOnTarget"/> probe — CanUseActionOnTarget false-negatives
    /// while the mob is still out of range (field-verified twice: farm-mode approach, melee
    /// max-melee walk-in), so gating movement on it parks the character outside melee.
    /// Combat targeting keeps the probe (story-ally exclusion needs it at dispatch range).
    /// </summary>
    public static bool IsMovementApproachable(IGameObject? target)
    {
        if (target is not IBattleNpc npc)
            return false;

        if (IsExcludedBattleNpcKind(npc))
            return false;

        if (!IsHostileBattleNpcKind(npc))
            return false;

        return npc.IsTargetable && !npc.IsDead;
    }

    /// <summary>
    /// True when the player can use a damage action on this battle NPC.
    /// </summary>
    public static bool IsPlayerAttackable(IGameObject target)
    {
        if (target is not IBattleNpc npc)
            return false;

        if (IsExcludedBattleNpcKind(npc))
            return false;

        if (!target.IsTargetable || target.IsDead)
            return false;

        return CanUseDamageActionOnTarget(target);
    }

    private static unsafe bool CanUseDamageActionOnTarget(IGameObject target)
    {
        try
        {
            var targetStruct = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)target.Address;
            if (targetStruct == null)
                return false;

            foreach (var actionId in DamageProbeActionIds)
            {
                if (ActionManager.CanUseActionOnTarget(actionId, targetStruct))
                    return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}
