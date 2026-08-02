using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;

namespace Daedalus.Rotation.Common.Helpers;

/// <summary>
/// Detects whether allies are in combat so the rotation can assist before the local player has aggro.
/// </summary>
internal static class PartyCombatHelper
{
    /// <summary>
    /// True when any party member (or Trust/Duty Support ally) other than the player has <see cref="StatusFlags.InCombat"/>.
    /// </summary>
    /// <summary>
    /// How close an ally's fight has to be before it counts as YOUR fight.
    /// <para>
    /// There was no limit at all, so one box pulling put every other box "in combat" anywhere in
    /// the zone — they then auto-engaged whatever hostile was within 25y of themselves, and
    /// healers started running heal logic, all while stood on the far side of the map. Field
    /// 2026-08-02. Generous enough to cover a real pull spread over a large arena, far short of
    /// a zone.
    /// </para>
    /// </summary>
    public const float PartyCombatRadiusYalms = 50f;

    public static bool IsAnyGroupMemberInCombat(
        IPlayerCharacter player,
        IPartyList partyList,
        IObjectTable objectTable)
    {
        if (partyList.Length > 0)
        {
            foreach (var member in partyList)
            {
                if (member.EntityId == player.EntityId)
                    continue;

                if (objectTable.SearchByEntityId(member.EntityId) is not IBattleChara chara)
                    continue;

                if (chara.IsDead)
                    continue;

                if (!IsWithinPartyCombatRadius(player, chara))
                    continue;

                if ((chara.StatusFlags & StatusFlags.InCombat) != 0)
                    return true;
            }

            return false;
        }

        // Trust / duty companion allies — PartyList is empty in Trust content.
        foreach (var obj in objectTable)
        {
            if (!BasePartyHelper.IsValidTrustNpc(obj, out var npc, includeDead: false))
                continue;

            // Same radius as the party path. Trust allies stay glued to you, so this rarely
            // changes anything — but leaving one path ungated is how they drift apart later.
            if (!IsWithinPartyCombatRadius(player, npc!))
                continue;

            if ((npc!.StatusFlags & StatusFlags.InCombat) != 0)
                return true;
        }

        return false;
    }

    /// <summary>An ally's fight only counts as yours if they are near enough to be in it.</summary>
    public static bool IsWithinPartyCombatRadius(IPlayerCharacter player, IBattleChara ally)
        => System.Numerics.Vector3.DistanceSquared(player.Position, ally.Position)
           <= PartyCombatRadiusYalms * PartyCombatRadiusYalms;
}
