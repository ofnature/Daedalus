using System.Linq;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;

namespace Daedalus.Services.Beastmaster;

/// <summary>
/// The Beastmaster's summoned familiar, read from what is actually in the world. One place, so the rotation
/// and the Debug window cannot disagree about whether a beast is out — which they did: the window kept its
/// own "which horn is out" flag that only a Battlehorn cast it happened to witness could set, so it read
/// "none out" with a Ziz standing next to the player (field 2026-10-11).
/// </summary>
public static class FamiliarLookup
{
    /// <summary>
    /// "Communing with the wilds. Able to execute Tempered Release." Granted by a summon; Tempered Release
    /// and Borrow both require it, so its presence is the game's own answer to "is the release still unspent".
    /// </summary>
    public const uint OneWithNatureStatusId = 4601;

    /// <summary>The familiar is an owned pet in the object table, the same shape as a Scholar's fairy.</summary>
    public static IBattleNpc? Find(IObjectTable objectTable, IPlayerCharacter player)
        => objectTable
            .OfType<IBattleNpc>()
            .FirstOrDefault(npc => npc.OwnerId == player.EntityId && npc.BattleNpcKind == BattleNpcSubKind.Pet);

    public static bool HasOneWithNature(IPlayerCharacter player)
    {
        if (player.StatusList is null)
            return false;

        foreach (var status in player.StatusList)
        {
            if (status != null && status.StatusId == OneWithNatureStatusId)
                return true;
        }

        return false;
    }
}
