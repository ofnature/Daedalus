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

            if ((npc!.StatusFlags & StatusFlags.InCombat) != 0)
                return true;
        }

        return false;
    }
}
