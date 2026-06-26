using Dalamud.Game.ClientState.Objects.Types;
using Daedalus.Services.Tank;

namespace Daedalus.Rotation.Common.Helpers;

/// <summary>
/// Resolves main/off-tank role for tank rotations and debug UI.
/// </summary>
public static class TankRoleHelper
{
    /// <summary>
    /// Resolves whether the player is the main tank.
    /// Solo-tank duties (no co-tank in party) always return true unless overridden.
    /// Two-tank content uses enmity on the current target when available.
    /// </summary>
    public static bool ResolveIsMainTank(
        bool? isMainTankOverride,
        IBattleChara? currentTarget,
        uint playerEntityId,
        bool hasCoTank,
        IEnmityService enmityService)
    {
        if (isMainTankOverride.HasValue)
            return isMainTankOverride.Value;

        if (!hasCoTank)
            return true;

        return currentTarget != null && enmityService.IsMainTankOn(currentTarget, playerEntityId);
    }
}
