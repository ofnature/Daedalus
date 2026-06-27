using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Daedalus.Data;
using Daedalus.Services.Action;

namespace Daedalus.Rotation.Common.Helpers;

/// <summary>
/// Shared role-action behavior for physical ranged DPS (BRD/MCH/DNC).
/// </summary>
public static class RangedSharedHelper
{
    private const uint PelotonStatusId = 1199;

    /// <summary>
    /// Auto-casts Peloton out of combat while moving so the party travel-speed buff stays up between
    /// pulls. No-op in combat (the game cancels Peloton on engage), when disabled, when already buffed,
    /// while stationary, or when the action isn't learned/ready. Cast directly because the scheduler's
    /// oGCD dispatch and the damage modules are in-combat only.
    /// </summary>
    public static void TryCastPeloton(
        Configuration config,
        IActionService actionService,
        IPlayerCharacter player,
        bool inCombat,
        bool isMoving)
    {
        if (inCombat) return;
        if (!config.RangedShared.EnablePeloton) return;
        if (!isMoving) return;
        if (player.Level < RoleActions.Peloton.MinLevel) return;
        if (HasStatus(player, PelotonStatusId)) return;
        if (!actionService.IsActionReady(RoleActions.Peloton.ActionId)) return;

        actionService.ExecuteOgcd(RoleActions.Peloton, player.GameObjectId);
    }

    private static bool HasStatus(IBattleChara player, uint statusId)
    {
        var list = player.StatusList;
        if (list == null)
            return false;
        foreach (var status in list)
        {
            if (status != null && status.StatusId == statusId)
                return true;
        }
        return false;
    }
}
